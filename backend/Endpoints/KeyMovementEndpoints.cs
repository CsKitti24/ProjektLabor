using System.Data;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs.KeyMovements;
using ProjektLabor.DTOs.Keys;
using ProjektLabor.Models;
using ProjektLabor.Security;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints
{
    public static class KeyMovementEndpoints
    {
        public static RouteGroupBuilder MapKeyMovementEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/key-movements")
                .WithTags("KeyMovements")
                .RequireAuthorization("Authenticated");

            // KM1 – POST /api/key-movements/issue
            group.MapPost("/issue", async (
                IssueKeyRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var operatorId = GetCallerId(caller);
                if (operatorId is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var key = await db.Kulcsok
                    .Include(k => k.Terem)
                    .Include(k => k.Kulcsmozgasok)
                    .FirstOrDefaultAsync(k => k.Id == request.KulcsId);

                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {request.KulcsId} azonosítójú kulcs nem található." });
                }

                if (!key.Aktivalva)
                {
                    return Results.BadRequest(new { message = "Deaktivált kulcs nem adható ki." });
                }

                // Check if key is already issued
                var lastMovement = key.Kulcsmozgasok?
                    .OrderByDescending(m => m.Idopont)
                    .FirstOrDefault();

                if (lastMovement != null && (lastMovement.MozgasTipus == "Kiadás" || lastMovement.MozgasTipus == "Kiadas"))
                {
                    return Results.Conflict(new { message = "A kulcs jelenleg ki van adva, előbb vissza kell venni." });
                }

                // Check booking if provided
                if (request.FoglalasId.HasValue)
                {
                    var booking = await db.Foglalasok
                        .Include(f => f.Felhasznalo)
                        .FirstOrDefaultAsync(f => f.Id == request.FoglalasId.Value);
                    if (booking is null)
                    {
                        return Results.NotFound(new { message = $"A(z) {request.FoglalasId.Value} azonosítójú foglalás nem található." });
                    }
                    if (booking.Status != "Aktiv")
                    {
                        return Results.BadRequest(new { message = $"A foglalás nem aktív (jelenlegi státusz: {booking.Status})." });
                    }

                    if (booking.TeremId != key.TeremId)
                    {
                        return Results.BadRequest(new { message = "A foglalás nem ehhez a kulcshoz tartozó teremre szól." });
                    }

                    var isAuthorizedRecipient =
                        booking.Felhasznalo?.Role == RoleNames.Oktato &&
                        (key.Tipus != "Mesterkulcs" ||
                         await db.MesterkulcsJogosultsagok.AnyAsync(access =>
                             access.KulcsId == key.Id && access.FelhasznaloId == booking.FelhasznaloId));
                    if (!isAuthorizedRecipient)
                    {
                        return Results.Forbid();
                    }
                }

                var movement = new Kulcsmozgas
                {
                    KulcsId = key.Id,
                    FoglalasId = request.FoglalasId,
                    MozgasTipus = "Kiadás",
                    Idopont = DateTime.UtcNow,
                    OperátorId = operatorId.Value,
                    Megjegyzes = request.Megjegyzes ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                db.Kulcsmozgasok.Add(movement);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "KEY_ISSUE",
                    entitasTipus: "Kulcsmozgas",
                    entitasId: movement.Id.ToString(),
                    ujErtek: $"KulcsId: {key.Id}, FoglalasId: {request.FoglalasId}");
                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }

                var opUser = await db.Felhasznalok.FindAsync(operatorId.Value);
                var bookingUser = request.FoglalasId.HasValue
                    ? (await db.Foglalasok.Include(f => f.Felhasznalo).FirstOrDefaultAsync(f => f.Id == request.FoglalasId.Value))?.Felhasznalo?.Nev
                    : null;

                var result = new KeyMovementItemDto
                {
                    Id = movement.Id,
                    KulcsId = key.Id,
                    KulcsSzam = key.Szam,
                    MozgasTipus = movement.MozgasTipus,
                    Idopont = movement.Idopont,
                    FoglalasId = movement.FoglalasId,
                    FoglalasFelhasznaloNev = bookingUser,
                    OperatorId = operatorId.Value,
                    OperatorNev = opUser?.Nev ?? string.Empty,
                    Megjegyzes = movement.Megjegyzes
                };

                return Results.Created($"/api/key-movements/{movement.Id}", result);
            })
            .WithName("IssueKey")
            .WithSummary("Kulcs kiadásának rögzítése")
            .RequireAuthorization("Portas")
            .Produces<KeyMovementItemDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // KM2 – POST /api/key-movements/return
            group.MapPost("/return", async (
                ReturnKeyRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var operatorId = GetCallerId(caller);
                if (operatorId is null)
                {
                    return Results.Unauthorized();
                }

                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var key = await db.Kulcsok
                    .Include(k => k.Kulcsmozgasok)
                    .FirstOrDefaultAsync(k => k.Id == request.KulcsId);

                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {request.KulcsId} azonosítójú kulcs nem található." });
                }

                var lastMovement = key.Kulcsmozgasok?
                    .OrderByDescending(m => m.Idopont)
                    .FirstOrDefault();

                if (lastMovement == null || (lastMovement.MozgasTipus != "Kiadás" && lastMovement.MozgasTipus != "Kiadas"))
                {
                    return Results.Conflict(new { message = "A kulcs jelenleg nincs kiadva, nem lehet visszavenni." });
                }

                var returnMovement = new Kulcsmozgas
                {
                    KulcsId = key.Id,
                    FoglalasId = lastMovement.FoglalasId,
                    MozgasTipus = "Visszavétel",
                    Idopont = DateTime.UtcNow,
                    OperátorId = operatorId.Value,
                    Megjegyzes = request.Megjegyzes ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                db.Kulcsmozgasok.Add(returnMovement);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "KEY_RETURN",
                    entitasTipus: "Kulcsmozgas",
                    entitasId: returnMovement.Id.ToString(),
                    ujErtek: $"KulcsId: {key.Id}");
                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }

                var opUser = await db.Felhasznalok.FindAsync(operatorId.Value);

                var result = new KeyMovementItemDto
                {
                    Id = returnMovement.Id,
                    KulcsId = key.Id,
                    KulcsSzam = key.Szam,
                    MozgasTipus = returnMovement.MozgasTipus,
                    Idopont = returnMovement.Idopont,
                    FoglalasId = returnMovement.FoglalasId,
                    OperatorId = operatorId.Value,
                    OperatorNev = opUser?.Nev ?? string.Empty,
                    Megjegyzes = returnMovement.Megjegyzes
                };

                return Results.Ok(result);
            })
            .WithName("ReturnKey")
            .WithSummary("Kulcs visszavételének rögzítése")
            .RequireAuthorization("Portas")
            .Produces<KeyMovementItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // KM3 – GET /api/key-movements
            group.MapGet("/", async (
                int? kulcsId,
                string? mozgasTipus,
                DateTime? tol,
                DateTime? ig,
                int page,
                int pageSize,
                AppDbContext db) =>
            {
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);

                var query = db.Kulcsmozgasok
                    .AsNoTracking()
                    .Include(km => km.Kulcs)
                    .Include(km => km.Operátor)
                    .Include(km => km.Foglalas)
                        .ThenInclude(f => f.Felhasznalo)
                    .AsQueryable();

                if (kulcsId.HasValue)
                {
                    query = query.Where(km => km.KulcsId == kulcsId.Value);
                }

                if (!string.IsNullOrWhiteSpace(mozgasTipus))
                {
                    query = query.Where(km => km.MozgasTipus == mozgasTipus);
                }

                if (tol.HasValue)
                {
                    query = query.Where(km => km.Idopont >= tol.Value);
                }

                if (ig.HasValue)
                {
                    query = query.Where(km => km.Idopont <= ig.Value);
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(km => km.Idopont)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(km => new KeyMovementItemDto
                    {
                        Id = km.Id,
                        KulcsId = km.KulcsId,
                        KulcsSzam = km.Kulcs != null ? km.Kulcs.Szam : string.Empty,
                        MozgasTipus = km.MozgasTipus,
                        Idopont = km.Idopont,
                        FoglalasId = km.FoglalasId,
                        FoglalasFelhasznaloNev = km.Foglalas != null && km.Foglalas.Felhasznalo != null
                            ? km.Foglalas.Felhasznalo.Nev
                            : null,
                        OperatorId = km.OperátorId,
                        OperatorNev = km.Operátor != null ? km.Operátor.Nev : string.Empty,
                        Megjegyzes = km.Megjegyzes
                    })
                    .ToListAsync();

                return Results.Ok(new KeyMovementListDto
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            })
            .WithName("GetKeyMovementsList")
            .WithSummary("Kulcsmozgások listázása szűrőkkel és lapozással")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<KeyMovementListDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // KM4 – GET /api/key-movements/{id}
            group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            {
                var km = await db.Kulcsmozgasok
                    .AsNoTracking()
                    .Include(k => k.Kulcs)
                    .Include(k => k.Operátor)
                    .Include(k => k.Foglalas)
                        .ThenInclude(f => f.Felhasznalo)
                    .FirstOrDefaultAsync(k => k.Id == id);

                if (km is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú kulcsmozgás nem található." });
                }

                var dto = new KeyMovementItemDto
                {
                    Id = km.Id,
                    KulcsId = km.KulcsId,
                    KulcsSzam = km.Kulcs != null ? km.Kulcs.Szam : string.Empty,
                    MozgasTipus = km.MozgasTipus,
                    Idopont = km.Idopont,
                    FoglalasId = km.FoglalasId,
                    FoglalasFelhasznaloNev = km.Foglalas != null && km.Foglalas.Felhasznalo != null
                        ? km.Foglalas.Felhasznalo.Nev
                        : null,
                    OperatorId = km.OperátorId,
                    OperatorNev = km.Operátor != null ? km.Operátor.Nev : string.Empty,
                    Megjegyzes = km.Megjegyzes
                };

                return Results.Ok(dto);
            })
            .WithName("GetKeyMovementById")
            .WithSummary("Egy kulcsmozgás részleteinek lekérdezése")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<KeyMovementItemDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // KM5 – GET /api/key-movements/overdue
            group.MapGet("/overdue", async (AppDbContext db) =>
            {
                var now = DateTime.UtcNow;

                // Load all active keys with their movements, room and bookings
                var keysWithLastMovement = await db.Kulcsok
                    .AsNoTracking()
                    .Where(k => k.Aktivalva)
                    .Include(k => k.Terem)
                    .Include(k => k.Kulcsmozgasok)
                        .ThenInclude(km => km.Foglalas)
                            .ThenInclude(f => f.Felhasznalo)
                    .ToListAsync();

                var overdueList = new List<OverdueKeyDto>();

                foreach (var key in keysWithLastMovement)
                {
                    var last = key.Kulcsmozgasok?
                        .OrderByDescending(m => m.Idopont)
                        .FirstOrDefault();

                    if (last != null && (last.MozgasTipus == "Kiadás" || last.MozgasTipus == "Kiadas"))
                    {
                        bool isOverdue = false;
                        DateTime deadline = last.Idopont.Date.AddHours(22); // Default closing time: 22:00

                        if (last.Foglalas != null)
                        {
                            deadline = last.Foglalas.EndTime;
                            if (now > deadline)
                            {
                                isOverdue = true;
                            }
                        }
                        else
                        {
                            // If issued without booking, overdue if after 22:00 of the issue day
                            if (now > deadline)
                            {
                                isOverdue = true;
                            }
                        }

                        if (isOverdue)
                        {
                            overdueList.Add(new OverdueKeyDto
                            {
                                KulcsId = key.Id,
                                KulcsSzam = key.Szam,
                                TeremId = key.TeremId,
                                TeremNev = key.Terem?.Nev ?? string.Empty,
                                KiadasIdeje = last.Idopont,
                                FoglalasId = last.FoglalasId,
                                FoglalasVége = last.Foglalas?.EndTime,
                                BirtokosNev = last.Foglalas?.Felhasznalo?.Nev,
                                BirtokosEmail = last.Foglalas?.Felhasznalo?.Email,
                                KésésPercekben = Math.Round((now - deadline).TotalMinutes, 1)
                            });
                        }
                    }
                }

                return Results.Ok(overdueList.OrderByDescending(o => o.KésésPercekben).ToList());
            })
            .WithName("GetOverdueKeys")
            .WithSummary("Lejárt vagy le nem adott kulcsok listázása")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<List<OverdueKeyDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            return group;
        }

        private static int? GetCallerId(ClaimsPrincipal caller)
        {
            var subject = caller.FindFirst("sub")?.Value
                ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(subject, out var userId) && userId > 0 ? userId : null;
        }
    }
}
