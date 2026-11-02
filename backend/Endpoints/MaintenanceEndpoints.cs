using System.Security.Claims;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Endpoints;

public static class MaintenanceEndpoints
{
    private const string AdministratorRole = "Adminisztrátor";
    private const string CancelledBookingStatus = "Megszuntetve";
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
        var maintenance = endpoints.MapGroup("/api/maintenance");

        maintenance.MapGet("/", GetAllAsync)
            .WithName("GetMaintenance");
        maintenance.MapGet("/{id:int}", GetByIdAsync)
            .WithName("GetMaintenanceById");
        maintenance.MapPost("/", CreateAsync)
            .RequireAuthorization()
            .WithName("CreateMaintenance");
        maintenance.MapPut("/{id:int}", UpdateAsync)
            .RequireAuthorization()
            .WithName("UpdateMaintenance");
        maintenance.MapDelete("/{id:int}", DeleteAsync)
            .RequireAuthorization()
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
            query = query.Where(item => item.EndTime > startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(item => item.StartTime < endTime.Value);
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

        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<IResult> CreateAsync(
        MaintenanceRequest request,
        ClaimsPrincipal user,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var administratorId = await GetAdministratorIdAsync(user, db, cancellationToken);
        if (administratorId is null)
        {
            return Results.Forbid();
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        if (!await db.Termek.AnyAsync(room => room.Id == request.TeremId, cancellationToken))
        {
            return Results.NotFound(new { message = "A megadott terem nem található." });
        }

        if (await HasBookingConflictAsync(db, request, cancellationToken))
        {
            return BookingConflict();
        }

        var item = new KarbantartasiIdosav
        {
            FelhasznaloId = administratorId.Value,
            TeremId = request.TeremId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Leiras = request.Leiras
        };

        db.KarbantartasiIdosavok.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        var response = await FindResponseAsync(db, item.Id, cancellationToken);
        return Results.Created($"/api/maintenance/{item.Id}", response);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        MaintenanceRequest request,
        ClaimsPrincipal user,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await GetAdministratorIdAsync(user, db, cancellationToken) is null)
        {
            return Results.Forbid();
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        var item = await db.KarbantartasiIdosavok.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        if (!await db.Termek.AnyAsync(room => room.Id == request.TeremId, cancellationToken))
        {
            return Results.NotFound(new { message = "A megadott terem nem található." });
        }

        if (await HasBookingConflictAsync(db, request, cancellationToken))
        {
            return BookingConflict();
        }

        item.TeremId = request.TeremId;
        item.StartTime = request.StartTime;
        item.EndTime = request.EndTime;
        item.Leiras = request.Leiras;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await FindResponseAsync(db, item.Id, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await GetAdministratorIdAsync(user, db, cancellationToken) is null)
        {
            return Results.Forbid();
        }

        var item = await db.KarbantartasiIdosavok.SingleOrDefaultAsync(
            item => item.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        db.KarbantartasiIdosavok.Remove(item);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<int?> GetAdministratorIdAsync(
        ClaimsPrincipal user,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!user.IsInRole(AdministratorRole))
        {
            return null;
        }

        var subject = user.FindFirstValue("sub");
        if (!int.TryParse(subject, out var userId))
        {
            return null;
        }

        return await db.Felhasznalok
            .Where(account => account.Id == userId && account.Role == AdministratorRole)
            .Select(account => (int?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string? ValidateRequest(MaintenanceRequest request)
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

        return null;
    }

    private static Task<bool> HasBookingConflictAsync(
        AppDbContext db,
        MaintenanceRequest request,
        CancellationToken cancellationToken) =>
        db.Foglalasok.AnyAsync(
            booking => booking.TeremId == request.TeremId &&
                       booking.Status != CancelledBookingStatus &&
                       booking.StartTime < request.EndTime &&
                       booking.EndTime > request.StartTime,
            cancellationToken);

    private static IResult BookingConflict() =>
        Results.Conflict(new { message = "A karbantartási idősáv ütközik egy teremfoglalással." });

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
        string? Leiras);

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
}
