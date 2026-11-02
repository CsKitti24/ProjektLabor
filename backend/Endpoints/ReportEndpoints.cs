using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints;

public static class ReportEndpoints
{
    private const int MaximumRangeDays = 366;
    private const string InstructorRole = "Oktato";
    private const string LateReturnEvent = "Késedelmesen leadott";
    private const string IssueMovement = "Kiadás";
    private const string LegacyIssueMovement = "Kiadas";
    private const string ReturnMovement = "Visszavétel";
    private const string LegacyReturnMovement = "Visszavetel";

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reports = endpoints.MapGroup("/api/reports")
            .WithTags("Reports")
            .RequireAuthorization("AdminOrIgazgato");

        reports.MapGet("/daily", GetDailyAsync)
            .WithName("GetDailyReport")
            .WithSummary("Napi kulcs- és teremforgalmi riport")
            .Produces<DailyReportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();
        reports.MapGet("/weekly", GetWeeklyAsync)
            .WithName("GetWeeklyReport")
            .WithSummary("Heti forgalmi összesítő")
            .Produces<WeeklyReportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();
        reports.MapGet("/room-utilization", GetRoomUtilizationAsync)
            .WithName("GetRoomUtilizationReport")
            .WithSummary("Termek foglalási kihasználtsága")
            .Produces<RoomUtilizationReportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();
        reports.MapGet("/key-traffic", GetKeyTrafficAsync)
            .WithName("GetKeyTrafficReport")
            .WithSummary("Kulcsok forgalmi statisztikája")
            .Produces<KeyTrafficReportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();
        reports.MapGet("/reliability", GetReliabilityAsync)
            .WithName("GetReliabilityReport")
            .WithSummary("Rendszerszintű megbízhatósági összesítő")
            .Produces<ReliabilityReportResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

