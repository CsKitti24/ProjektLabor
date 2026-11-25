using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs;
using ProjektLabor.DTOs.Rooms;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints
{
    public static class RoomEndpoints
    {
        // A terem befogadóképességének felső korlátja (létrehozáskor és szerkesztéskor is).
        private const int MaxCapacity = 10_000;

        public static RouteGroupBuilder MapRoomEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/rooms")
                .WithTags("Rooms");

            // R1 – GET /api/rooms
            group.MapGet("/", async (
                bool? aktivalva,
                int? minKapacitas,
                string? felszereltseg,
                AppDbContext db,
                ILogger<Program> logger) =>
            {
                try
                {
                    var query = db.Termek.AsNoTracking();
                    if (aktivalva.HasValue)
                    {
                        query = query.Where(t => t.Aktivalva == aktivalva.Value);
                    }

                    if (minKapacitas.HasValue)
                    {
                        query = query.Where(t => t.Kapacitas >= minKapacitas.Value);
                    }

                    if (!string.IsNullOrWhiteSpace(felszereltseg))
                    {
                        var term = felszereltseg.Trim();
                        query = query.Where(t =>
                            (t.Felszereltseg != null && t.Felszereltseg.Contains(term)) ||
                            (t.Leiras != null && t.Leiras.Contains(term)));
                    }

                    var rooms = await query
                        .OrderBy(t => t.Nev)
                        .Select(t => new RoomDto
                        {
                            Id = t.Id,
                            Nev = t.Nev,
                            Kapacitas = t.Kapacitas,
                            Leiras = t.Leiras,
                            Felszereltseg = t.Felszereltseg,
                            Aktivalva = t.Aktivalva
                        })
                        .ToListAsync();

                    return Results.Ok(rooms);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error occurred while retrieving rooms from database.");
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Internal Server Error",
                        detail: "Hiba történt a termek lekérdezése során."
                    );
                }
            })
            .WithName("GetRooms")
            .WithSummary("Termek listájának lekérdezése")
            .RequireAuthorization("Authenticated")
            .Produces<List<RoomDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

            // R2 – GET /api/rooms/{id}
            group.MapGet("/{id:int}", async (int id, AppDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    var room = await db.Termek
                        .AsNoTracking()
                        .Where(t => t.Id == id)
                        .Select(t => new RoomDto
                        {
                            Id = t.Id,
                            Nev = t.Nev,
                            Kapacitas = t.Kapacitas,
                            Leiras = t.Leiras,
                            Felszereltseg = t.Felszereltseg,
                            Aktivalva = t.Aktivalva
                        })
                        .FirstOrDefaultAsync();

                    if (room is null)
                    {
                        return Results.NotFound(new { message = $"A(z) {id} azonosítójú terem nem található." });
                    }

                    return Results.Ok(room);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error occurred while retrieving room {RoomId} from database.", id);
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Internal Server Error",
                        detail: "Hiba történt a terem lekérdezése során."
                    );
                }
            })
            .WithName("GetRoomById")
            .WithSummary("Egy terem adatainak lekérdezése azonosító alapján")
            .RequireAuthorization("Authenticated")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

            // R3 – POST /api/rooms
            group.MapPost("/", async (
                CreateRoomRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                if (string.IsNullOrWhiteSpace(request.Nev))
                {
                    return Results.BadRequest(new { message = "A terem nevének megadása kötelező." });
                }

                if (request.Kapacitas < 1 || request.Kapacitas > MaxCapacity)
                {
                    return Results.BadRequest(new { message = "A terem kapacitása 1 és 10 000 között lehet." });
                }

                var normalizedName = request.Nev.Trim();
                if (await RoomNameTakenAsync(db, normalizedName, exceptRoomId: null))
                {
                    return Results.Conflict(new { message = $"Már létezik '{normalizedName}' nevű terem (esetleg inaktívként)." });
                }

                var room = new Terem
                {
                    Nev = request.Nev.Trim(),
                    Kapacitas = request.Kapacitas,
                    Leiras = request.Leiras?.Trim() ?? string.Empty,
                    Felszereltseg = request.Felszereltseg?.Trim(),
                    Aktivalva = request.Aktivalva
                };

                db.Termek.Add(room);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CREATE",
                    entitasTipus: "Terem",
                    entitasId: room.Id.ToString(),
                    ujErtek: $"Nev: {room.Nev}, Kapacitas: {room.Kapacitas}, Felszereltseg: {room.Felszereltseg}, Aktivalva: {room.Aktivalva}");

                var result = new RoomDto
                {
                    Id = room.Id,
                    Nev = room.Nev,
                    Kapacitas = room.Kapacitas,
                    Leiras = room.Leiras,
                    Felszereltseg = room.Felszereltseg,
                    Aktivalva = room.Aktivalva
                };

                return Results.Created($"/api/rooms/{room.Id}", result);
            })
            .WithName("CreateRoom")
            .WithSummary("Új terem rögzítése")
            .RequireAuthorization("Adminisztrator")
            .Produces<RoomDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            // R4 – PUT /api/rooms/{id}
            group.MapPut("/{id:int}", async (
                int id,
                UpdateRoomRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var room = await db.Termek.FindAsync(id);
                if (room is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú terem nem található." });
                }

                if (request.Kapacitas.HasValue && (request.Kapacitas.Value < 1 || request.Kapacitas.Value > MaxCapacity))
                {
                    return Results.BadRequest(new { message = "A terem kapacitása 1 és 10 000 között lehet." });
                }

                var changes = new List<string>();

                if (!string.IsNullOrWhiteSpace(request.Nev) && request.Nev.Trim() != room.Nev)
                {
                    if (await RoomNameTakenAsync(db, request.Nev.Trim(), exceptRoomId: room.Id))
                    {
                        return Results.Conflict(new { message = $"Már létezik '{request.Nev.Trim()}' nevű terem (esetleg inaktívként)." });
                    }

                    changes.Add($"Nev: '{room.Nev}' -> '{request.Nev.Trim()}'");
                    room.Nev = request.Nev.Trim();
                }

                if (request.Kapacitas.HasValue && request.Kapacitas.Value != room.Kapacitas)
                {
                    changes.Add($"Kapacitas: {room.Kapacitas} -> {request.Kapacitas.Value}");
                    room.Kapacitas = request.Kapacitas.Value;
                }

                if (request.Leiras != null && request.Leiras.Trim() != room.Leiras)
                {
                    changes.Add($"Leiras: '{room.Leiras}' -> '{request.Leiras.Trim()}'");
                    room.Leiras = request.Leiras.Trim();
                }

                if (request.Felszereltseg != null && request.Felszereltseg.Trim() != room.Felszereltseg)
                {
                    changes.Add($"Felszereltseg: '{room.Felszereltseg}' -> '{request.Felszereltseg.Trim()}'");
                    room.Felszereltseg = request.Felszereltseg.Trim();
                }

                if (request.Aktivalva.HasValue && request.Aktivalva.Value != room.Aktivalva)
                {
                    // Deaktiválásra ugyanaz a szabály vonatkozik, mint a törlésnél: jövőbeli aktív
                    // foglalás mellett a terem nem deaktiválható.
                    if (!request.Aktivalva.Value)
                    {
                        if (await IsRoomKeyIssuedAsync(db, id))
                        {
                            return Results.Conflict(new { message = "A terem kulcsa jelenleg ki van adva. Deaktiválás előtt vedd vissza a kulcsot." });
                        }

                        var now = DateTime.UtcNow;
                        var futureBookings = await db.Foglalasok.CountAsync(booking =>
                            booking.TeremId == id &&
                            booking.Status == BookingRules.ActiveStatus &&
                            booking.EndTime > now);
                        if (futureBookings > 0)
                        {
                            return Results.Conflict(new
                            {
                                message = $"A teremnek {futureBookings} jövőbeli aktív foglalása van. Deaktiválás előtt ezeket le kell mondani (a terem törlésével ez egy lépésben is megtehető).",
                                jovobeliFoglalasok = futureBookings
                            });
                        }
                    }

                    changes.Add($"Aktivalva: {room.Aktivalva} -> {request.Aktivalva.Value}");
                    room.Aktivalva = request.Aktivalva.Value;
                }

                if (changes.Count > 0)
                {
                    await db.SaveChangesAsync();

                    await auditService.LogAsync(
                        caller,
                        muveletTipus: "UPDATE",
                        entitasTipus: "Terem",
                        entitasId: id.ToString(),
                        ujErtek: string.Join("; ", changes));
                }

                var result = new RoomDto
                {
                    Id = room.Id,
                    Nev = room.Nev,
                    Kapacitas = room.Kapacitas,
                    Leiras = room.Leiras,
                    Felszereltseg = room.Felszereltseg,
                    Aktivalva = room.Aktivalva
                };

                return Results.Ok(result);
            })
            .WithName("UpdateRoom")
            .WithSummary("Terem adatainak módosítása")
            .RequireAuthorization("Adminisztrator")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            // R5 – DELETE /api/rooms/{id} (Deaktiválás)
            group.MapDelete("/{id:int}", async (
                int id,
                bool? foglalasokLemondasa,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller,
                TimeProvider timeProvider) =>
            {
                // Ugyanazzal a zárral, mint a foglalás: deaktiválás közben nem jöhet létre új foglalás a teremre.
                return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
                {
                await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                    ? null
                    : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                await SqlServerLocking.AcquireAsync(db, $"room-booking:{id}");

                var room = await db.Termek.FindAsync(id);
                if (room is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú terem nem található." });
                }

                if (!room.Aktivalva)
                {
                    return Results.Conflict(new { message = "A terem már deaktivált." });
                }

                // Amíg a terem kulcsa (vagy egy erre a teremre kiadott mesterkulcs) kint van, a terem nem
                // deaktiválható: előbb vissza kell venni, különben a kulcs "elveszne" a nyilvántartásból.
                if (await IsRoomKeyIssuedAsync(db, id))
                {
                    return Results.Conflict(new { message = "A terem kulcsa jelenleg ki van adva. Deaktiválás előtt vedd vissza a kulcsot." });
                }

                // A még le nem zajlott aktív foglalások nem maradhatnak egy deaktivált teremben:
                // vagy az admin kifejezetten kéri a lemondásukat, vagy a deaktiválás elmarad.
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var futureBookings = await db.Foglalasok
                    .Where(booking =>
                        booking.TeremId == id &&
                        booking.Status == BookingRules.ActiveStatus &&
                        booking.EndTime > now)
                    .ToListAsync();
                if (futureBookings.Count > 0 && foglalasokLemondasa != true)
                {
                    return Results.Conflict(new
                    {
                        message = $"A teremnek {futureBookings.Count} jövőbeli aktív foglalása van. Deaktiválás előtt ezeket le kell mondani.",
                        jovobeliFoglalasok = futureBookings.Count
                    });
                }

                foreach (var booking in futureBookings)
                {
                    booking.Status = BookingRules.CancelledStatus;
                    booking.UpdatedAt = now;
                    BookingNotifications.AddCancelled(
                        db,
                        booking,
                        $"A(z) {room.Nev} terem foglalását lemondtuk, mert a terem megszűnt vagy nem használható.",
                        now);
                }

                // Soft delete: deactivation
                room.Aktivalva = false;
                await db.SaveChangesAsync();

                foreach (var booking in futureBookings)
                {
                    await auditService.LogAsync(
                        caller,
                        muveletTipus: "CANCEL",
                        entitasTipus: "Foglalas",
                        entitasId: booking.Id.ToString(),
                        ujErtek: $"Status: {BookingRules.CancelledStatus}",
                        indoklas: $"A(z) {room.Nev} terem deaktiválása miatt lemondva.");
                }

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "DEACTIVATE",
                    entitasTipus: "Terem",
                    entitasId: id.ToString(),
                    ujErtek: "Aktivalva: false");

                if (transaction is not null)
                {
                    await transaction.CommitAsync();
                }

                return Results.Ok(new { message = $"A(z) {id} azonosítójú terem sikeresen deaktiválva lett." });
                });
            })
            .WithName("DeleteRoom")
            .Produces(StatusCodes.Status409Conflict)
            .WithSummary("Terem deaktiválása")
            .RequireAuthorization("Adminisztrator")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            // R6 – GET /api/rooms/{id}/availability
            group.MapGet("/{id:int}/availability", async (
                int id,
                DateTime? datum,
                AppDbContext db) =>
            {
                var roomExists = await db.Termek.AnyAsync(t => t.Id == id);
                if (!roomExists)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú terem nem található." });
                }

                // A napot az intézmény helyi naptára szerint értelmezzük; a határok UTC-ben vannak.
                var targetDate = datum.HasValue
                    ? datum.Value.Kind == DateTimeKind.Unspecified
                        ? DateOnly.FromDateTime(datum.Value)
                        : AppTime.LocalDate(datum.Value)
                    : AppTime.LocalDate(DateTime.UtcNow);
                var dayStart = AppTime.LocalToUtc(targetDate, TimeOnly.FromTimeSpan(BookingRules.OpeningTime));
                var dayEnd = AppTime.LocalToUtc(targetDate, TimeOnly.FromTimeSpan(BookingRules.ClosingTime));

                // Foglalások az adott napra
                var bookings = await db.Foglalasok
                    .AsNoTracking()
                    .Include(f => f.Felhasznalo)
                    .Where(BookingRules.ActiveBookings)
                    .Where(f => f.TeremId == id &&
                                f.StartTime < dayEnd &&
                                f.EndTime > dayStart)
                    .OrderBy(f => f.StartTime)
                    .Select(f => new BookingSlotDto
                    {
                        FoglalasId = f.Id,
                        StartTime = f.StartTime,
                        EndTime = f.EndTime,
                        Statusz = f.Status,
                        FelhasznaloNev = f.Felhasznalo != null ? f.Felhasznalo.Nev : null
                    })
                    .ToListAsync();

                // Karbantartási idősávok az adott napra
                var maintenances = await db.KarbantartasiIdosavok
                    .AsNoTracking()
                    .Where(m => m.TeremId == id &&
                                m.StartTime < dayEnd &&
                                m.EndTime > dayStart)
                    .OrderBy(m => m.StartTime)
                    .Select(m => new MaintenanceSlotDto
                    {
                        Id = m.Id,
                        StartTime = m.StartTime,
                        EndTime = m.EndTime,
                        Leiras = m.Leiras
                    })
                    .ToListAsync();

                // Foglalt intervallumok összesítése
                var busyIntervals = new List<(DateTime Start, DateTime End)>();
                foreach (var b in bookings)
                {
                    busyIntervals.Add((
                        b.StartTime < dayStart ? dayStart : b.StartTime,
                        b.EndTime > dayEnd ? dayEnd : b.EndTime
                    ));
                }
                foreach (var m in maintenances)
                {
                    busyIntervals.Add((
                        m.StartTime < dayStart ? dayStart : m.StartTime,
                        m.EndTime > dayEnd ? dayEnd : m.EndTime
                    ));
                }

                // Szabad idősávok számítása 08:00 és 20:00 között
                var freeSlots = new List<TimeSlotDto>();
                var sortedBusy = busyIntervals.OrderBy(x => x.Start).ToList();
                var currentPointer = dayStart;

                foreach (var busy in sortedBusy)
                {
                    if (busy.Start > currentPointer)
                    {
                        freeSlots.Add(new TimeSlotDto
                        {
                            StartTime = currentPointer,
                            EndTime = busy.Start
                        });
                    }
                    if (busy.End > currentPointer)
                    {
                        currentPointer = busy.End;
                    }
                }

                if (currentPointer < dayEnd)
                {
                    freeSlots.Add(new TimeSlotDto
                    {
                        StartTime = currentPointer,
                        EndTime = dayEnd
                    });
                }

                var availability = new RoomAvailabilityDto
                {
                    TeremId = id,
                    Datum = targetDate.ToDateTime(TimeOnly.MinValue),
                    Szabad = freeSlots,
                    Foglalt = bookings,
                    Karbantartas = maintenances
                };

                return Results.Ok(availability);
            })
            .WithName("GetRoomAvailability")
            .WithSummary("Terem szabad és foglalt időszakjainak lekérdezése")
            .RequireAuthorization("Authenticated")
            .Produces<RoomAvailabilityDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

            // R7 – GET /api/rooms/{id}/keys
            group.MapGet("/{id:int}/keys", async (int id, AppDbContext db) =>
            {
                var roomExists = await db.Termek.AnyAsync(t => t.Id == id);
                if (!roomExists)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú terem nem található." });
                }

                var directKeys = await db.Kulcsok
                    .AsNoTracking()
                    .Where(k => k.Tipus != "Mesterkulcs" && k.TeremId == id)
                    .Select(k => new RoomKeyDto
                    {
                        Id = k.Id,
                        Szam = k.Szam,
                        Tipus = k.Tipus,
                        Aktivalva = k.Aktivalva,
                        IsPrimaryRoomKey = true
                    })
                    .ToListAsync();

                var masterKeys = await db.KulcsTermek
                    .AsNoTracking()
                    .Where(kt => kt.TeremId == id && kt.Kulcs.Tipus == "Mesterkulcs")
                    .Include(kt => kt.Kulcs)
                    .Select(kt => new RoomKeyDto
                    {
                        Id = kt.Kulcs.Id,
                        Szam = kt.Kulcs.Szam,
                        Tipus = kt.Kulcs.Tipus,
                        Aktivalva = kt.Kulcs.Aktivalva,
                        IsPrimaryRoomKey = kt.Kulcs.TeremId == id
                    })
                    .ToListAsync();

                var allKeys = directKeys
                    .Concat(masterKeys.Where(mk => directKeys.All(dk => dk.Id != mk.Id)))
                    .OrderBy(k => k.Szam)
                    .ToList();

                return Results.Ok(allKeys);
            })
            .WithName("GetRoomKeys")
            .WithSummary("Teremhez tartozó kulcsok listázása")
            .RequireAuthorization("AdminOrPortasOrIgazgato")
            .Produces<List<RoomKeyDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

            return group;
        }

        // Ki van-e adva most a terem saját kulcsa, vagy egy erre a teremre kiadott mesterkulcs.
        private static Task<bool> IsRoomKeyIssuedAsync(AppDbContext db, int roomId) =>
            db.Kulcsmozgasok
                .Where(movement =>
                    (movement.TeremId == roomId || (movement.TeremId == null && movement.Kulcs.TeremId == roomId)) &&
                    !db.Kulcsmozgasok.Any(newer =>
                        newer.KulcsId == movement.KulcsId &&
                        (newer.Idopont > movement.Idopont ||
                         (newer.Idopont == movement.Idopont && newer.Id > movement.Id))))
                .AnyAsync(movement =>
                    movement.MozgasTipus == KeyMovementTypes.Issue ||
                    movement.MozgasTipus == KeyMovementTypes.LegacyIssue);

        // A teremnév egyedi (kis- és nagybetűtől függetlenül), az inaktív termeket is beleértve.
        private static Task<bool> RoomNameTakenAsync(AppDbContext db, string name, int? exceptRoomId)
        {
            var lowered = name.ToLower();
            return db.Termek.AnyAsync(room =>
                room.Nev.ToLower() == lowered &&
                (exceptRoomId == null || room.Id != exceptRoomId));
        }
    }
}
