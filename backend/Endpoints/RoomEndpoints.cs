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
        public static RouteGroupBuilder MapRoomEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/rooms")
                .WithTags("Rooms");

            // R1 – GET /api/rooms
            group.MapGet("/", async (bool? aktivalva, AppDbContext db, ILogger<Program> logger) =>
            {
                try
                {
                    var query = db.Termek.AsNoTracking();
                    if (aktivalva.HasValue)
                    {
                        query = query.Where(t => t.Aktivalva == aktivalva.Value);
                    }

                    var rooms = await query
                        .OrderBy(t => t.Nev)
                        .Select(t => new RoomDto
                        {
                            Id = t.Id,
                            Nev = t.Nev,
                            Kapacitas = t.Kapacitas,
                            Leiras = t.Leiras,
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
            .Produces<List<RoomDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

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
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi();

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

                if (request.Kapacitas < 1)
                {
                    return Results.BadRequest(new { message = "A terem kapacitásának legalább 1-nek kell lennie." });
                }

                var room = new Terem
                {
                    Nev = request.Nev.Trim(),
                    Kapacitas = request.Kapacitas,
                    Leiras = request.Leiras?.Trim() ?? string.Empty,
                    Aktivalva = request.Aktivalva
                };

                db.Termek.Add(room);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CREATE",
                    entitasTipus: "Terem",
                    entitasId: room.Id.ToString(),
                    ujErtek: $"Nev: {room.Nev}, Kapacitas: {room.Kapacitas}, Aktivalva: {room.Aktivalva}");

                var result = new RoomDto
                {
                    Id = room.Id,
                    Nev = room.Nev,
                    Kapacitas = room.Kapacitas,
                    Leiras = room.Leiras,
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
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

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

                if (request.Kapacitas.HasValue && request.Kapacitas.Value < 1)
                {
                    return Results.BadRequest(new { message = "A terem kapacitásának legalább 1-nek kell lennie." });
                }

                var changes = new List<string>();

                if (!string.IsNullOrWhiteSpace(request.Nev) && request.Nev.Trim() != room.Nev)
                {
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

                if (request.Aktivalva.HasValue && request.Aktivalva.Value != room.Aktivalva)
                {
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
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // R5 – DELETE /api/rooms/{id} (Deaktiválás)
            group.MapDelete("/{id:int}", async (
                int id,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var room = await db.Termek.FindAsync(id);
                if (room is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú terem nem található." });
                }

                // Soft delete: deactivation
                room.Aktivalva = false;
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "DEACTIVATE",
                    entitasTipus: "Terem",
                    entitasId: id.ToString(),
                    ujErtek: "Aktivalva: false");

                return Results.Ok(new { message = $"A(z) {id} azonosítójú terem sikeresen deaktiválva lett." });
            })
            .WithName("DeleteRoom")
            .WithSummary("Terem deaktiválása")
            .RequireAuthorization("Adminisztrator")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

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

                var targetDate = (datum ?? DateTime.UtcNow).Date;
                var dayStart = targetDate.AddHours(8); // Campus nyitás: 08:00
                var dayEnd = targetDate.AddHours(20);   // Campus zárás: 20:00

                // Foglalások az adott napra
                var bookings = await db.Foglalasok
                    .AsNoTracking()
                    .Include(f => f.Felhasznalo)
                    .Where(f => f.TeremId == id &&
                                f.Status != "Megszuntetve" &&
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
                    Datum = targetDate,
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
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

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
                    .Where(k => k.TeremId == id)
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
                    .Where(kt => kt.TeremId == id)
                    .Include(kt => kt.Kulcs)
                    .Select(kt => new RoomKeyDto
                    {
                        Id = kt.Kulcs.Id,
                        Szam = kt.Kulcs.Szam,
                        Tipus = kt.Kulcs.Tipus,
                        Aktivalva = kt.Kulcs.Aktivalva,
                        IsPrimaryRoomKey = false
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
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            return group;
        }
    }
}
