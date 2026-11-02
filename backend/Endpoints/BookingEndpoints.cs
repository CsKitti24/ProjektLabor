using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Bookings;
using ProjektLabor.Models;
using ProjektLabor.Security;
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
                    caller.IsInRole(RoleNames.Portas) ||
                    caller.IsInRole(RoleNames.Adminisztrator) ||
                    caller.IsInRole(RoleNames.UzemeltetesiIgazgato);
                if (!canReadAnyBooking &&
                    (!caller.IsInRole(RoleNames.Oktato) || f.FelhasznaloId != callerId))
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
                var subject = caller.FindFirst("sub")?.Value
                    ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(subject, out var callerId) || callerId <= 0)
                {
                    return Results.Unauthorized();
                }

                if (request.StartTime >= request.EndTime)
                {
                    return Results.BadRequest(new { message = "A kezdési időpontnak meg kell előznie a befejezési időpontot." });
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

                var isAdmin = caller.IsInRole(RoleNames.Adminisztrator);
                var targetUserId = callerId;
                if (request.FelhasznaloId.HasValue && isAdmin)
                {
                    var targetUserExists = await db.Felhasznalok
                        .AnyAsync(user => user.Id == request.FelhasznaloId.Value);
                    if (!targetUserExists)
                    {
                        return Results.NotFound(new
                        {
                            message = $"A(z) {request.FelhasznaloId.Value} azonosítójú felhasználó nem található."
                        });
                    }

                    targetUserId = request.FelhasznaloId.Value;
                }

                // Check for overlapping active bookings in the room
                bool hasConflict = await db.Foglalasok.AnyAsync(f =>
                    f.TeremId == request.TeremId &&
                    f.Status != "Megszuntetve" &&
                    request.StartTime < f.EndTime &&
                    request.EndTime > f.StartTime);

                if (hasConflict)
                {
                    return Results.Conflict(new { message = "A megadott idősáv ütközik egy már meglévő foglalással." });
                }

                // Check for overlapping maintenance
                bool maintenanceConflict = await db.KarbantartasiIdosavok.AnyAsync(m =>
                    m.TeremId == request.TeremId &&
                    request.StartTime < m.EndTime &&
                    request.EndTime > m.StartTime);

                if (maintenanceConflict)
                {
                    return Results.Conflict(new { message = "A terem a megadott időszakban karbantartás alatt áll." });
                }

                var booking = new Foglalas
                {
                    FelhasznaloId = targetUserId,
                    TeremId = request.TeremId,
                    StartTime = request.StartTime,
                    EndTime = request.EndTime,
                    Status = "Aktiv",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.Foglalasok.Add(booking);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CREATE",
                    entitasTipus: "Foglalas",
                    entitasId: booking.Id.ToString(),
                    ujErtek: $"TeremId: {booking.TeremId}, Start: {booking.StartTime:O}, End: {booking.EndTime:O}");

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

                var subject = caller.FindFirst("sub")?.Value
                    ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(subject, out var callerId) || callerId <= 0)
                {
                    return Results.Unauthorized();
                }

                bool isAdmin = caller.IsInRole(RoleNames.Adminisztrator);

                if (!isAdmin &&
                    (!caller.IsInRole(RoleNames.Oktato) || booking.FelhasznaloId != callerId))
                {
                    return Results.Forbid();
                }

                var newStart = request.StartTime ?? booking.StartTime;
                var newEnd = request.EndTime ?? booking.EndTime;

                if (newStart >= newEnd)
                {
                    return Results.BadRequest(new { message = "A kezdési időpontnak meg kell előznie a befejezési időpontot." });
                }

                if (request.StartTime.HasValue || request.EndTime.HasValue)
                {
                    bool conflict = await db.Foglalasok.AnyAsync(f =>
                        f.Id != id &&
                        f.TeremId == booking.TeremId &&
                        f.Status != "Megszuntetve" &&
                        newStart < f.EndTime &&
                        newEnd > f.StartTime);

                    if (conflict)
                    {
                        return Results.Conflict(new { message = "Az új időpont ütközik egy már meglévő foglalással." });
                    }
                }

                if (!string.IsNullOrWhiteSpace(request.Status))
                {
                    var canonicalStatus = AllowedStatuses.FirstOrDefault(s => s.Equals(request.Status, StringComparison.OrdinalIgnoreCase));
                    if (canonicalStatus == null)
                    {
                        return Results.BadRequest(new { message = $"Érvénytelen foglalás státusz: '{request.Status}'. Megengedett: Aktiv, NoShow, Megszuntetve, Kesz." });
                    }
                    booking.Status = canonicalStatus;
                }

                booking.StartTime = newStart;
                booking.EndTime = newEnd;
                booking.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "UPDATE",
                    entitasTipus: "Foglalas",
                    entitasId: id.ToString(),
                    ujErtek: $"Start: {booking.StartTime:O}, End: {booking.EndTime:O}, Status: {booking.Status}");

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
                    CreatedAt = booking.CreatedAt,
                    UpdatedAt = booking.UpdatedAt
                };

                return Results.Ok(result);
            })
            .WithName("UpdateBooking")
            .WithSummary("Foglalás időpontjának vagy státuszának módosítása")
            .RequireAuthorization("AdminOrOktato")
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

                var subject = caller.FindFirst("sub")?.Value
                    ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(subject, out var callerId) || callerId <= 0)
                {
                    return Results.Unauthorized();
                }

                bool isAdmin = caller.IsInRole(RoleNames.Adminisztrator);

                if (!isAdmin &&
                    (!caller.IsInRole(RoleNames.Oktato) || booking.FelhasznaloId != callerId))
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
            .RequireAuthorization("AdminOrOktato")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            return group;
        }
    }
}
