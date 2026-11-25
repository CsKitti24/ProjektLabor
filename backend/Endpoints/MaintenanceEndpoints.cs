using System.Security.Claims;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Security;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints;

public static class MaintenanceEndpoints
{
    private static readonly Expression<Func<KarbantartasiIdosav, MaintenanceResponse>> ResponseProjection =
        item => new(
            item.Id,
            item.TeremId,
            item.Terem.Nev,
            item.FelhasznaloId,
            item.Felhasznalo.Nev,
            item.StartTime,
            item.EndTime,
            item.Leiras,
            item.CreatedAt);

    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var maintenance = endpoints.MapGroup("/api/maintenance")
            .RequireAuthorization();

        maintenance.MapGet("/", GetAllAsync)
            .WithName("GetMaintenance");
        maintenance.MapGet("/{id:int}", GetByIdAsync)
            .WithName("GetMaintenanceById");
        maintenance.MapPost("/", CreateAsync)
            .RequireAuthorization(RolePolicies.Administrator)
            .WithName("CreateMaintenance");
        maintenance.MapPut("/{id:int}", UpdateAsync)
            .RequireAuthorization(RolePolicies.Administrator)
            .WithName("UpdateMaintenance");
        maintenance.MapDelete("/{id:int}", DeleteAsync)
            .RequireAuthorization(RolePolicies.Administrator)
            .WithName("DeleteMaintenance");

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(
        [FromServices] AppDbContext db,
        [FromQuery] int? teremId,
        [FromQuery] DateTime? startTime,
        [FromQuery] DateTime? endTime,
        CancellationToken cancellationToken)
    {
        if (teremId is <= 0 || (startTime.HasValue && endTime.HasValue && endTime <= startTime))
        {
            return Results.BadRequest(new { message = "Érvénytelen teremazonosító vagy időintervallum." });
        }

        var query = db.KarbantartasiIdosavok.AsNoTracking().AsQueryable();

        if (teremId.HasValue)
        {
            query = query.Where(item => item.TeremId == teremId.Value);
        }

        if (startTime.HasValue)
        {
            var startUtc = AppTime.ToUtc(startTime.Value);
            query = query.Where(item => item.EndTime > startUtc);
        }

        if (endTime.HasValue)
        {
            var endUtc = AppTime.ToUtc(endTime.Value);
            query = query.Where(item => item.StartTime < endUtc);
        }

        var items = await query
            .OrderBy(item => item.TeremId)
            .ThenBy(item => item.StartTime)
            .Select(ResponseProjection)
            .ToListAsync(cancellationToken);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetByIdAsync(
        int id,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await db.KarbantartasiIdosavok
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(ResponseProjection)
            .SingleOrDefaultAsync(cancellationToken);

        return item is null ? ApiErrors.NotFound("A karbantartás nem található.") : Results.Ok(item);
    }

    private static async Task<IResult> CreateAsync(
        MaintenanceRequest request,
        ClaimsPrincipal user,
        [FromServices] AppDbContext db,
        [FromServices] AuditService auditService,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var administratorId = await GetAdministratorIdAsync(user, db, cancellationToken);
        if (administratorId is null)
        {
            return ApiErrors.Forbidden(ApiErrors.AdminOnly);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var validationError = ValidateRequest(request, nowUtc);
        if (validationError is not null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        request = request with
        {
            StartTime = AppTime.ToUtc(request.StartTime),
            EndTime = AppTime.ToUtc(request.EndTime)
        };

        var targetRoom = await db.Termek
            .AsNoTracking()
            .SingleOrDefaultAsync(room => room.Id == request.TeremId, cancellationToken);
        if (targetRoom is null)
        {
            return Results.NotFound(new { message = "A megadott terem nem található." });
        }

        if (!targetRoom.Aktivalva)
        {
            return Results.BadRequest(new { message = "Deaktivált teremre nem lehet karbantartást rögzíteni." });
        }

        return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
        {
        await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? null
            : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await SqlServerLocking.AcquireAsync(db, $"room-booking:{request.TeremId}", cancellationToken);

        if (await HasOverlappingMaintenanceAsync(db, request, null, cancellationToken))
        {
            return OverlappingMaintenance();
        }

        var conflicts = await GetBookingConflictsAsync(db, request, cancellationToken);
        var remainingConflicts = conflicts
            .Where(conflict => request.LemondandoFoglalasIds?.Contains(conflict.FoglalasId) != true)
            .ToList();
        if (remainingConflicts.Count > 0)
        {
            return BookingConflict(remainingConflicts);
        }

        // A kifejezetten lemondásra jelölt ütköző foglalásokat ugyanebben a tranzakcióban mondjuk le:
        // ha a karbantartás nem jön létre, a foglalások is érintetlenek maradnak.
        var cancelledBookings = await CancelConflictingBookingsAsync(
            db, conflicts, targetRoom.Nev, nowUtc, cancellationToken);

        var item = new KarbantartasiIdosav
        {
            FelhasznaloId = administratorId.Value,
            TeremId = request.TeremId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Leiras = string.IsNullOrWhiteSpace(request.Leiras) ? null : request.Leiras.Trim()
        };

        db.KarbantartasiIdosavok.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        await AuditCancellationsAsync(auditService, user, cancelledBookings, targetRoom.Nev);
        await auditService.LogAsync(user, "CREATE", "KarbantartasiIdosav", item.Id.ToString(),
            ujErtek: $"TeremId: {item.TeremId}; Start: {item.StartTime:O}; End: {item.EndTime:O}");
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var response = await FindResponseAsync(db, item.Id, cancellationToken);
        return Results.Created($"/api/maintenance/{item.Id}", response);
        });
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        MaintenanceRequest request,
        ClaimsPrincipal user,
        [FromServices] AppDbContext db,
        [FromServices] AuditService auditService,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await GetAdministratorIdAsync(user, db, cancellationToken) is null)
        {
            return ApiErrors.Forbidden(ApiErrors.AdminOnly);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var validationError = ValidateRequest(request, nowUtc);
        if (validationError is not null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        request = request with
        {
            StartTime = AppTime.ToUtc(request.StartTime),
            EndTime = AppTime.ToUtc(request.EndTime)
        };

        var item = await db.KarbantartasiIdosavok.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (item is null)
        {
            return ApiErrors.NotFound("A karbantartás nem található.");
        }

        if (item.EndTime <= nowUtc)
        {
            return Results.Conflict(new { message = "Lezárult karbantartás nem módosítható (az előzmény megmarad)." });
        }

        var targetRoom = await db.Termek
            .AsNoTracking()
            .SingleOrDefaultAsync(room => room.Id == request.TeremId, cancellationToken);
        if (targetRoom is null)
        {
            return Results.NotFound(new { message = "A megadott terem nem található." });
        }

        if (!targetRoom.Aktivalva)
        {
            return Results.BadRequest(new { message = "Deaktivált teremre nem lehet karbantartást rögzíteni." });
        }

        return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
        {
        await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? null
            : await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        foreach (var roomId in new[] { item.TeremId, request.TeremId }.Distinct().Order())
        {
            await SqlServerLocking.AcquireAsync(db, $"room-booking:{roomId}", cancellationToken);
        }

        if (await HasOverlappingMaintenanceAsync(db, request, id, cancellationToken))
        {
            return OverlappingMaintenance();
        }

        var conflicts = await GetBookingConflictsAsync(db, request, cancellationToken);
        var remainingConflicts = conflicts
            .Where(conflict => request.LemondandoFoglalasIds?.Contains(conflict.FoglalasId) != true)
            .ToList();
        if (remainingConflicts.Count > 0)
        {
            return BookingConflict(remainingConflicts);
        }

        var cancelledBookings = await CancelConflictingBookingsAsync(
            db, conflicts, targetRoom.Nev, nowUtc, cancellationToken);

        item.TeremId = request.TeremId;
        item.StartTime = request.StartTime;
        item.EndTime = request.EndTime;
        item.Leiras = string.IsNullOrWhiteSpace(request.Leiras) ? null : request.Leiras.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await AuditCancellationsAsync(auditService, user, cancelledBookings, targetRoom.Nev);
        await auditService.LogAsync(user, "UPDATE", "KarbantartasiIdosav", item.Id.ToString(),
            ujErtek: $"TeremId: {item.TeremId}; Start: {item.StartTime:O}; End: {item.EndTime:O}");
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Results.Ok(await FindResponseAsync(db, item.Id, cancellationToken));
        });
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        [FromServices] AppDbContext db,
        [FromServices] AuditService auditService,
        [FromServices] TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await GetAdministratorIdAsync(user, db, cancellationToken) is null)
        {
            return ApiErrors.Forbidden(ApiErrors.AdminOnly);
        }

        var item = await db.KarbantartasiIdosavok.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (item is null)
        {
            return ApiErrors.NotFound("A karbantartás nem található.");
        }

        if (item.EndTime <= timeProvider.GetUtcNow().UtcDateTime)
        {
            return Results.Conflict(new { message = "Lezárult karbantartás nem törölhető (az előzmény megmarad)." });
        }

        return await SqlServerLocking.ExecuteWithRetryAsync<IResult>(db, async () =>
        {
        await using var transaction = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? null
            : await db.Database.BeginTransactionAsync(cancellationToken);
        db.KarbantartasiIdosavok.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        await auditService.LogAsync(user, "DELETE", "KarbantartasiIdosav", id.ToString());
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Results.NoContent();
        });
    }

    private static async Task<int?> GetAdministratorIdAsync(
        ClaimsPrincipal user,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!user.IsInRole(RoleNames.Adminisztrator))
        {
            return null;
        }

        var subject = user.FindFirstValue("sub");
        if (!int.TryParse(subject, out var userId))
        {
            return null;
        }

        return await db.Felhasznalok
            .Where(account => account.Id == userId && account.Role == RoleNames.Adminisztrator)
            .Select(account => (int?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private const int MaxMaintenanceDays = 90;
    private const int MaxDescriptionLength = 500;

    private static string? ValidateRequest(MaintenanceRequest request, DateTime nowUtc)
    {
        if (request.TeremId <= 0)
        {
            return "A teremazonosítónak pozitívnak kell lennie.";
        }

        if (request.StartTime == default || request.EndTime == default ||
            request.EndTime <= request.StartTime)
        {
            return "A záró időpontnak későbbinek kell lennie a kezdő időpontnál.";
        }

        var startUtc = AppTime.ToUtc(request.StartTime);
        var endUtc = AppTime.ToUtc(request.EndTime);
        if (endUtc <= nowUtc)
        {
            return "Már lezárult időszakra nem rögzíthető karbantartás.";
        }

        if (endUtc - startUtc > TimeSpan.FromDays(MaxMaintenanceDays))
        {
            return $"Egy karbantartási idősáv legfeljebb {MaxMaintenanceDays} napos lehet.";
        }

        if (request.Leiras?.Trim().Length > MaxDescriptionLength)
        {
            return $"A leírás legfeljebb {MaxDescriptionLength} karakter lehet.";
        }

        return null;
    }

    // A karbantartás miatt lemondandó (a kérésben kifejezetten megjelölt) ütköző foglalások lemondása,
    // értesítéssel a foglaló oktatónak. A mentés a hívó tranzakciójában történik.
    private static async Task<List<Foglalas>> CancelConflictingBookingsAsync(
        AppDbContext db,
        IReadOnlyList<BookingConflictResponse> conflicts,
        string roomName,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (conflicts.Count == 0)
        {
            return [];
        }

        var ids = conflicts.Select(conflict => conflict.FoglalasId).ToList();
        var bookings = await db.Foglalasok
            .Where(booking => ids.Contains(booking.Id))
            .ToListAsync(cancellationToken);
        foreach (var booking in bookings)
        {
            booking.Status = BookingRules.CancelledStatus;
            booking.UpdatedAt = nowUtc;
            BookingNotifications.AddCancelled(
                db,
                booking,
                $"A(z) {roomName} terem foglalását karbantartás miatt lemondtuk ({AppTime.ToLocal(booking.StartTime):yyyy.MM.dd. HH:mm}).",
                nowUtc);
        }

        return bookings;
    }

    private static async Task AuditCancellationsAsync(
        AuditService auditService,
        ClaimsPrincipal user,
        IEnumerable<Foglalas> bookings,
        string roomName)
    {
        foreach (var booking in bookings)
        {
            await auditService.LogAsync(
                user,
                muveletTipus: "CANCEL",
                entitasTipus: "Foglalas",
                entitasId: booking.Id.ToString(),
                ujErtek: $"Status: {BookingRules.CancelledStatus}",
                indoklas: $"A(z) {roomName} terem karbantartása miatt lemondva.");
        }
    }

    // Ugyanarra a teremre egymást átfedő karbantartási idősávok nem rögzíthetők.
    private static Task<bool> HasOverlappingMaintenanceAsync(
        AppDbContext db,
        MaintenanceRequest request,
        int? ownId,
        CancellationToken cancellationToken) =>
        db.KarbantartasiIdosavok.AnyAsync(
            item =>
                item.TeremId == request.TeremId &&
                item.Id != ownId &&
                request.StartTime < item.EndTime &&
                request.EndTime > item.StartTime,
            cancellationToken);

    private static IResult OverlappingMaintenance() =>
        Results.Conflict(new { message = "A megadott idősáv átfed egy már rögzített karbantartással ugyanebben a teremben." });

    private static Task<List<BookingConflictResponse>> GetBookingConflictsAsync(
        AppDbContext db,
        MaintenanceRequest request,
        CancellationToken cancellationToken) =>
        db.Foglalasok
            .AsNoTracking()
            .Where(BookingRules.ActiveBookings)
            .Where(booking =>
                booking.TeremId == request.TeremId &&
                booking.StartTime < request.EndTime &&
                booking.EndTime > request.StartTime)
            .OrderBy(booking => booking.StartTime)
            .Select(booking => new BookingConflictResponse(
                booking.Id,
                booking.FelhasznaloId,
                booking.Felhasznalo.Nev,
                booking.TeremId,
                booking.Terem.Nev,
                booking.StartTime,
                booking.EndTime,
                booking.Status))
            .ToListAsync(cancellationToken);

    private static IResult BookingConflict(IReadOnlyList<BookingConflictResponse> conflicts) =>
        Results.Conflict(new
        {
            message = "A karbantartási idősáv ütközik az alábbi foglalásokkal. A foglalásokat törölni vagy áthelyezni kell.",
            conflicts
        });

    private static Task<MaintenanceResponse?> FindResponseAsync(
        AppDbContext db,
        int id,
        CancellationToken cancellationToken) =>
        db.KarbantartasiIdosavok
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(ResponseProjection)
            .SingleOrDefaultAsync(cancellationToken);

    public sealed record MaintenanceRequest(
        int TeremId,
        DateTime StartTime,
        DateTime EndTime,
        string? Leiras,
        // Az ütköző foglalások közül ezeket a karbantartással együtt (atomikusan) le kell mondani.
        IReadOnlyList<int>? LemondandoFoglalasIds = null);

    public sealed record MaintenanceResponse(
        int Id,
        int TeremId,
        string TeremNev,
        int FelhasznaloId,
        string FelhasznaloNev,
        DateTime StartTime,
        DateTime EndTime,
        string? Leiras,
        DateTime CreatedAt);

    public sealed record BookingConflictResponse(
        int FoglalasId,
        int FelhasznaloId,
        string FelhasznaloNev,
        int TeremId,
        string TeremNev,
        DateTime StartTime,
        DateTime EndTime,
        string Status);
}
