using System.Data;
using System.Globalization;
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
                .RequireAuthorization("AdminOrPortasOrIgazgato");

            group.MapGet("/recipients", async (
                int kulcsId,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var key = await db.Kulcsok
                    .AsNoTracking()
                    .Where(item => item.Id == kulcsId && item.Aktivalva)
                    .Select(item => new { item.Id, item.Tipus })
                    .SingleOrDefaultAsync(cancellationToken);
                if (key is null)
                {
                    return Results.NotFound(new { message = "A kulcs nem található vagy nem aktív." });
                }

                var recipients = db.Felhasznalok
                    .AsNoTracking()
                    .Where(user => user.Role == RoleNames.Oktato);
                if (key.Tipus == "Mesterkulcs")
                {
                    recipients = recipients.Where(user => db.MesterkulcsJogosultsagok.Any(
                        access => access.KulcsId == key.Id && access.FelhasznaloId == user.Id));
                }

                var results = await recipients
                    .OrderBy(user => user.Nev)
                    .Select(user => new KeyRecipientDto(user.Id, user.Nev, user.Username))
                    .ToListAsync(cancellationToken);

                return Results.Ok(results);
            })
            .WithName("GetKeyRecipients")
            .WithSummary("A kiválasztott kulcs átvevőjeként jogosult oktatók")
            .RequireAuthorization("Portas")
            .Produces<List<KeyRecipientDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // KM1 – POST /api/key-movements/issue
            group.MapPost("/issue", async (
                IssueKeyRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller,
                TimeProvider timeProvider) =>
            {
                var validationError = ValidateMovementRequest(request.Pin, request.TeremAllapot, request.SerulesLeiras);
                if (validationError is not null)
                {
                    return Results.BadRequest(new { message = validationError });
                }

                var operatorId = GetCallerId(caller);
                if (operatorId is null ||
                    !await db.Felhasznalok.AnyAsync(user => user.Id == operatorId.Value))
                {
                    return Results.Unauthorized();
                }

                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await SqlServerLocking.AcquireAsync(db, $"key-issue:{request.KulcsId}");

                var key = await db.Kulcsok
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
                    .ThenByDescending(m => m.Id)
                    .FirstOrDefault();

                if (lastMovement != null && (lastMovement.MozgasTipus == "Kiadás" || lastMovement.MozgasTipus == "Kiadas"))
                {
                    return Results.Conflict(new { message = "A kulcs jelenleg ki van adva, előbb vissza kell venni." });
                }

                int recipientId;
                string recipientName;
                string? recipientPinHash;
                if (request.FoglalasId.HasValue)
                {
                    var booking = await db.Foglalasok
                        .Include(item => item.Felhasznalo)
                        .FirstOrDefaultAsync(item => item.Id == request.FoglalasId.Value);
                    if (booking is null)
                    {
                        return Results.NotFound(new { message = $"A(z) {request.FoglalasId.Value} azonosítójú foglalás nem található." });
                    }
                    if (booking.Status != "Aktiv")
                    {
                        return Results.BadRequest(new { message = $"A foglalás nem aktív (jelenlegi státusz: {booking.Status})." });
                    }
                    if (booking.Felhasznalo.Role != RoleNames.Oktato)
                    {
                        return Results.BadRequest(new { message = "Kulcs átvevője csak oktató lehet." });
                    }
                    if (request.AtvevoFelhasznaloId.HasValue &&
                        request.AtvevoFelhasznaloId.Value != booking.FelhasznaloId)
                    {
                        return Results.BadRequest(new { message = "A foglaláshoz tartozó átvevő nem módosítható." });
                    }

                    if (booking.TeremId != key.TeremId)
                    {
                        return Results.BadRequest(new { message = "A foglalás nem ehhez a kulcshoz tartozó teremre szól." });
                    }

                    recipientId = booking.FelhasznaloId;
                    recipientName = booking.Felhasznalo.Nev;
                    recipientPinHash = booking.Felhasznalo.PinHash;
                }
                else
                {
                    if (!request.AtvevoFelhasznaloId.HasValue)
                    {
                        return Results.BadRequest(new
                        {
                            message = "Foglalás nélküli kulcskiadásnál az átvevő oktató azonosítója kötelező."
                        });
                    }

                    var recipient = await db.Felhasznalok
                        .Where(user => user.Id == request.AtvevoFelhasznaloId.Value)
                        .Select(user => new { user.Id, user.Nev, user.Role, user.PinHash })
                        .SingleOrDefaultAsync();
                    if (recipient is null)
                    {
                        return Results.NotFound(new { message = "A megadott átvevő nem található." });
                    }
                    if (recipient.Role != RoleNames.Oktato)
                    {
                        return Results.BadRequest(new { message = "Kulcs átvevője csak oktató lehet." });
                    }
                    if (key.Tipus == "Mesterkulcs" &&
                        !await db.MesterkulcsJogosultsagok.AnyAsync(access =>
                            access.KulcsId == key.Id && access.FelhasznaloId == recipient.Id))
                    {
                        return Results.Forbid();
                    }

                    recipientId = recipient.Id;
                    recipientName = recipient.Nev;
                    recipientPinHash = recipient.PinHash;
                }

                if (string.IsNullOrWhiteSpace(recipientPinHash))
                {
                    return Results.Conflict(new { message = "Az átvevő még nem állított be PIN-kódot." });
                }
                if (!BCrypt.Net.BCrypt.Verify(request.Pin, recipientPinHash))
                {
                    return Results.Forbid();
                }
                if (key.Tipus == "Mesterkulcs" &&
                    !await db.MesterkulcsJogosultsagok.AnyAsync(access =>
                        access.KulcsId == key.Id && access.FelhasznaloId == recipientId))
                {
                    return Results.Forbid();
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var movement = new Kulcsmozgas
                {
                    KulcsId = key.Id,
                    FoglalasId = request.FoglalasId,
                    AtvevoFelhasznaloId = recipientId,
                    MozgasTipus = "Kiadás",
                    Idopont = now,
                    OperátorId = operatorId.Value,
                    Megjegyzes = request.Megjegyzes ?? string.Empty,
                    TeremAllapot = request.TeremAllapot.Trim(),
                    SerulesLeiras = NormalizeDamageDescription(request.SerulesLeiras),
                    CreatedAt = now
                };

                db.Kulcsmozgasok.Add(movement);
                var conditionTicket = AddConditionTicket(
                    db,
                    operatorId.Value,
                    key.TeremId,
                    movement.SerulesLeiras,
                    now);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "KEY_ISSUE",
                    entitasTipus: "Kulcsmozgas",
                    entitasId: movement.Id.ToString(),
                    ujErtek: $"KulcsId: {key.Id}, FoglalasId: {request.FoglalasId}, AtvevoFelhasznaloId: {recipientId}, TeremAllapot: {movement.TeremAllapot}, SerulesLeiras: {movement.SerulesLeiras}");
                if (conditionTicket is not null)
                {
                    await auditService.LogAsync(
                        caller, "CREATE", "Hibajegy", conditionTicket.Id.ToString(),
                        ujErtek: $"TeremId: {conditionTicket.TeremId}; Kategoria: {conditionTicket.Kategoria}; KulcsmozgasId: {movement.Id}");
                }
                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }

                var opUser = await db.Felhasznalok.FindAsync(operatorId.Value);

                var result = new KeyMovementItemDto
                {
                    Id = movement.Id,
                    KulcsId = key.Id,
                    KulcsSzam = key.Szam,
                    MozgasTipus = movement.MozgasTipus,
                    Idopont = movement.Idopont,
                    FoglalasId = movement.FoglalasId,
                    FoglalasFelhasznaloNev = request.FoglalasId.HasValue ? recipientName : null,
                    AtvevoFelhasznaloId = recipientId,
                    AtvevoNev = recipientName,
                    OperatorId = operatorId.Value,
                    OperatorNev = opUser?.Nev ?? string.Empty,
                    Megjegyzes = movement.Megjegyzes,
                    TeremAllapot = movement.TeremAllapot,
                    SerulesLeiras = movement.SerulesLeiras
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
                ClaimsPrincipal caller,
                TimeProvider timeProvider) =>
            {
                var validationError = ValidateMovementRequest(request.Pin, request.TeremAllapot, request.SerulesLeiras);
                if (validationError is not null)
                {
                    return Results.BadRequest(new { message = validationError });
                }

                var operatorId = GetCallerId(caller);
                if (operatorId is null ||
                    !await db.Felhasznalok.AnyAsync(user => user.Id == operatorId.Value))
                {
                    return Results.Unauthorized();
                }

                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await SqlServerLocking.AcquireAsync(db, $"key-issue:{request.KulcsId}");

                var key = await db.Kulcsok
                    .Include(k => k.Kulcsmozgasok)
                    .FirstOrDefaultAsync(k => k.Id == request.KulcsId);

                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {request.KulcsId} azonosítójú kulcs nem található." });
                }

                var lastMovement = key.Kulcsmozgasok?
                    .OrderByDescending(m => m.Idopont)
                    .ThenByDescending(m => m.Id)
                    .FirstOrDefault();

                if (lastMovement == null || (lastMovement.MozgasTipus != "Kiadás" && lastMovement.MozgasTipus != "Kiadas"))
                {
                    return Results.Conflict(new { message = "A kulcs jelenleg nincs kiadva, nem lehet visszavenni." });
                }

                var recipientId = lastMovement.AtvevoFelhasznaloId;
                if (!recipientId.HasValue && lastMovement.FoglalasId.HasValue)
                {
                    recipientId = await db.Foglalasok
                        .Where(booking => booking.Id == lastMovement.FoglalasId.Value)
                        .Select(booking => (int?)booking.FelhasznaloId)
                        .SingleOrDefaultAsync();
                }

                if (!recipientId.HasValue)
                {
                    return Results.Conflict(new { message = "A kulcs korábbi átvevője nem azonosítható, ezért PIN-ellenőrzéssel nem vehető vissza." });
                }
                var recipientPinHash = await db.Felhasznalok
                    .Where(user => user.Id == recipientId.Value)
                    .Select(user => user.PinHash)
                    .SingleOrDefaultAsync();
                if (string.IsNullOrWhiteSpace(recipientPinHash))
                {
                    return Results.Conflict(new { message = "Az átvevő még nem állított be PIN-kódot." });
                }
                if (!BCrypt.Net.BCrypt.Verify(request.Pin, recipientPinHash))
                {
                    return Results.Forbid();
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var returnMovement = new Kulcsmozgas
                {
                    KulcsId = key.Id,
                    FoglalasId = lastMovement.FoglalasId,
                    AtvevoFelhasznaloId = recipientId,
                    MozgasTipus = "Visszavétel",
                    Idopont = now,
                    OperátorId = operatorId.Value,
                    Megjegyzes = request.Megjegyzes ?? string.Empty,
                    TeremAllapot = request.TeremAllapot.Trim(),
                    SerulesLeiras = NormalizeDamageDescription(request.SerulesLeiras),
                    CreatedAt = now
                };

                db.Kulcsmozgasok.Add(returnMovement);
                var conditionTicket = AddConditionTicket(
                    db,
                    operatorId.Value,
                    key.TeremId,
                    returnMovement.SerulesLeiras,
                    now);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "KEY_RETURN",
                    entitasTipus: "Kulcsmozgas",
                    entitasId: returnMovement.Id.ToString(),
                    ujErtek: $"KulcsId: {key.Id}, AtvevoFelhasznaloId: {recipientId}, TeremAllapot: {returnMovement.TeremAllapot}, SerulesLeiras: {returnMovement.SerulesLeiras}");
                if (conditionTicket is not null)
                {
                    await auditService.LogAsync(
                        caller, "CREATE", "Hibajegy", conditionTicket.Id.ToString(),
                        ujErtek: $"TeremId: {conditionTicket.TeremId}; Kategoria: {conditionTicket.Kategoria}; KulcsmozgasId: {returnMovement.Id}");
                }
                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }
                var opUser = await db.Felhasznalok.FindAsync(operatorId.Value);
                var recipientName = recipientId.HasValue
                    ? await db.Felhasznalok
                        .Where(user => user.Id == recipientId.Value)
                        .Select(user => user.Nev)
                        .SingleOrDefaultAsync()
                    : null;

                var result = new KeyMovementItemDto
                {
                    Id = returnMovement.Id,
                    KulcsId = key.Id,
                    KulcsSzam = key.Szam,
                    MozgasTipus = returnMovement.MozgasTipus,
                    Idopont = returnMovement.Idopont,
                    FoglalasId = returnMovement.FoglalasId,
                    AtvevoFelhasznaloId = recipientId,
                    AtvevoNev = recipientName,
                    OperatorId = operatorId.Value,
                    OperatorNev = opUser?.Nev ?? string.Empty,
                    Megjegyzes = returnMovement.Megjegyzes,
                    TeremAllapot = returnMovement.TeremAllapot,
                    SerulesLeiras = returnMovement.SerulesLeiras
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
                    .Include(km => km.AtvevoFelhasznalo)
                    .Include(km => km.Foglalas)
                        .ThenInclude(f => f!.Felhasznalo)
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
                        AtvevoFelhasznaloId = km.AtvevoFelhasznaloId,
                        AtvevoNev = km.AtvevoFelhasznalo != null
                            ? km.AtvevoFelhasznalo.Nev
                            : km.Foglalas != null && km.Foglalas.Felhasznalo != null
                                ? km.Foglalas.Felhasznalo.Nev
                                : null,
                        OperatorId = km.OperátorId,
                        OperatorNev = km.Operátor != null ? km.Operátor.Nev : string.Empty,
                        Megjegyzes = km.Megjegyzes ?? string.Empty,
                        TeremAllapot = km.TeremAllapot,
                        SerulesLeiras = km.SerulesLeiras
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
                    .Include(k => k.AtvevoFelhasznalo)
                    .Include(k => k.Foglalas)
                        .ThenInclude(f => f!.Felhasznalo)
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
                    AtvevoFelhasznaloId = km.AtvevoFelhasznaloId,
                    AtvevoNev = km.AtvevoFelhasznalo?.Nev ?? km.Foglalas?.Felhasznalo?.Nev,
                    OperatorId = km.OperátorId,
                    OperatorNev = km.Operátor != null ? km.Operátor.Nev : string.Empty,
                    Megjegyzes = km.Megjegyzes ?? string.Empty,
                    TeremAllapot = km.TeremAllapot,
                    SerulesLeiras = km.SerulesLeiras
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
            group.MapGet("/overdue", async (
                AppDbContext db,
                IConfiguration configuration,
                ILogger<Program> logger,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var closingTime = await AutomationParameterReader.GetClosingTimeAsync(
                    db, logger, cancellationToken);
                var timeZone = AutomationParameterReader.GetTimeZone(configuration, logger);

                // Load all active keys with their movements, room and bookings
                var keysWithLastMovement = await db.Kulcsok
                    .AsNoTracking()
                    .Where(k => k.Aktivalva)
                    .Include(k => k.Terem)
                    .Include(k => k.Kulcsmozgasok)
                        .ThenInclude(km => km.AtvevoFelhasznalo)
                    .Include(k => k.Kulcsmozgasok)
                        .ThenInclude(km => km.Foglalas)
                            .ThenInclude(f => f!.Felhasznalo)
                    .ToListAsync();

                var overdueList = new List<OverdueKeyDto>();

                foreach (var key in keysWithLastMovement)
                {
                    var last = key.Kulcsmozgasok?
                        .OrderByDescending(m => m.Idopont)
                        .ThenByDescending(m => m.Id)
                        .FirstOrDefault();

                    if (last != null && (last.MozgasTipus == "Kiadás" || last.MozgasTipus == "Kiadas"))
                    {
                        var deadline = KeyReturnDeadlineCalculator.GetDeadlineUtc(
                            last.Idopont,
                            last.Foglalas?.EndTime,
                            closingTime,
                            timeZone);
                        if (now > deadline)
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
                                BirtokosNev = last.AtvevoFelhasznalo?.Nev ?? last.Foglalas?.Felhasznalo?.Nev,
                                BirtokosEmail = last.AtvevoFelhasznalo?.Email ?? last.Foglalas?.Felhasznalo?.Email,
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
            var subject = caller.FindFirstValue("sub") ??
                          caller.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) &&
                   userId > 0
                ? userId
                : null;
        }

        private static string? ValidateMovementRequest(
            string? pin,
            string? roomCondition,
            string? damageDescription)
        {
            if (string.IsNullOrWhiteSpace(pin) ||
                pin.Length is < 4 or > 8 ||
                pin.Any(character => character is < '0' or > '9'))
            {
                return "Érvényes, 4–8 számjegyű PIN-kód szükséges.";
            }

            if (string.IsNullOrWhiteSpace(roomCondition) || roomCondition.Trim().Length > 500)
            {
                return "A terem állapotának rögzítése kötelező, legfeljebb 500 karakterben.";
            }

            if (damageDescription?.Length > 1000)
            {
                return "A sérülés leírása legfeljebb 1000 karakter lehet.";
            }

            return null;
        }

        private static string? NormalizeDamageDescription(string? damageDescription) =>
            string.IsNullOrWhiteSpace(damageDescription) ? null : damageDescription.Trim();

        private static Hibajegy? AddConditionTicket(
            AppDbContext db,
            int operatorId,
            int roomId,
            string? damageDescription,
            DateTime createdAt)
        {
            if (string.IsNullOrWhiteSpace(damageDescription))
            {
                return null;
            }

            var ticket = new Hibajegy
            {
                FelhasznaloId = operatorId,
                TeremId = roomId,
                Kategoria = "Sérülés",
                Leiras = damageDescription,
                Statusz = "Nyitva",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };
            db.Hibajegyek.Add(ticket);
            return ticket;
        }

        private sealed record KeyRecipientDto(int Id, string Nev, string Username);
    }
}
