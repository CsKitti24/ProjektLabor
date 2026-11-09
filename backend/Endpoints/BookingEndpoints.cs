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
        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Aktiv",
            "NoShow",
            "Megszuntetve",
            "Kesz"
        };

        public static RouteGroupBuilder MapBookingEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/bookings")
                .WithTags("Bookings")
                .RequireAuthorization("Authenticated");

            // B6 – GET /api/bookings/my (Regisztráljuk az /{id} előtt!)
            group.MapGet("/my", async (
                string? status,
                AppDbContext db,
                ClaimsPrincipal caller) =>
            {
                var subClaim = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value;
                if (!int.TryParse(subClaim, out int userId))
                {
                    return Results.Unauthorized();
                }

                var query = db.Foglalasok
                    .AsNoTracking()
                    .Include(f => f.Terem)
                    .Include(f => f.Felhasznalo)
                    .Where(f => f.FelhasznaloId == userId);

                if (!string.IsNullOrWhiteSpace(status))
                {
                    query = query.Where(f => f.Status == status);
                }

                var list = await query
                    .OrderByDescending(f => f.StartTime)
                    .Select(f => new BookingDto
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
                    })
                    .ToListAsync();

                return Results.Ok(list);
            })
            .WithName("GetMyBookings")
            .WithSummary("A bejelentkezett felhasználó saját foglalásai")
            .RequireAuthorization("Oktato")
            .Produces<List<BookingDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // B1 – GET /api/bookings
            group.MapGet("/", async (
                int? teremId,
                int? felhasznaloId,
                string? status,
                DateTime? tol,
                DateTime? ig,
                int page,
                int pageSize,
                AppDbContext db) =>
            {
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);

                var query = db.Foglalasok
                    .AsNoTracking()
                    .Include(f => f.Terem)
                    .Include(f => f.Felhasznalo)
                    .AsQueryable();

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
                    query = query.Where(f => f.EndTime >= tol.Value);
                }

                if (ig.HasValue)
                {
                    query = query.Where(f => f.StartTime <= ig.Value);
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(f => f.StartTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(f => new BookingDto
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
                    })
                    .ToListAsync();

                return Results.Ok(new BookingListDto
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            })
            .WithName("GetBookings")
            .WithSummary("Foglalások listázása szűrőkkel és lapozással")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<BookingListDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // B2 – GET /api/bookings/{id}
            group.MapGet("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal caller) =>
            {
                var f = await db.Foglalasok
                    .AsNoTracking()
                    .Include(f => f.Terem)
                    .Include(f => f.Felhasznalo)
                    .FirstOrDefaultAsync(f => f.Id == id);

                if (f is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú foglalás nem található." });
                }

                var subject = caller.FindFirst("sub")?.Value
                    ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(subject, out var callerId) || callerId <= 0)
                {
                    return Results.Unauthorized();
                }

                var canReadAnyBooking =
                    caller.IsInRole("Portas") ||
                    caller.IsInRole("Adminisztrator") ||
                    caller.IsInRole("UzemeltetesiIgazgato");
                if (!canReadAnyBooking &&
                    (!caller.IsInRole("Oktato") || f.FelhasznaloId != callerId))
                {
                    return Results.Forbid();
                }

                var dto = new BookingDto
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

                return Results.Ok(dto);
            })
            .WithName("GetBookingById")
            .WithSummary("Egy foglalás részletes adatainak lekérdezése")
            .Produces<BookingDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // B3 – POST /api/bookings
            group.MapPost("/", async (
                CreateBookingRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var timeError = BookingRules.ValidateTimeRange(request.StartTime, request.EndTime);
                if (timeError is not null)
                {
                    return Results.BadRequest(new { message = timeError });
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

                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                await SqlServerLocking.AcquireAsync(db, $"room-booking:{request.TeremId}");

                // Determine target user
                int targetUserId;
                var subClaim = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value;
                int.TryParse(subClaim, out int callerId);

                bool isAdmin = caller.IsInRole("Adminisztrator");

                if (request.FelhasznaloId.HasValue && isAdmin)
                {
                    bool userExists = await db.Felhasznalok.AnyAsync(u => u.Id == request.FelhasznaloId.Value);
                    if (!userExists)
                    {
                        return Results.NotFound(new { message = $"A(z) {request.FelhasznaloId.Value} azonosítójú felhasználó nem található." });
                    }
                    targetUserId = request.FelhasznaloId.Value;
                }
                else
                {
                    targetUserId = callerId;
                }

                // Check for overlapping active bookings in the room
                var validationError = await BookingRules.ValidateAsync(
                    db, request.TeremId, request.StartTime, request.EndTime);
                if (validationError is not null)
                {
                    return Results.Conflict(new { message = validationError });
                }

                var booking = new Foglalas
                {
                    FelhasznaloId = targetUserId,
                    TeremId = request.TeremId,
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    Status = "Aktiv",
                    SpecialisIgenyek = string.IsNullOrWhiteSpace(request.SpecialisIgenyek) ? null : request.SpecialisIgenyek.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.Foglalasok.Add(booking);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("overlaps with an existing booking") || ex.Message.Contains("overlaps with a maintenance period"))
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

                var user = await db.Felhasznalok.FindAsync(targetUserId);

                var result = new BookingDto
                {
                    Id = booking.Id,
                    FelhasznaloId = booking.FelhasznaloId,
                    FelhasznaloNev = user?.Nev ?? string.Empty,
                    FelhasznaloEmail = user?.Email ?? string.Empty,
                    TeremId = booking.TeremId,
                    TeremNev = room.Nev,
                    StartTime = booking.StartTime,
                    EndTime = booking.EndTime,
                    Status = booking.Status,
                    SpecialisIgenyek = booking.SpecialisIgenyek,
                    CreatedAt = booking.CreatedAt,
                    UpdatedAt = booking.UpdatedAt
                };

                return Results.Created($"/api/bookings/{booking.Id}", result);
            })
            .WithName("CreateBooking")
            .WithSummary("Új teremfoglalás létrehozása")
            .RequireAuthorization("AdminOrOktato")
            .Produces<BookingDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // B4 – PUT /api/bookings/{id}
            group.MapPut("/{id:int}", async (
                int id,
                UpdateBookingRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var booking = await db.Foglalasok
                    .Include(f => f.Terem)
                    .Include(f => f.Felhasznalo)
                    .FirstOrDefaultAsync(f => f.Id == id);

                if (booking is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú foglalás nem található." });
                }

                var subClaim = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value;
                int.TryParse(subClaim, out int callerId);
                bool isAdmin = caller.IsInRole("Adminisztrator");

                if (!isAdmin && booking.FelhasznaloId != callerId)
                {
                    return Results.Forbid();
                }
                if (!isAdmin && request.TeremId.HasValue)
                {
                    return Results.Forbid();
                }

                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var requestedRoomId = request.TeremId ?? booking.TeremId;
                foreach (var roomId in new[] { booking.TeremId, requestedRoomId }.Distinct().Order())
                {
                    await SqlServerLocking.AcquireAsync(db, $"room-booking:{roomId}");
                }

                var newStart = request.StartTime ?? booking.StartTime;
                var newEnd = request.EndTime ?? booking.EndTime;
                var newRoomId = requestedRoomId;
                var targetRoom = newRoomId == booking.TeremId
                    ? booking.Terem
                    : await db.Termek.SingleOrDefaultAsync(room => room.Id == newRoomId);
                if (targetRoom is null)
                {
                    return Results.NotFound(new { message = "A megadott terem nem található." });
                }

                var timeError = BookingRules.ValidateTimeRange(newStart, newEnd);
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

                if (!string.IsNullOrWhiteSpace(request.Status))
                {
                    var canonicalStatus = AllowedStatuses.FirstOrDefault(s => s.Equals(request.Status, StringComparison.OrdinalIgnoreCase));
                    if (canonicalStatus == null)
                    {
                        return Results.BadRequest(new { message = $"Érvénytelen foglalás státusz: '{request.Status}'. Megengedett: Aktiv, NoShow, Megszuntetve, Kesz." });
                    }
                    if (!BookingRules.IsUserStatusChangeAllowed(booking.Status, canonicalStatus, isAdmin))
                    {
                        return Results.Forbid();
                    }
                    booking.Status = canonicalStatus;
                }

                if (request.SpecialisIgenyek != null)
                {
                    booking.SpecialisIgenyek = string.IsNullOrWhiteSpace(request.SpecialisIgenyek) ? null : request.SpecialisIgenyek.Trim();
                }

                booking.StartTime = newStart;
                booking.EndTime = newEnd;
                booking.TeremId = newRoomId;
                booking.Terem = targetRoom;
                booking.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("overlaps with an existing booking") || ex.Message.Contains("overlaps with a maintenance period"))
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

                var result = new BookingDto
                {
                    Id = booking.Id,
                    FelhasznaloId = booking.FelhasznaloId,
                    FelhasznaloNev = booking.Felhasznalo?.Nev ?? string.Empty,
                    FelhasznaloEmail = booking.Felhasznalo?.Email ?? string.Empty,
                    TeremId = booking.TeremId,
                    TeremNev = booking.Terem?.Nev ?? string.Empty,
                    StartTime = booking.StartTime,
                    EndTime = booking.EndTime,
                    Status = booking.Status,
                    SpecialisIgenyek = booking.SpecialisIgenyek,
                    CreatedAt = booking.CreatedAt,
                    UpdatedAt = booking.UpdatedAt
                };

                return Results.Ok(result);
            })
            .WithName("UpdateBooking")
            .WithSummary("Foglalás időpontjának vagy státuszának módosítása")
            .Produces<BookingDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // B5 – DELETE /api/bookings/{id} (Lemondás)
            group.MapDelete("/{id:int}", async (
                int id,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var booking = await db.Foglalasok.FindAsync(id);
                if (booking is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú foglalás nem található." });
                }

                var subClaim = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value;
                int.TryParse(subClaim, out int callerId);
                bool isAdmin = caller.IsInRole("Adminisztrator");

                if (!isAdmin && booking.FelhasznaloId != callerId)
                {
                    return Results.Forbid();
                }

                booking.Status = "Megszuntetve";
                booking.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CANCEL",
                    entitasTipus: "Foglalas",
                    entitasId: id.ToString(),
                    ujErtek: "Status: Megszuntetve");

                return Results.Ok(new { message = $"A(z) {id} azonosítójú foglalás sikeresen lemondásra került." });
            })
            .WithName("DeleteBooking")
            .WithSummary("Foglalás lemondása")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            return group;
        }
    }
}