        return endpoints;
    }

    private static async Task<IResult> GetDailyAsync(
        string? datum,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryParseDate(datum, out var day) || day == DateOnly.MaxValue)
        {
            return InvalidDate("A datum paraméter megadása kötelező YYYY-MM-DD formátumban, érvényes napra.");
        }

        var start = ToStart(day);
        var end = start.AddDays(1);
        var traffic = await GetTrafficAsync(start, end, db, cancellationToken);

        return Results.Ok(new DailyReportResponse(
            day,
            traffic.Issues,
            traffic.Returns,
            traffic.LateBookings,
            traffic.NoShows,
            traffic.Rooms));
    }

    private static async Task<IResult> GetWeeklyAsync(
        string? datum,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var day = datum is null
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : TryParseDate(datum, out var parsed)
                ? parsed
                : (DateOnly?)null;

        if (!day.HasValue)
        {
            return InvalidDate("A datum paraméternek YYYY-MM-DD formátumúnak kell lennie.");
        }

        var daysSinceMonday = ((int)day.Value.DayOfWeek + 6) % 7;
        if (day.Value.DayNumber < daysSinceMonday)
        {
            return InvalidDate("A megadott dátum hetének kezdete kívül esik az érvényes dátumtartományon.");
        }

        var monday = day.Value.AddDays(-daysSinceMonday);
        if (monday.DayNumber > DateOnly.MaxValue.DayNumber - 7)
        {
            return InvalidDate("A megadott dátum hetének vége kívül esik az érvényes dátumtartományon.");
        }

        var start = ToStart(monday);
        var end = start.AddDays(7);
        var traffic = await GetTrafficAsync(start, end, db, cancellationToken);

        return Results.Ok(new WeeklyReportResponse(
            monday,
            monday.AddDays(6),
            traffic.Issues,
            traffic.Returns,
            traffic.LateBookings,
            traffic.NoShows,
            traffic.Rooms));
    }

    private static async Task<IResult> GetRoomUtilizationAsync(
        string? kezdet,
        string? veg,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryResolveRange(kezdet, veg, out var startDay, out var endDay, out var error))
        {
            return Results.BadRequest(new { message = error });
        }

        var start = ToStart(startDay);
        var end = ToStart(endDay.AddDays(1));
        var rooms = await db.Termek
            .AsNoTracking()
            .Where(room => room.Aktivalva)
            .OrderBy(room => room.Nev)
            .Select(room => new { room.Id, room.Nev })
            .ToListAsync(cancellationToken);

        var bookings = await db.Foglalasok
            .AsNoTracking()
            .Where(booking =>
                booking.StartTime < end &&
                booking.EndTime > start &&
                booking.Status != "NoShow" &&
                booking.Status != "Megszuntetve")
            .Select(booking => new { booking.TeremId, booking.StartTime, booking.EndTime })
            .ToListAsync(cancellationToken);
        var maintenances = await db.KarbantartasiIdosavok
            .AsNoTracking()
            .Where(maintenance =>
                maintenance.StartTime < end &&
                maintenance.EndTime > start)
            .Select(maintenance => new
            {
                maintenance.TeremId,
                maintenance.StartTime,
                maintenance.EndTime
            })
            .ToListAsync(cancellationToken);

        var roomReports = rooms.Select(room =>
        {
            var bookedHours = bookings
                .Where(booking => booking.TeremId == room.Id)
                .Sum(booking =>
                {
                    var overlapStart = booking.StartTime > start ? booking.StartTime : start;
                    var overlapEnd = booking.EndTime < end ? booking.EndTime : end;
                    return Math.Max(0, (overlapEnd - overlapStart).TotalHours);
                });

            var maintenanceHours = maintenances
                .Where(maintenance => maintenance.TeremId == room.Id)
                .Sum(maintenance =>
                {
                    var overlapStart = maintenance.StartTime > start ? maintenance.StartTime : start;
                    var overlapEnd = maintenance.EndTime < end ? maintenance.EndTime : end;
                    return Math.Max(0, (overlapEnd - overlapStart).TotalHours);
                });
            var availableHours = Math.Max(
                0,
                (end - start).TotalDays * (BookingRules.ClosingTime - BookingRules.OpeningTime).TotalHours -
                maintenanceHours);
            var utilization = availableHours == 0
                ? 0
                : Math.Clamp(bookedHours / availableHours * 100, 0, 100);

            return new RoomUtilizationItem(
                room.Id,
                room.Nev,
                Math.Round(bookedHours, 2),
                Math.Round(availableHours, 2),
                Math.Round(utilization, 2));
        }).ToList();

        var totalBookedHours = roomReports.Sum(room => room.FoglaltOra);
        var totalAvailableHours = roomReports.Sum(room => room.RendelkezesreAlloOra);
        var totalUtilization = totalAvailableHours == 0
            ? 0
            : Math.Clamp(totalBookedHours / totalAvailableHours * 100, 0, 100);

        return Results.Ok(new RoomUtilizationReportResponse(
            startDay,
            endDay,
            roomReports,
            Math.Round(totalBookedHours, 2),
            Math.Round(totalAvailableHours, 2),
            Math.Round(totalUtilization, 2)));
    }

    private static async Task<IResult> GetKeyTrafficAsync(
        string? kezdet,
        string? veg,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryResolveRange(kezdet, veg, out var startDay, out var endDay, out var error))
        {
            return Results.BadRequest(new { message = error });
        }

        var start = ToStart(startDay);
        var end = ToStart(endDay.AddDays(1));
        var keys = await db.Kulcsok
            .AsNoTracking()
            .OrderBy(key => key.Szam)
            .Select(key => new { key.Id, key.Szam, key.TeremId, TeremNev = key.Terem.Nev })
            .ToListAsync(cancellationToken);

        var movements = await db.Kulcsmozgasok
            .AsNoTracking()
            .Where(movement => movement.Idopont >= start && movement.Idopont < end)
            .Select(movement => new { movement.KulcsId, movement.MozgasTipus })
            .ToListAsync(cancellationToken);

        var items = keys.Select(key =>
        {
            var keyMovements = movements.Where(movement => movement.KulcsId == key.Id).ToList();
            return new KeyTrafficItem(
                key.Id,
                key.Szam,
                key.TeremId,
                key.TeremNev,
                keyMovements.Count,
                keyMovements.Count(movement => IsIssue(movement.MozgasTipus)),
                keyMovements.Count(movement => IsReturn(movement.MozgasTipus)));
        }).ToList();

        return Results.Ok(new KeyTrafficReportResponse(
            startDay,
            endDay,
            items.Sum(item => item.OsszesMozgas),
            items.Sum(item => item.Kiadasok),
            items.Sum(item => item.Visszavetelok),
            items));
    }

    private static async Task<IResult> GetReliabilityAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var startYear = now.Month >= 9 ? now.Year : now.Year - 1;
        var academicYear = $"{startYear}/{startYear + 1}";
        var events = await db.MegbizhatosagiEsemenyek
            .AsNoTracking()
            .Where(item => item.Tanev == academicYear && item.Felhasznalo.Role == InstructorRole)
            .OrderBy(item => item.FelhasznaloId)
            .ThenBy(item => item.EsemenyIdopontja)
            .ThenBy(item => item.Id)
            .Select(item => new { item.FelhasznaloId, item.EsemenyTipus, item.Pont })
            .ToListAsync(cancellationToken);

        var userScores = events
            .GroupBy(item => item.FelhasznaloId)
            .Select(userEvents => userEvents.Aggregate(
                0,
                (score, item) => Math.Clamp(score + item.Pont, 0, 100)))
            .ToList();
        var eventTypes = events
            .GroupBy(item => item.EsemenyTipus)
            .OrderBy(group => group.Key)
            .Select(group => new ReliabilityEventTypeSummary(
                group.Key,
                group.Count(),
                group.Sum(item => item.Pont)))
            .ToList();

        return Results.Ok(new ReliabilityReportResponse(
            academicYear,
            events.Count,
            userScores.Count,
            userScores.Count == 0 ? 0 : Math.Round(userScores.Average(), 2),
            eventTypes));
    }

    private static async Task<TrafficSummary> GetTrafficAsync(
        DateTime start,
        DateTime end,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var movements = await db.Kulcsmozgasok
            .AsNoTracking()
            .Where(movement => movement.Idopont >= start && movement.Idopont < end)
            .Select(movement => new { movement.Kulcs.TeremId, movement.MozgasTipus })
            .ToListAsync(cancellationToken);
        var bookings = await db.Foglalasok
            .AsNoTracking()
            .Where(booking => booking.StartTime >= start && booking.StartTime < end)
            .Select(booking => new { booking.TeremId, booking.Status })
            .ToListAsync(cancellationToken);
        var lateBookings = await db.MegbizhatosagiEsemenyek
            .AsNoTracking()
            .CountAsync(
                item => item.EsemenyIdopontja >= start &&
                        item.EsemenyIdopontja < end &&
                        item.EsemenyTipus == LateReturnEvent,
                cancellationToken);
        var rooms = await db.Termek
            .AsNoTracking()
            .OrderBy(room => room.Nev)
            .Select(room => new { room.Id, room.Nev })
            .ToListAsync(cancellationToken);

        return new TrafficSummary(
            movements.Count(movement => IsIssue(movement.MozgasTipus)),
            movements.Count(movement => IsReturn(movement.MozgasTipus)),
            lateBookings,
            bookings.Count(booking => booking.Status == "NoShow"),
            rooms.Select(room => new RoomTrafficItem(
                room.Id,
                room.Nev,
                bookings.Count(booking => booking.TeremId == room.Id),
                movements.Count(movement =>
                    movement.TeremId == room.Id && IsIssue(movement.MozgasTipus)))).ToList());
    }

    private static bool TryResolveRange(
        string? startText,
        string? endText,
        out DateOnly start,
        out DateOnly end,
        out string error)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        start = today.AddDays(1 - today.Day);
        end = today;
        error = string.Empty;

        if (startText is null && endText is null)
        {
            return true;
        }

        if (!TryParseDate(startText, out start) || !TryParseDate(endText, out end))
        {
            error = "A kezdet és veg paramétert együtt, YYYY-MM-DD formátumban kell megadni.";
            return false;
        }

        if (end < start)
        {
            error = "A veg dátum nem lehet korábbi a kezdet dátumnál.";
            return false;
        }

        if (end == DateOnly.MaxValue)
        {
            error = "A veg dátum nem lehet 9999-12-31, mert az utána következő nap már nem ábrázolható.";
            return false;
        }

        if (end.DayNumber - start.DayNumber + 1 > MaximumRangeDays)
        {
            error = $"A riport legfeljebb {MaximumRangeDays} napos időszakra kérhető le.";
            return false;
        }

        return true;
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static DateTime ToStart(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static bool IsIssue(string movementType) =>
        movementType == IssueMovement || movementType == LegacyIssueMovement;

    private static bool IsReturn(string movementType) =>
        movementType == ReturnMovement || movementType == LegacyReturnMovement;

    private static IResult InvalidDate(string message) =>
        Results.BadRequest(new { message });

    private sealed record TrafficSummary(
        int Issues,
        int Returns,
        int LateBookings,
        int NoShows,
        List<RoomTrafficItem> Rooms);

    public sealed record DailyReportResponse(
        DateOnly Datum,
        int OsszesKulcskiadas,
        int OsszesKulcsvisszavetel,
        int OsszesKesedelmesFoglalas,
        int OsszesNoShow,
        List<RoomTrafficItem> TermenkintiForgalom);

    public sealed record WeeklyReportResponse(
        DateOnly HetKezdete,
        DateOnly HetVege,
        int OsszesKulcskiadas,
        int OsszesKulcsvisszavetel,
        int OsszesKesedelmesFoglalas,
        int OsszesNoShow,
        List<RoomTrafficItem> TermenkintiForgalom);

    public sealed record RoomTrafficItem(
        int TeremId,
        string TeremNev,
        int Foglalasok,
        int Kulcskiadasok);

    public sealed record RoomUtilizationReportResponse(
        DateOnly Kezdet,
        DateOnly Veg,
        List<RoomUtilizationItem> Termek,
        double OsszesFoglaltOra,
        double OsszesRendelkezesreAlloOra,
        double KihasznaltsagSzazalek);

    public sealed record RoomUtilizationItem(
        int TeremId,
        string TeremNev,
        double FoglaltOra,
        double RendelkezesreAlloOra,
        double KihasznaltsagSzazalek);

    public sealed record KeyTrafficReportResponse(
        DateOnly Kezdet,
        DateOnly Veg,
        int OsszesMozgas,
        int OsszesKiadas,
        int OsszesVisszavetel,
        List<KeyTrafficItem> Kulcsok);

    public sealed record KeyTrafficItem(
        int KulcsId,
        string KulcsSzam,
        int TeremId,
        string TeremNev,
        int OsszesMozgas,
        int Kiadasok,
        int Visszavetelok);

    public sealed record ReliabilityReportResponse(
        string Tanev,
        int EsemenyekSzama,
        int ErintettOktatokSzama,
        double AtlagosPontszam,
        List<ReliabilityEventTypeSummary> Esemenytipusok);

    public sealed record ReliabilityEventTypeSummary(
        string EsemenyTipus,
        int EsemenyekSzama,
        int OsszesPont);
}
