using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
                    .Where(user => user.Role == RoleNames.Oktato && user.Aktiv);
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

                return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
                {
                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await SqlServerLocking.AcquireAsync(db, $"key-issue:{request.KulcsId}");

                var key = await db.Kulcsok.FirstOrDefaultAsync(k => k.Id == request.KulcsId);

                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {request.KulcsId} azonosítójú kulcs nem található." });
                }

                if (!key.Aktivalva)
                {
                    return Results.BadRequest(new { message = "Deaktivált kulcs nem adható ki." });
                }

                var lastMovement = await GetLatestMovementAsync(db, key.Id);
                if (lastMovement != null && KeyMovementTypes.IsIssue(lastMovement.MozgasTipus))
                {
                    return Results.Conflict(new { message = "A kulcs jelenleg ki van adva, előbb vissza kell venni." });
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                Felhasznalo recipient;
                int issueRoomId;
                if (request.FoglalasId.HasValue)
                {
                    var booking = await db.Foglalasok
                        .Include(item => item.Felhasznalo)
                        .FirstOrDefaultAsync(item => item.Id == request.FoglalasId.Value);
                    if (booking is null)
                    {
                        return Results.NotFound(new { message = $"A(z) {request.FoglalasId.Value} azonosítójú foglalás nem található." });
                    }
                    if (booking.Status != BookingRules.ActiveStatus)
                    {
                        return Results.BadRequest(new { message = $"A foglalás nem aktív (jelenlegi státusz: {booking.Status})." });
                    }
                    if (now >= booking.EndTime)
                    {
                        return Results.BadRequest(new { message = "A foglalás már véget ért, ehhez nem adható ki kulcs." });
                    }
                    if (booking.StartTime - now > BookingRules.KeyIssueLeadTime)
                    {
                        return Results.BadRequest(new
                        {
                            message = $"A kulcs legkorábban a foglalás kezdete előtt {BookingRules.KeyIssueLeadTime.TotalHours:0} órával adható ki."
                        });
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

                    if (key.Tipus == "Mesterkulcs")
                    {
                        if (request.TeremId.HasValue && request.TeremId.Value != booking.TeremId)
                        {
                            return Results.BadRequest(new { message = "A megadott terem nem egyezik a foglalás termével." });
                        }

                        if (!await db.KulcsTermek.AnyAsync(access =>
                                access.KulcsId == key.Id && access.TeremId == booking.TeremId))
                        {
                            return Results.BadRequest(new { message = "A mesterkulcs nincs hozzárendelve a foglalás terméhez." });
                        }
                    }
                    else if (booking.TeremId != key.TeremId)
                    {
                        return Results.BadRequest(new { message = "A foglalás nem ehhez a kulcshoz tartozó teremre szól." });
                    }

                    issueRoomId = booking.TeremId;
                    recipient = booking.Felhasznalo;
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

                    var requestedRecipient = await db.Felhasznalok
                        .SingleOrDefaultAsync(user => user.Id == request.AtvevoFelhasznaloId.Value);
                    if (requestedRecipient is null)
                    {
                        return Results.NotFound(new { message = "A megadott átvevő nem található." });
                    }
                    recipient = requestedRecipient;
                    if (recipient.Role != RoleNames.Oktato)
                    {
                        return Results.BadRequest(new { message = "Kulcs átvevője csak oktató lehet." });
                    }
                    if (key.Tipus == "Mesterkulcs")
                    {
                        if (!request.TeremId.HasValue)
                        {
                            return Results.BadRequest(new { message = "Foglalás nélküli mesterkulcs-kiadáshoz terem megadása kötelező." });
                        }

                        if (!await db.KulcsTermek.AnyAsync(access =>
                                access.KulcsId == key.Id && access.TeremId == request.TeremId.Value))
                        {
                            return Results.BadRequest(new { message = "A mesterkulcs nincs hozzárendelve a megadott teremhez." });
                        }
                    }

                    issueRoomId = key.Tipus == "Mesterkulcs"
                        ? request.TeremId!.Value
                        : key.TeremId;
                }

                if (!recipient.Aktiv)
                {
                    return Results.BadRequest(new { message = "Inaktivált felhasználó nem vehet át kulcsot." });
                }

                var recipientId = recipient.Id;
                var recipientName = recipient.Nev;
                var pinResult = await CheckRecipientPinAsync(db, transaction, recipient, request.Pin, now);
                if (pinResult is not null)
                {
                    return pinResult;
                }

                // Ha a portás nem adott meg foglalást, a kiadást az átvevő erre a teremre szóló,
                // éppen esedékes aktív foglalásához kötjük; így a foglalás nem lesz tévesen no-show.
                var linkedBookingId = request.FoglalasId;
                if (!linkedBookingId.HasValue)
                {
                    var latestStart = now.Add(BookingRules.KeyIssueLeadTime);
                    linkedBookingId = await db.Foglalasok
                        .Where(booking =>
                            booking.FelhasznaloId == recipientId &&
                            booking.TeremId == issueRoomId &&
                            booking.Status == BookingRules.ActiveStatus &&
                            booking.EndTime > now &&
                            booking.StartTime <= latestStart)
                        .OrderBy(booking => booking.StartTime)
                        .Select(booking => (int?)booking.Id)
                        .FirstOrDefaultAsync();
                }
                if (key.Tipus == "Mesterkulcs" &&
                    !await db.MesterkulcsJogosultsagok.AnyAsync(access =>
                        access.KulcsId == key.Id && access.FelhasznaloId == recipientId))
                {
                    return Results.Forbid();
                }

                var movement = new Kulcsmozgas
                {
                    KulcsId = key.Id,
                    FoglalasId = linkedBookingId,
                    TeremId = issueRoomId,
                    AtvevoFelhasznaloId = recipientId,
                    MozgasTipus = KeyMovementTypes.Issue,
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
                    issueRoomId,
                    movement.SerulesLeiras,
                    now);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "KEY_ISSUE",
                    entitasTipus: "Kulcsmozgas",
                    entitasId: movement.Id.ToString(),
                    ujErtek: $"KulcsId: {key.Id}, FoglalasId: {linkedBookingId}, AtvevoFelhasznaloId: {recipientId}, TeremAllapot: {movement.TeremAllapot}, SerulesLeiras: {movement.SerulesLeiras}");
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
                    FoglalasFelhasznaloNev = linkedBookingId.HasValue ? recipientName : null,
                    AtvevoFelhasznaloId = recipientId,
                    AtvevoNev = recipientName,
                    OperatorId = operatorId.Value,
                    OperatorNev = opUser?.Nev ?? string.Empty,
                    Megjegyzes = movement.Megjegyzes,
                    TeremAllapot = movement.TeremAllapot,
                    SerulesLeiras = movement.SerulesLeiras
                };

                return Results.Created($"/api/key-movements/{movement.Id}", result);
                });
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

                return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
                {
                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await SqlServerLocking.AcquireAsync(db, $"key-issue:{request.KulcsId}");

                var key = await db.Kulcsok.FirstOrDefaultAsync(k => k.Id == request.KulcsId);

                if (key is null)
                {
                    return Results.NotFound(new { message = $"A(z) {request.KulcsId} azonosítójú kulcs nem található." });
                }

                var lastMovement = await GetLatestMovementAsync(db, key.Id);
                if (lastMovement == null || !KeyMovementTypes.IsIssue(lastMovement.MozgasTipus))
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
                var recipient = await db.Felhasznalok.SingleOrDefaultAsync(user => user.Id == recipientId.Value);
                if (recipient is null)
                {
                    return Results.Conflict(new { message = "A kulcs korábbi átvevője nem található." });
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var pinResult = await CheckRecipientPinAsync(db, transaction, recipient, request.Pin, now);
                if (pinResult is not null)
                {
                    return pinResult;
                }

                // A visszavétel ugyanarra a teremre vonatkozik, amelyre a kulcsot kiadták
                // (mesterkulcsnál ez eltérhet a kulcs saját termétől).
                var issuedRoomId = lastMovement.TeremId
                    ?? (lastMovement.FoglalasId.HasValue
                        ? await db.Foglalasok
                            .Where(booking => booking.Id == lastMovement.FoglalasId.Value)
                            .Select(booking => (int?)booking.TeremId)
                            .SingleOrDefaultAsync()
                        : null)
                    ?? (key.Tipus == "Mesterkulcs" ? null : key.TeremId);

                int returnRoomId;
                if (issuedRoomId.HasValue)
                {
                    if (request.TeremId.HasValue && request.TeremId.Value != issuedRoomId.Value)
                    {
                        return Results.BadRequest(new { message = "A megadott terem nem egyezik a kiadás termével." });
                    }
                    returnRoomId = issuedRoomId.Value;
                }
                else
                {
                    // Régi adat: foglalás nélkül kiadott mesterkulcs, a kiadás terme nincs tárolva.
                    if (!request.TeremId.HasValue)
                    {
                        return Results.BadRequest(new
                        {
                            message = "A mesterkulcs kiadásának terme nem ismert, ezért a visszavételnél meg kell adni, melyik teremből hozták vissza.",
                            teremMegadasaSzukseges = true
                        });
                    }
                    if (!await db.KulcsTermek.AnyAsync(access =>
                            access.KulcsId == key.Id && access.TeremId == request.TeremId.Value))
                    {
                        return Results.BadRequest(new { message = "A mesterkulcs nincs hozzárendelve a megadott teremhez." });
                    }
                    returnRoomId = request.TeremId.Value;
                }
                var returnMovement = new Kulcsmozgas
                {
                    KulcsId = key.Id,
                    FoglalasId = lastMovement.FoglalasId,
                    TeremId = returnRoomId,
                    AtvevoFelhasznaloId = recipientId,
                    MozgasTipus = KeyMovementTypes.Return,
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
                    returnRoomId,
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
                var recipientName = recipient.Nev;

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
                });
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
                int? page,
                int? pageSize,
                AppDbContext db) =>
            {
                var normalizedPage = page.GetValueOrDefault(1);
                normalizedPage = normalizedPage <= 0 ? 1 : normalizedPage;
                var normalizedPageSize = pageSize.GetValueOrDefault(20);
                normalizedPageSize = normalizedPageSize <= 0 ? 20 : Math.Min(normalizedPageSize, 100);

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
                    var tolUtc = AppTime.ToUtc(tol.Value);
                    query = query.Where(km => km.Idopont >= tolUtc);
                }

                if (ig.HasValue)
                {
                    var igUtc = AppTime.ToUtc(ig.Value);
                    query = query.Where(km => km.Idopont <= igUtc);
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(km => km.Idopont)
                    .Skip((normalizedPage - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
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
                    Page = normalizedPage,
                    PageSize = normalizedPageSize
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

                // Csak az aktív kulcsok legutolsó mozgását kérdezzük le, és abból a kiadásokat.
                var latestIssues = await db.Kulcsmozgasok
                    .AsNoTracking()
                    .Where(movement =>
                        movement.Kulcs.Aktivalva &&
                        !db.Kulcsmozgasok.Any(newer =>
                            newer.KulcsId == movement.KulcsId &&
                            (newer.Idopont > movement.Idopont ||
                             (newer.Idopont == movement.Idopont && newer.Id > movement.Id))))
                    .Where(movement =>
                        movement.MozgasTipus == KeyMovementTypes.Issue ||
                        movement.MozgasTipus == KeyMovementTypes.LegacyIssue)
                    .Select(movement => new
                    {
                        movement.KulcsId,
                        KulcsSzam = movement.Kulcs.Szam,
                        RoomId = movement.TeremId ?? movement.Kulcs.TeremId,
                        movement.Idopont,
                        movement.FoglalasId,
                        FoglalasVege = movement.Foglalas != null ? (DateTime?)movement.Foglalas.EndTime : null,
                        BirtokosNev = movement.AtvevoFelhasznalo != null
                            ? movement.AtvevoFelhasznalo.Nev
                            : movement.Foglalas != null ? movement.Foglalas.Felhasznalo.Nev : null,
                        BirtokosEmail = movement.AtvevoFelhasznalo != null
                            ? movement.AtvevoFelhasznalo.Email
                            : movement.Foglalas != null ? movement.Foglalas.Felhasznalo.Email : null
                    })
                    .ToListAsync(cancellationToken);

                var roomIds = latestIssues.Select(item => item.RoomId).Distinct().ToList();
                var roomNames = await db.Termek
                    .AsNoTracking()
                    .Where(room => roomIds.Contains(room.Id))
                    .ToDictionaryAsync(room => room.Id, room => room.Nev, cancellationToken);

                var overdueList = new List<OverdueKeyDto>();
                foreach (var last in latestIssues)
                {
                    var deadline = KeyReturnDeadlineCalculator.GetDeadlineUtc(
                        last.Idopont,
                        last.FoglalasVege,
                        closingTime,
                        timeZone);
                    if (now > deadline)
                    {
                        overdueList.Add(new OverdueKeyDto
                        {
                            KulcsId = last.KulcsId,
                            KulcsSzam = last.KulcsSzam,
                            TeremId = last.RoomId,
                            TeremNev = roomNames.GetValueOrDefault(last.RoomId, string.Empty),
                            KiadasIdeje = last.Idopont,
                            FoglalasId = last.FoglalasId,
                            FoglalasVége = last.FoglalasVege,
                            BirtokosNev = last.BirtokosNev,
                            BirtokosEmail = last.BirtokosEmail,
                            KésésPercekben = Math.Round((now - deadline).TotalMinutes, 1)
                        });
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

        private const int MaxPinAttempts = 5;
        private static readonly TimeSpan PinLockDuration = TimeSpan.FromMinutes(15);

        private static Task<Kulcsmozgas?> GetLatestMovementAsync(AppDbContext db, int keyId) =>
            db.Kulcsmozgasok
                .Where(movement => movement.KulcsId == keyId)
                .OrderByDescending(movement => movement.Idopont)
                .ThenByDescending(movement => movement.Id)
                .FirstOrDefaultAsync();

        /// <summary>
        /// Ellenőrzi az átvevő PIN-kódját. Sikeres ellenőrzésnél null-t ad vissza.
        /// Hibás PIN esetén a próbálkozást elmenti (a tranzakciót is lezárja), és
        /// <see cref="MaxPinAttempts"/> egymás utáni hiba után ideiglenesen zárolja a PIN-t.
        /// </summary>
        private static async Task<IResult?> CheckRecipientPinAsync(
            AppDbContext db,
            IDbContextTransaction? transaction,
            Felhasznalo recipient,
            string pin,
            DateTime now)
        {
            if (string.IsNullOrWhiteSpace(recipient.PinHash))
            {
                return Results.Conflict(new { message = "Az átvevő még nem állított be PIN-kódot." });
            }

            if (recipient.PinZarolvaEddig > now)
            {
                return Results.Json(
                    new
                    {
                        message = $"Túl sok hibás PIN-próbálkozás. A PIN {AppTime.ToLocal(recipient.PinZarolvaEddig.Value):HH:mm}-ig zárolva van."
                    },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (BCrypt.Net.BCrypt.Verify(pin, recipient.PinHash))
            {
                // A nullázás a művelet többi változásával együtt mentődik.
                recipient.PinHibasProbalkozasok = 0;
                recipient.PinZarolvaEddig = null;
                return null;
            }

            recipient.PinHibasProbalkozasok++;
            if (recipient.PinHibasProbalkozasok >= MaxPinAttempts)
            {
                recipient.PinHibasProbalkozasok = 0;
                recipient.PinZarolvaEddig = now.Add(PinLockDuration);
            }

            await db.SaveChangesAsync();
            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }

            return Results.Forbid();
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
