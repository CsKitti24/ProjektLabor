using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Bookings;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints
{
    public static class BookingEndpoints
    {
        // A /my végpont egy kérésben ennyi foglalást ad vissza (a legújabbak elöl).
        private const int MyBookingsMaxPageSize = 500;

        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            BookingRules.ActiveStatus,
            BookingRules.NoShowStatus,
            BookingRules.CancelledStatus,
            BookingRules.DoneStatus
        };

        // Egyetlen leképezés a foglalás → DTO átalakításhoz: lekérdezésben (SQL-re fordítva)
        // és memóriában (lefordított delegáltként) is ezt használjuk.
        private static readonly Expression<Func<Foglalas, BookingDto>> ToDtoExpression = f => new BookingDto
        {
            Id = f.Id,
            FelhasznaloId = f.FelhasznaloId,
            FelhasznaloNev = f.Felhasznalo != null ? f.Felhasznalo.Nev : string.Empty,
            FelhasznaloEmail = f.Felhasznalo != null ? f.Felhasznalo.Email : string.Empty,
            TeremId = f.TeremId,
            TeremNev = f.Terem != null ? f.Terem.Nev : string.Empty,
            StartTime = f.StartTime,
            EndTime = f.EndTime,
            Status = f.Status,
            SpecialisIgenyek = f.SpecialisIgenyek,
            CreatedAt = f.CreatedAt,
            UpdatedAt = f.UpdatedAt
        };

        private static readonly Func<Foglalas, BookingDto> ToDto = ToDtoExpression.Compile();

        public static RouteGroupBuilder MapBookingEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/bookings")
                .WithTags("Bookings")
                .RequireAuthorization("Authenticated");

            // B6 – GET /api/bookings/my (Regisztráljuk az /{id} előtt!)
            group.MapGet("/my", async (
                string? status,
                int? page,
                int? pageSize,
                AppDbContext db,
                HttpContext httpContext,
                ClaimsPrincipal caller) =>
            {
                if (GetCallerId(caller) is not { } userId)
                {
                    return Results.Unauthorized();
                }

                // A válasz lista marad (a legújabbak elöl); egyszerre legfeljebb MyBookingsMaxPageSize elem,
                // az összes elem számát az X-Total-Count fejléc adja, régebbiek a page paraméterrel érhetők el.
                var normalizedPage = Pagination.NormalizePage(page);
                var normalizedPageSize = pageSize is > 0
                    ? Math.Min(pageSize.Value, MyBookingsMaxPageSize)
                    : MyBookingsMaxPageSize;

                var query = db.Foglalasok
                    .AsNoTracking()
                    .Where(f => f.FelhasznaloId == userId);

                if (!string.IsNullOrWhiteSpace(status))
                {
                    query = query.Where(f => f.Status == status);
                }

                var totalCount = await query.CountAsync();
                var list = await query
                    .OrderByDescending(f => f.StartTime)
                    .ThenByDescending(f => f.Id)
                    .Skip((normalizedPage - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .Select(ToDtoExpression)
                    .ToListAsync();

                httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Ok(list);
            })
            .WithName("GetMyBookings")
            .WithSummary("A bejelentkezett felhasználó saját foglalásai")
            .RequireAuthorization("Oktato")
            .Produces<List<BookingDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            // B1 – GET /api/bookings
            group.MapGet("/", async (
                int? teremId,
                int? felhasznaloId,
                string? status,
                DateTime? tol,
                DateTime? ig,
                int? page,
                int? pageSize,
                AppDbContext db) =>
            {
                var normalizedPage = Pagination.NormalizePage(page);
                var normalizedPageSize = pageSize.GetValueOrDefault(20);
                normalizedPageSize = normalizedPageSize <= 0 ? 20 : Math.Min(normalizedPageSize, 100);

                var query = db.Foglalasok.AsNoTracking();

                if (teremId.HasValue)
                {
                    query = query.Where(f => f.TeremId == teremId.Value);
                }

                if (felhasznaloId.HasValue)
                {
                    query = query.Where(f => f.FelhasznaloId == felhasznaloId.Value);
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    query = query.Where(f => f.Status == status);
                }

                if (tol.HasValue)
                {
                    var tolUtc = AppTime.ToUtc(tol.Value);
                    query = query.Where(f => f.EndTime >= tolUtc);
                }

                if (ig.HasValue)
                {
                    var igUtc = AppTime.ToUtc(ig.Value);
                    query = query.Where(f => f.StartTime <= igUtc);
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(f => f.StartTime)
                    .Skip((normalizedPage - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .Select(ToDtoExpression)
                    .ToListAsync();

                return Results.Ok(new BookingListDto
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = normalizedPage,
                    PageSize = normalizedPageSize
                });
            })
            .WithName("GetBookings")
            .WithSummary("Foglalások listázása szűrőkkel és lapozással")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<BookingListDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

            // B2 – GET /api/bookings/{id}
            group.MapGet("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal caller) =>
            {
                var dto = await db.Foglalasok
                    .AsNoTracking()
                    .Where(f => f.Id == id)
                    .Select(ToDtoExpression)
                    .FirstOrDefaultAsync();

                if (dto is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú foglalás nem található." });
                }

                if (GetCallerId(caller) is not { } callerId)
                {
                    return Results.Unauthorized();
                }

                var canReadAnyBooking =
                    caller.IsInRole("Portas") ||
                    caller.IsInRole("Adminisztrator") ||
                    caller.IsInRole("UzemeltetesiIgazgato");
                if (!canReadAnyBooking &&
                    (!caller.IsInRole("Oktato") || dto.FelhasznaloId != callerId))
                {
                    return ApiErrors.Forbidden("Ehhez a foglaláshoz nincs hozzáférésed.");
                }

                return Results.Ok(dto);
            })
            .WithName("GetBookingById")
            .WithSummary("Egy foglalás részletes adatainak lekérdezése")
            .Produces<BookingDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

            // B3 – POST /api/bookings
            group.MapPost("/", async (
                CreateBookingRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller,
                TimeProvider timeProvider) =>
            {
                if (GetCallerId(caller) is not { } callerId)
                {
                    return Results.Unauthorized();
                }

                var targetUserId = request.FelhasznaloId.HasValue && caller.IsInRole("Adminisztrator")
                    ? request.FelhasznaloId.Value
                    : callerId;
                var targetUser = await db.Felhasznalok.FindAsync(targetUserId);
                if (targetUser is null || !targetUser.Aktiv)
                {
                    return Results.NotFound(new { message = $"A(z) {targetUserId} azonosítójú aktív felhasználó nem található." });
                }

                // Kulcsot csak oktató vehet át, ezért foglalás is csak oktató nevére szólhat
                // (különben a foglalás biztosan no-show lenne).
                if (targetUser.Role != ProjektLabor.Security.RoleNames.Oktato)
                {
                    return Results.BadRequest(new { message = "Foglalás csak oktató nevére rögzíthető; adminként add meg az oktatót (felhasznaloId)." });
                }

                // A felfüggesztést az időpont-ellenőrzések előtt nézzük, hogy az oktató ezt az okot lássa.
                if (targetUser.FoglalasFelfuggesztveEddig > timeProvider.GetUtcNow().UtcDateTime)
                {
                    return ApiErrors.Forbidden(
                        $"{targetUser.Nev} foglalási joga {AppTime.ToLocal(targetUser.FoglalasFelfuggesztveEddig.Value):yyyy.MM.dd. HH:mm}-ig fel van függesztve" +
                        (string.IsNullOrWhiteSpace(targetUser.FelfuggesztesOka) ? "." : $": {targetUser.FelfuggesztesOka}"));
                }

                var timeError = BookingRules.ValidateTimeRange(request.StartTime, request.EndTime);
                if (timeError is not null)
                {
                    return Results.BadRequest(new { message = timeError });
                }

                var startUtc = AppTime.ToUtc(request.StartTime);
                var endUtc = AppTime.ToUtc(request.EndTime);
                if (startUtc <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    return Results.BadRequest(new { message = "Múltbeli vagy már megkezdődött időpontra nem lehet foglalni." });
                }

                var windowError = BookingRules.ValidateBookingWindow(startUtc, timeProvider.GetUtcNow().UtcDateTime);
                if (windowError is not null)
                {
                    return Results.BadRequest(new { message = windowError });
                }

                var room = await db.Termek.FindAsync(request.TeremId);
                if (room is null)
                {
                    return Results.NotFound(new { message = $"A(z) {request.TeremId} azonosítójú terem nem található." });
                }

                if (!room.Aktivalva)
                {
                    return Results.BadRequest(new { message = "Deaktivált teremre nem lehet foglalást rögzíteni." });
                }

                return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
                {
                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                await SqlServerLocking.AcquireAsync(db, $"room-booking:{request.TeremId}");

                var validationError = await BookingRules.ValidateAsync(
                    db, request.TeremId, startUtc, endUtc);
                if (validationError is not null)
                {
                    return Results.Conflict(new { message = validationError });
                }

                var quotaError = await BookingRules.ValidateInstructorQuotaAsync(
                    db, targetUserId, timeProvider.GetUtcNow().UtcDateTime);
                if (quotaError is not null)
                {
                    return Results.Conflict(new { message = quotaError });
                }

                var booking = new Foglalas
                {
                    FelhasznaloId = targetUserId,
                    Felhasznalo = targetUser,
                    TeremId = request.TeremId,
                    Terem = room,
                    StartTime = startUtc,
                    EndTime = endUtc,
                    Status = BookingRules.ActiveStatus,
                    SpecialisIgenyek = string.IsNullOrWhiteSpace(request.SpecialisIgenyek) ? null : request.SpecialisIgenyek.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.Foglalasok.Add(booking);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (InvalidOperationException ex) when (IsOverlapException(ex))
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync();
                    }
                    return Results.Conflict(new { message = ex.Message });
                }

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CREATE",
                    entitasTipus: "Foglalas",
                    entitasId: booking.Id.ToString(),
                    ujErtek: $"TeremId: {booking.TeremId}, Start: {booking.StartTime:O}, End: {booking.EndTime:O}, Igenyek: {booking.SpecialisIgenyek}");
                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }

                return Results.Created($"/api/bookings/{booking.Id}", ToDto(booking));
                });
            })
            .WithName("CreateBooking")
            .WithSummary("Új teremfoglalás létrehozása")
            .RequireAuthorization("AdminOrOktato")
            .Produces<BookingDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

            // B4 – PUT /api/bookings/{id}
            group.MapPut("/{id:int}", async (
                int id,
                UpdateBookingRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller,
                TimeProvider timeProvider) =>
            {
                var snapshot = await db.Foglalasok
                    .AsNoTracking()
                    .Where(f => f.Id == id)
                    .Select(f => new { f.FelhasznaloId, f.TeremId })
                    .FirstOrDefaultAsync();

                if (snapshot is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú foglalás nem található." });
                }

                if (GetCallerId(caller) is not { } callerId)
                {
                    return Results.Unauthorized();
                }

                bool isAdmin = caller.IsInRole("Adminisztrator");
                if (!isAdmin && snapshot.FelhasznaloId != callerId)
                {
                    return ApiErrors.Forbidden("Csak a saját foglalásodat módosíthatod.");
                }
                if (!isAdmin && request.TeremId.HasValue)
                {
                    return ApiErrors.Forbidden("A foglalás termét csak az adminisztrátor módosíthatja.");
                }

                string? requestedStatus = null;
                if (!string.IsNullOrWhiteSpace(request.Status))
                {
                    requestedStatus = AllowedStatuses.FirstOrDefault(s => s.Equals(request.Status, StringComparison.OrdinalIgnoreCase));
                    if (requestedStatus == null)
                    {
                        return Results.BadRequest(new { message = $"Érvénytelen foglalás státusz: '{request.Status}'. Megengedett: Aktiv, NoShow, Megszuntetve, Kesz." });
                    }
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;

                return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
                {
                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var requestedRoomId = request.TeremId ?? snapshot.TeremId;
                foreach (var roomId in new[] { snapshot.TeremId, requestedRoomId }.Distinct().Order())
                {
                    await SqlServerLocking.AcquireAsync(db, $"room-booking:{roomId}");
                }

                // A foglalást a zárak megszerzése után, a tranzakción belül olvassuk be,
                // így nem dolgozunk elavult állapottal.
                var booking = await db.Foglalasok
                    .Include(f => f.Terem)
                    .Include(f => f.Felhasznalo)
                    .FirstOrDefaultAsync(f => f.Id == id);
                if (booking is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú foglalás nem található." });
                }
                if (booking.TeremId != snapshot.TeremId)
                {
                    return Results.Conflict(new { message = "A foglalás közben módosult, kérjük, próbáld újra." });
                }

                var newStart = request.StartTime.HasValue ? AppTime.ToUtc(request.StartTime.Value) : booking.StartTime;
                var newEnd = request.EndTime.HasValue ? AppTime.ToUtc(request.EndTime.Value) : booking.EndTime;
                var newRoomId = requestedRoomId;
                var changesSchedule =
                    newStart != booking.StartTime ||
                    newEnd != booking.EndTime ||
                    newRoomId != booking.TeremId;
                var resultingStatus = requestedStatus ?? booking.Status;
                var statusChanges = !string.Equals(resultingStatus, booking.Status, StringComparison.Ordinal);
                var previousStart = booking.StartTime;
                var previousRoomName = booking.Terem.Nev;

                if (statusChanges &&
                    !BookingRules.IsUserStatusChangeAllowed(booking.Status, resultingStatus, isAdmin))
                {
                    return ApiErrors.Forbidden("A foglalás erre az állapotra nem módosítható (a lezárt és a no-show állapotot a rendszer állítja be, visszaaktiválni csak az adminisztrátor tud).");
                }

                var hasStarted = booking.StartTime <= now;
                if (statusChanges && resultingStatus == BookingRules.CancelledStatus && hasStarted && !isAdmin)
                {
                    return Results.BadRequest(new { message = "Már megkezdődött foglalás nem mondható le." });
                }

                if (changesSchedule)
                {
                    if (resultingStatus != BookingRules.ActiveStatus)
                    {
                        return Results.Conflict(new { message = "Csak aktív foglalás időpontja vagy terme módosítható." });
                    }
                    if (hasStarted)
                    {
                        return Results.BadRequest(new { message = "Már megkezdődött vagy lezajlott foglalás időpontja nem módosítható." });
                    }

                    // Ha a foglaláshoz már kiadták (korán felvették) a kulcsot, a foglalás nem tehető át másik
                    // terembe: az oktatónál a régi terem kulcsa van.
                    if (newRoomId != booking.TeremId && await HasIssuedKeyForBookingAsync(db, booking.Id))
                    {
                        return Results.Conflict(new { message = "A foglaláshoz már kiadták a kulcsot; másik terembe csak a kulcs visszavétele után helyezhető át." });
                    }
                }

                // Lemondott foglalás visszaaktiválásakor is érvényes az oktatónkénti jövőbeli foglaláskorlát.
                if (statusChanges && resultingStatus == BookingRules.ActiveStatus)
                {
                    var quotaError = await BookingRules.ValidateInstructorQuotaAsync(db, booking.FelhasznaloId, now);
                    if (quotaError is not null)
                    {
                        return Results.Conflict(new { message = quotaError });
                    }
                }

                if (resultingStatus == BookingRules.ActiveStatus && (changesSchedule || statusChanges))
                {
                    if (newStart <= now)
                    {
                        return Results.BadRequest(new { message = "Múltbeli vagy már megkezdődött időpontra nem lehet foglalni." });
                    }

                    var targetRoom = newRoomId == booking.TeremId
                        ? booking.Terem
                        : await db.Termek.SingleOrDefaultAsync(room => room.Id == newRoomId);
                    if (targetRoom is null)
                    {
                        return Results.NotFound(new { message = "A megadott terem nem található." });
                    }
                    if (!targetRoom.Aktivalva)
                    {
                        return Results.BadRequest(new { message = "Deaktivált teremre nem lehet foglalást rögzíteni." });
                    }

                    var timeError = BookingRules.ValidateTimeRange(newStart, newEnd)
                        ?? BookingRules.ValidateBookingWindow(newStart, timeProvider.GetUtcNow().UtcDateTime);
                    if (timeError is not null)
                    {
                        return Results.BadRequest(new { message = timeError });
                    }

                    var validationError = await BookingRules.ValidateAsync(
                        db, newRoomId, newStart, newEnd, id);
                    if (validationError is not null)
                    {
                        return Results.Conflict(new { message = validationError });
                    }

                    booking.StartTime = newStart;
                    booking.EndTime = newEnd;
                    booking.TeremId = newRoomId;
                    booking.Terem = targetRoom;
                }

                booking.Status = resultingStatus;
                if (request.SpecialisIgenyek != null)
                {
                    booking.SpecialisIgenyek = string.IsNullOrWhiteSpace(request.SpecialisIgenyek) ? null : request.SpecialisIgenyek.Trim();
                }
                booking.UpdatedAt = DateTime.UtcNow;

                // Ha nem a foglaló oktató módosította (hanem pl. az adminisztrátor), értesítést kap róla.
                if (booking.FelhasznaloId != callerId)
                {
                    var originalDescription = $"{previousRoomName}, {AppTime.ToLocal(previousStart):yyyy.MM.dd. HH:mm}";
                    if (statusChanges && booking.Status == BookingRules.CancelledStatus)
                    {
                        BookingNotifications.AddCancelled(
                            db, booking, $"Az adminisztrátor lemondta a foglalásodat ({originalDescription}).", now);
                    }
                    else if (changesSchedule)
                    {
                        BookingNotifications.AddChanged(
                            db,
                            booking,
                            $"Az adminisztrátor módosította a foglalásodat ({originalDescription}). Új időpont: " +
                            $"{booking.Terem.Nev}, {AppTime.ToLocal(booking.StartTime):yyyy.MM.dd. HH:mm}–{AppTime.ToLocal(booking.EndTime):HH:mm}.",
                            now);
                    }
                }

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (InvalidOperationException ex) when (IsOverlapException(ex))
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync();
                    }
                    return Results.Conflict(new { message = ex.Message });
                }

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "UPDATE",
                    entitasTipus: "Foglalas",
                    entitasId: id.ToString(),
                    ujErtek: $"Start: {booking.StartTime:O}, End: {booking.EndTime:O}, Status: {booking.Status}, Igenyek: {booking.SpecialisIgenyek}");
                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }

                return Results.Ok(ToDto(booking));
                });
            })
            .WithName("UpdateBooking")
            .WithSummary("Foglalás időpontjának vagy státuszának módosítása")
            .Produces<BookingDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

            // B5 – DELETE /api/bookings/{id} (Lemondás)
            group.MapDelete("/{id:int}", async (
                int id,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller,
                TimeProvider timeProvider) =>
            {
                var booking = await db.Foglalasok.FindAsync(id);
                if (booking is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú foglalás nem található." });
                }

                if (GetCallerId(caller) is not { } callerId)
                {
                    return Results.Unauthorized();
                }

                bool isAdmin = caller.IsInRole("Adminisztrator");
                if (!isAdmin && booking.FelhasznaloId != callerId)
                {
                    return ApiErrors.Forbidden("Csak a saját foglalásodat mondhatod le.");
                }

                if (booking.Status != BookingRules.ActiveStatus)
                {
                    return Results.Conflict(new { message = $"Csak aktív foglalás mondható le (jelenlegi státusz: {booking.Status})." });
                }

                if (!isAdmin && booking.StartTime <= timeProvider.GetUtcNow().UtcDateTime)
                {
                    return Results.BadRequest(new { message = "Már megkezdődött foglalás nem mondható le." });
                }

                booking.Status = BookingRules.CancelledStatus;
                booking.UpdatedAt = DateTime.UtcNow;
                if (booking.FelhasznaloId != callerId)
                {
                    var roomName = await db.Termek.Where(room => room.Id == booking.TeremId).Select(room => room.Nev).SingleAsync();
                    BookingNotifications.AddCancelled(
                        db,
                        booking,
                        $"Az adminisztrátor lemondta a foglalásodat ({roomName}, {AppTime.ToLocal(booking.StartTime):yyyy.MM.dd. HH:mm}).",
                        timeProvider.GetUtcNow().UtcDateTime);
                }

                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CANCEL",
                    entitasTipus: "Foglalas",
                    entitasId: id.ToString(),
                    ujErtek: $"Status: {BookingRules.CancelledStatus}");

                return Results.Ok(new { message = $"A(z) {id} azonosítójú foglalás sikeresen lemondásra került." });
            })
            .WithName("DeleteBooking")
            .WithSummary("Foglalás lemondása")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

            return group;
        }

        private static int? GetCallerId(ClaimsPrincipal caller)
        {
            var subject = caller.FindFirst("sub")?.Value ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(subject, out var userId) && userId > 0 ? userId : null;
        }

        // Van-e a foglaláshoz kötött kiadás, amelynek a kulcsa most is kint van.
        private static Task<bool> HasIssuedKeyForBookingAsync(AppDbContext db, int bookingId) =>
            db.Kulcsmozgasok
                .Where(movement =>
                    movement.FoglalasId == bookingId &&
                    (movement.MozgasTipus == KeyMovementTypes.Issue || movement.MozgasTipus == KeyMovementTypes.LegacyIssue) &&
                    !db.Kulcsmozgasok.Any(newer =>
                        newer.KulcsId == movement.KulcsId &&
                        (newer.Idopont > movement.Idopont ||
                         (newer.Idopont == movement.Idopont && newer.Id > movement.Id))))
                .AnyAsync();

        private static bool IsOverlapException(InvalidOperationException exception) =>
            exception is BookingConflictException;
    }
}
