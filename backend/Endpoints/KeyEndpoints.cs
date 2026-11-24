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
                    query = query.Where(k => k.Tipus == "Mesterkulcs"
                        ? k.KulcsTeremKapcsok.Any(access => access.TeremId == teremId.Value)
                        : k.TeremId == teremId.Value);
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
                        .ThenByDescending(km => km.Id)
                        .FirstOrDefault();

                    var allapot = GetState(k, lastMovement);

                    return new KeyDto
                    {
                        Id = k.Id,
                        Szam = k.Szam,
                        Tipus = k.Tipus,
                        Aktivalva = k.Aktivalva,
                        TeremId = k.TeremId,
                        TeremNev = k.Terem?.Nev ?? string.Empty,
                        JelenlegiAllapot = allapot,
                        KiadottTeremIsmeretlen = IsIssuedRoomUnknown(k, lastMovement, allapot)
                    };
                }).ToList();

                return Results.Ok(results);
            })
            .WithName("GetKeys")
            .WithSummary("Kulcsok listázása szűrőkkel")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<List<KeyDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

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
                    .ThenByDescending(km => km.Id)
                    .FirstOrDefault();

                var allapot = GetState(k, lastMovement);

                var dto = new KeyDto
                {
                    Id = k.Id,
                    Szam = k.Szam,
                    Tipus = k.Tipus,
                    Aktivalva = k.Aktivalva,
                    TeremId = k.TeremId,
                    TeremNev = k.Terem?.Nev ?? string.Empty,
                    JelenlegiAllapot = allapot,
                    KiadottTeremIsmeretlen = IsIssuedRoomUnknown(k, lastMovement, allapot)
                };

                return Results.Ok(dto);
            })
            .WithName("GetKeyById")
            .WithSummary("Egy kulcs adatainak lekérdezése")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<KeyDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

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

                // Ugyanazzal a (levágott) értékkel ellenőrzünk, amelyet elmentünk.
                var keyNumber = request.Szam.Trim();
                bool numberExists = await db.Kulcsok.AnyAsync(k => k.Szam == keyNumber);
                if (numberExists)
                {
                    return Results.Conflict(new { message = $"A megadott kulcsszám ('{keyNumber}') már létezik." });
                }

                var key = new Kulcs
                {
                    Szam = keyNumber,
                    Tipus = request.Tipus,
                    TeremId = request.TeremId,
                    Aktivalva = request.Aktivalva,
                    KulcsTeremKapcsok = request.Tipus == "Mesterkulcs"
                        ? [new KulcsTerem { TeremId = request.TeremId }]
                        : []
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
            .Produces(StatusCodes.Status403Forbidden);

            // K4 – PUT /api/keys/{id}
            group.MapPut("/{id:int}", async (
                int id,
                UpdateKeyRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var key = await db.Kulcsok
                    .Include(k => k.Terem)
                    .Include(k => k.KulcsTeremKapcsok)
                    .FirstOrDefaultAsync(k => k.Id == id);
                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú kulcs nem található." });
                }

                var changes = new List<string>();
                var previousType = key.Tipus;
                var previousRoomId = key.TeremId;
                var isIssued = await IsIssuedAsync(db, id);

                // Kint lévő kulcs típusa, terme és aktív állapota nem változhat, különben a kulcs
                // visszavétele és a késések nyilvántartása elromlana.
                if (isIssued &&
                    ((!string.IsNullOrWhiteSpace(request.Tipus) && request.Tipus != key.Tipus) ||
                     (request.TeremId.HasValue && request.TeremId.Value != key.TeremId) ||
                     request.Aktivalva == false))
                {
                    return Results.Conflict(new
                    {
                        message = "A kulcs jelenleg ki van adva: a típusa, terme és aktív állapota csak a visszavétel után módosítható."
                    });
                }

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

                if (previousType == "Mesterkulcs" && key.Tipus != "Mesterkulcs")
                {
                    // Osztálykulccsá váláskor a mesterkulcs-termek és -jogosultságok is megszűnnek.
                    db.KulcsTermek.RemoveRange(key.KulcsTeremKapcsok);
                    db.MesterkulcsJogosultsagok.RemoveRange(
                        await db.MesterkulcsJogosultsagok.Where(access => access.KulcsId == key.Id).ToListAsync());
                }
                else if (key.Tipus == "Mesterkulcs" &&
                         (previousType != key.Tipus || previousRoomId != key.TeremId) &&
                         key.KulcsTeremKapcsok.All(room => room.TeremId != key.TeremId))
                {
                    key.KulcsTeremKapcsok.Add(new KulcsTerem
                    {
                        KulcsId = key.Id,
                        TeremId = key.TeremId
                    });
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
                    JelenlegiAllapot = !key.Aktivalva ? "Deaktivalt" : isIssued ? "Kiadva" : "Szabad"
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
            .Produces(StatusCodes.Status403Forbidden);

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

                if (await IsIssuedAsync(db, id))
                {
                    return Results.Conflict(new { message = "A kulcs jelenleg ki van adva, ezért csak a visszavétel után deaktiválható." });
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
            .Produces(StatusCodes.Status403Forbidden);

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
                    .Include(km => km.AtvevoFelhasznalo)
                    .Include(km => km.Foglalas)
                        .ThenInclude(f => f!.Felhasznalo)
                    .Where(km => km.KulcsId == id)
                    .OrderByDescending(km => km.Idopont)
                    .ThenByDescending(km => km.Id)
                    .FirstOrDefaultAsync();

                // Ugyanaz a szabály, mint a kulcslistában: kint lévő kulcs mindig "Kiadva".
                var allapot = GetState(key, lastMovement);

                var status = new KeyStatusDto
                {
                    KulcsId = key.Id,
                    Szam = key.Szam,
                    Allapot = allapot,
                    KiadasIdopontja = allapot == "Kiadva" ? lastMovement?.Idopont : null,
                    KiadtaOperatorNev = allapot == "Kiadva" ? lastMovement?.Operátor?.Nev : null,
                    AktualisBirtokosNev = allapot == "Kiadva"
                        ? lastMovement?.AtvevoFelhasznalo?.Nev ?? lastMovement?.Foglalas?.Felhasznalo?.Nev
                        : null,
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
            .Produces(StatusCodes.Status403Forbidden);

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
                    .Include(km => km.AtvevoFelhasznalo)
                    .Include(km => km.Foglalas)
                        .ThenInclude(f => f!.Felhasznalo)
                    .Where(km => km.KulcsId == id)
                    .OrderByDescending(km => km.Idopont)
                    .ThenByDescending(km => km.Id)
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

                return Results.Ok(movements);
            })
            .WithName("GetKeyMovements")
            .WithSummary("Kulcs mozgástörténetének lekérdezése")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<List<KeyMovementItemDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            group.MapGet("/{id:int}/rooms", async (
                int id,
                AppDbContext db,
                CancellationToken cancellationToken) =>
            {
                var isMasterKey = await db.Kulcsok.AnyAsync(
                    key => key.Id == id && key.Tipus == "Mesterkulcs",
                    cancellationToken);
                if (!isMasterKey)
                {
                    return Results.NotFound(new { message = "A megadott mesterkulcs nem található." });
                }

                var rooms = await db.KulcsTermek
                    .AsNoTracking()
                    .Where(access => access.KulcsId == id)
                    .OrderBy(access => access.Terem.Nev)
                    .Select(access => new KeyRoomDto(access.TeremId, access.Terem.Nev))
                    .ToListAsync(cancellationToken);

                return Results.Ok(rooms);
            })
            .WithName("GetMasterKeyRooms")
            .WithSummary("Mesterkulcshoz rendelt termek listázása")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<List<KeyRoomDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

            group.MapPut("/{id:int}/rooms", async (
                int id,
                UpdateKeyRoomsRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller,
                CancellationToken cancellationToken) =>
            {
                if (request.TeremIds is null ||
                    request.TeremIds.Count != request.TeremIds.Distinct().Count() ||
                    request.TeremIds.Any(roomId => roomId <= 0))
                {
                    return Results.BadRequest(new { message = "A teremazonosítók nem ismétlődhetnek és pozitívnak kell lenniük." });
                }

                // Terem nélküli mesterkulcs semmire nem adható ki; legalább egy terem kell.
                if (request.TeremIds.Count == 0)
                {
                    return Results.BadRequest(new { message = "A mesterkulcshoz legalább egy termet hozzá kell rendelni." });
                }

                var key = await db.Kulcsok
                    .Include(item => item.KulcsTeremKapcsok)
                    .SingleOrDefaultAsync(
                        item => item.Id == id && item.Tipus == "Mesterkulcs",
                        cancellationToken);
                if (key is null)
                {
                    return Results.NotFound(new { message = "A megadott mesterkulcs nem található." });
                }

                var existingRoomIds = await db.Termek
                    .Where(room => request.TeremIds.Contains(room.Id))
                    .Select(room => room.Id)
                    .ToListAsync(cancellationToken);
                if (existingRoomIds.Count != request.TeremIds.Count)
                {
                    return Results.BadRequest(new { message = "A hozzárendelés csak létező termeket tartalmazhat." });
                }

                return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
                {
                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(cancellationToken);
                var requestedRoomIds = request.TeremIds.ToHashSet();
                var previousRoomIds = key.KulcsTeremKapcsok
                    .Select(room => room.TeremId)
                    .Order()
                    .ToArray();
                var removedRooms = key.KulcsTeremKapcsok
                    .Where(room => !requestedRoomIds.Contains(room.TeremId))
                    .ToList();
                db.KulcsTermek.RemoveRange(removedRooms);
                foreach (var roomId in request.TeremIds.Where(
                             roomId => key.KulcsTeremKapcsok.All(room => room.TeremId != roomId)))
                {
                    key.KulcsTeremKapcsok.Add(new KulcsTerem
                    {
                        KulcsId = key.Id,
                        TeremId = roomId
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
                await auditService.LogAsync(
                    caller,
                    "UPDATE_ROOMS",
                    "Kulcs",
                    key.Id.ToString(),
                    regiErtek: string.Join(",", previousRoomIds),
                    ujErtek: string.Join(",", request.TeremIds.Order()));
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return Results.Ok(await db.KulcsTermek
                    .AsNoTracking()
                    .Where(access => access.KulcsId == id)
                    .OrderBy(access => access.Terem.Nev)
                    .Select(access => new KeyRoomDto(access.TeremId, access.Terem.Nev))
                    .ToListAsync(cancellationToken));
                });
            })
            .WithName("UpdateMasterKeyRooms")
            .WithSummary("Mesterkulcshoz rendelt termek teljes körű frissítése")
            .RequireAuthorization("Adminisztrator")
            .Produces<List<KeyRoomDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

            return group;
        }

        // Kint lévő kulcsnál a "Kiadva" állapot az erősebb, hogy egy (régebben) kiadva deaktivált
        // kulcs is visszavehető maradjon.
        private static string GetState(Kulcs key, Kulcsmozgas? lastMovement) =>
            lastMovement != null && KeyMovementTypes.IsIssue(lastMovement.MozgasTipus)
                ? "Kiadva"
                : key.Aktivalva ? "Szabad" : "Deaktivalt";

        private static async Task<bool> IsIssuedAsync(AppDbContext db, int keyId)
        {
            var lastType = await db.Kulcsmozgasok
                .Where(movement => movement.KulcsId == keyId)
                .OrderByDescending(movement => movement.Idopont)
                .ThenByDescending(movement => movement.Id)
                .Select(movement => movement.MozgasTipus)
                .FirstOrDefaultAsync();
            return KeyMovementTypes.IsIssue(lastType);
        }

        private static bool IsIssuedRoomUnknown(Kulcs key, Kulcsmozgas? lastMovement, string state) =>
            state == "Kiadva" &&
            key.Tipus == "Mesterkulcs" &&
            lastMovement is { TeremId: null, FoglalasId: null };
    }
}
