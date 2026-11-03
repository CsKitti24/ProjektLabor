using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Keys;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints
{
    public static class KeyEndpoints
    {
        public static RouteGroupBuilder MapKeyEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/keys")
                .WithTags("Keys")
                .RequireAuthorization("Authenticated");

            // K1 – GET /api/keys
            group.MapGet("/", async (
                int? teremId,
                string? tipus,
                bool? aktivalva,
                AppDbContext db) =>
            {
                var query = db.Kulcsok
                    .AsNoTracking()
                    .Include(k => k.Terem)
                    .Include(k => k.Kulcsmozgasok)
                    .AsQueryable();

                if (teremId.HasValue)
                {
                    query = query.Where(k => k.TeremId == teremId.Value);
                }

                if (!string.IsNullOrWhiteSpace(tipus))
                {
                    query = query.Where(k => k.Tipus == tipus);
                }

                if (aktivalva.HasValue)
                {
                    query = query.Where(k => k.Aktivalva == aktivalva.Value);
                }

                var list = await query
                    .OrderBy(k => k.Szam)
                    .ToListAsync();

                var results = list.Select(k =>
                {
                    var lastMovement = k.Kulcsmozgasok?
                        .OrderByDescending(km => km.Idopont)
                        .FirstOrDefault();

                    string allapot;
                    if (!k.Aktivalva)
                    {
                        allapot = "Deaktivalt";
                    }
                    else if (lastMovement != null && (lastMovement.MozgasTipus == "Kiadás" || lastMovement.MozgasTipus == "Kiadas"))
                    {
                        allapot = "Kiadva";
                    }
                    else
                    {
                        allapot = "Szabad";
                    }

                    return new KeyDto
                    {
                        Id = k.Id,
                        Szam = k.Szam,
                        Tipus = k.Tipus,
                        Aktivalva = k.Aktivalva,
                        TeremId = k.TeremId,
                        TeremNev = k.Terem?.Nev ?? string.Empty,
                        JelenlegiAllapot = allapot
                    };
                }).ToList();

                return Results.Ok(results);
            })
            .WithName("GetKeys")
            .WithSummary("Kulcsok listázása szűrőkkel")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<List<KeyDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // K2 – GET /api/keys/{id}
            group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            {
                var k = await db.Kulcsok
                    .AsNoTracking()
                    .Include(k => k.Terem)
                    .Include(k => k.Kulcsmozgasok)
                    .FirstOrDefaultAsync(k => k.Id == id);

                if (k is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú kulcs nem található." });
                }

                var lastMovement = k.Kulcsmozgasok?
                    .OrderByDescending(km => km.Idopont)
                    .FirstOrDefault();

                string allapot = !k.Aktivalva
                    ? "Deaktivalt"
                    : (lastMovement != null && (lastMovement.MozgasTipus == "Kiadás" || lastMovement.MozgasTipus == "Kiadas"))
                        ? "Kiadva"
                        : "Szabad";

                var dto = new KeyDto
                {
                    Id = k.Id,
                    Szam = k.Szam,
                    Tipus = k.Tipus,
                    Aktivalva = k.Aktivalva,
                    TeremId = k.TeremId,
                    TeremNev = k.Terem?.Nev ?? string.Empty,
                    JelenlegiAllapot = allapot
                };

                return Results.Ok(dto);
            })
            .WithName("GetKeyById")
            .WithSummary("Egy kulcs adatainak lekérdezése")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<KeyDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // K3 – POST /api/keys
            group.MapPost("/", async (
                CreateKeyRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                if (string.IsNullOrWhiteSpace(request.Szam) || string.IsNullOrWhiteSpace(request.Tipus))
                {
                    return Results.BadRequest(new { message = "A kulcsszám és típus megadása kötelező." });
                }

                bool roomExists = await db.Termek.AnyAsync(t => t.Id == request.TeremId);
                if (!roomExists)
                {
                    return Results.NotFound(new { message = $"A(z) {request.TeremId} azonosítójú terem nem található." });
                }

                bool numberExists = await db.Kulcsok.AnyAsync(k => k.Szam == request.Szam);
                if (numberExists)
                {
                    return Results.Conflict(new { message = $"A megadott kulcsszám ('{request.Szam}') már létezik." });
                }

                var key = new Kulcs
                {
                    Szam = request.Szam.Trim(),
                    Tipus = request.Tipus,
                    TeremId = request.TeremId,
                    Aktivalva = request.Aktivalva
                };

                db.Kulcsok.Add(key);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CREATE",
                    entitasTipus: "Kulcs",
                    entitasId: key.Id.ToString(),
                    ujErtek: $"Szam: {key.Szam}, Tipus: {key.Tipus}, TeremId: {key.TeremId}");

                var room = await db.Termek.FindAsync(key.TeremId);

                var result = new KeyDto
                {
                    Id = key.Id,
                    Szam = key.Szam,
                    Tipus = key.Tipus,
                    Aktivalva = key.Aktivalva,
                    TeremId = key.TeremId,
                    TeremNev = room?.Nev ?? string.Empty,
                    JelenlegiAllapot = key.Aktivalva ? "Szabad" : "Deaktivalt"
                };

                return Results.Created($"/api/keys/{key.Id}", result);
            })
            .WithName("CreateKey")
            .WithSummary("Új kulcs rögzítése")
            .RequireAuthorization("Adminisztrator")
            .Produces<KeyDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // K4 – PUT /api/keys/{id}
            group.MapPut("/{id:int}", async (
                int id,
                UpdateKeyRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var key = await db.Kulcsok.Include(k => k.Terem).FirstOrDefaultAsync(k => k.Id == id);
                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú kulcs nem található." });
                }

                var changes = new List<string>();

                if (!string.IsNullOrWhiteSpace(request.Szam) && request.Szam.Trim() != key.Szam)
                {
                    bool numberTaken = await db.Kulcsok.AnyAsync(k => k.Id != id && k.Szam == request.Szam.Trim());
                    if (numberTaken)
                    {
                        return Results.Conflict(new { message = $"A megadott kulcsszám ('{request.Szam.Trim()}') már használatban van." });
                    }
                    changes.Add($"Szam: '{key.Szam}' -> '{request.Szam.Trim()}'");
                    key.Szam = request.Szam.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.Tipus) && request.Tipus != key.Tipus)
                {
                    changes.Add($"Tipus: '{key.Tipus}' -> '{request.Tipus}'");
                    key.Tipus = request.Tipus;
                }

                if (request.TeremId.HasValue && request.TeremId.Value != key.TeremId)
                {
                    bool roomExists = await db.Termek.AnyAsync(t => t.Id == request.TeremId.Value);
                    if (!roomExists)
                    {
                        return Results.NotFound(new { message = $"A(z) {request.TeremId.Value} azonosítójú terem nem található." });
                    }
                    changes.Add($"TeremId: {key.TeremId} -> {request.TeremId.Value}");
                    key.TeremId = request.TeremId.Value;
                }

                if (request.Aktivalva.HasValue && request.Aktivalva.Value != key.Aktivalva)
                {
                    changes.Add($"Aktivalva: {key.Aktivalva} -> {request.Aktivalva.Value}");
                    key.Aktivalva = request.Aktivalva.Value;
                }

                if (changes.Count > 0)
                {
                    await db.SaveChangesAsync();

                    await auditService.LogAsync(
                        caller,
                        muveletTipus: "UPDATE",
                        entitasTipus: "Kulcs",
                        entitasId: id.ToString(),
                        ujErtek: string.Join("; ", changes));
                }

                var room = await db.Termek.FindAsync(key.TeremId);

                var result = new KeyDto
                {
                    Id = key.Id,
                    Szam = key.Szam,
                    Tipus = key.Tipus,
                    Aktivalva = key.Aktivalva,
                    TeremId = key.TeremId,
                    TeremNev = room?.Nev ?? string.Empty,
                    JelenlegiAllapot = key.Aktivalva ? "Szabad" : "Deaktivalt"
                };

                return Results.Ok(result);
            })
            .WithName("UpdateKey")
            .WithSummary("Kulcs adatainak módosítása")
            .RequireAuthorization("Adminisztrator")
            .Produces<KeyDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // K5 – DELETE /api/keys/{id} (Deaktiválás)
            group.MapDelete("/{id:int}", async (
                int id,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var key = await db.Kulcsok.FindAsync(id);
                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú kulcs nem található." });
                }

                key.Aktivalva = false;
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "DEACTIVATE",
                    entitasTipus: "Kulcs",
                    entitasId: id.ToString(),
                    ujErtek: "Aktivalva: false");

                return Results.Ok(new { message = $"A(z) {id} azonosítójú kulcs sikeresen deaktiválva lett." });
            })
            .WithName("DeleteKey")
            .WithSummary("Kulcs deaktiválása")
            .RequireAuthorization("Adminisztrator")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // K6 – GET /api/keys/{id}/status
            group.MapGet("/{id:int}/status", async (int id, AppDbContext db) =>
            {
                var key = await db.Kulcsok.FindAsync(id);
                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú kulcs nem található." });
                }

                var lastMovement = await db.Kulcsmozgasok
                    .AsNoTracking()
                    .Include(km => km.Operátor)
                    .Include(km => km.Foglalas)
                        .ThenInclude(f => f.Felhasznalo)
                    .Where(km => km.KulcsId == id)
                    .OrderByDescending(km => km.Idopont)
                    .FirstOrDefaultAsync();

                string allapot = !key.Aktivalva
                    ? "Deaktivalt"
                    : (lastMovement != null && (lastMovement.MozgasTipus == "Kiadás" || lastMovement.MozgasTipus == "Kiadas"))
                        ? "Kiadva"
                        : "Szabad";

                var status = new KeyStatusDto
                {
                    KulcsId = key.Id,
                    Szam = key.Szam,
                    Allapot = allapot,
                    KiadasIdopontja = allapot == "Kiadva" ? lastMovement?.Idopont : null,
                    KiadtaOperatorNev = allapot == "Kiadva" ? lastMovement?.Operátor?.Nev : null,
                    AktualisBirtokosNev = allapot == "Kiadva" ? lastMovement?.Foglalas?.Felhasznalo?.Nev : null,
                    KapcsolodoFoglalasId = allapot == "Kiadva" ? lastMovement?.FoglalasId : null
                };

                return Results.Ok(status);
            })
            .WithName("GetKeyStatus")
            .WithSummary("Kulcs aktuális állapotának lekérdezése")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<KeyStatusDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // K7 – GET /api/keys/{id}/movements
            group.MapGet("/{id:int}/movements", async (int id, AppDbContext db) =>
            {
                var keyExists = await db.Kulcsok.AnyAsync(k => k.Id == id);
                if (!keyExists)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú kulcs nem található." });
                }

                var movements = await db.Kulcsmozgasok
                    .AsNoTracking()
                    .Include(km => km.Kulcs)
                    .Include(km => km.Operátor)
                    .Include(km => km.Foglalas)
                        .ThenInclude(f => f.Felhasznalo)
                    .Where(km => km.KulcsId == id)
                    .OrderByDescending(km => km.Idopont)
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

                return Results.Ok(movements);
            })
            .WithName("GetKeyMovements")
            .WithSummary("Kulcs mozgástörténetének lekérdezése")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<List<KeyMovementItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            return group;
        }
    }
}
