using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;

namespace ProjektLabor.Endpoints;

public static class NotificationEndpoints
{
    private const string PendingState = "KuldesAlatt";

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications");

        notifications.MapGet("/my", GetMyNotificationsAsync)
            .RequireAuthorization("Oktato")
            .WithName("GetMyNotifications");

        notifications.MapGet("/pending", GetPendingNotificationsAsync)
            .RequireAuthorization("AdminOrPortas")
            .WithName("GetPendingNotifications");

        return endpoints;
    }

    private static async Task<IResult> GetMyNotificationsAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var sub = principal.FindFirstValue("sub") ??
                  principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) ||
            userId <= 0)
        {
            return Results.Unauthorized();
        }

        var userExists = await db.Felhasznalok
            .AnyAsync(user => user.Id == userId && user.Role == "Oktato", cancellationToken);
        if (!userExists)
        {
            return Results.Forbid();
        }

        var items = await db.Ertesitesek
            .AsNoTracking()
            .Where(notification => notification.Foglalas.FelhasznaloId == userId)
            .OrderByDescending(notification => notification.IdoPont)
            .ThenByDescending(notification => notification.Id)
            .Select(notification => new NotificationResponse(
                notification.Id,
                notification.FoglalasId,
                notification.ErtesitesTipus,
                notification.IdoPont,
                notification.Allapot,
                notification.CreatedAt,
                notification.Foglalas.StartTime,
                notification.Foglalas.EndTime))
            .ToListAsync(cancellationToken);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetPendingNotificationsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var items = await db.Ertesitesek
            .AsNoTracking()
            .Where(notification => notification.Allapot == PendingState)
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Select(notification => new NotificationResponse(
                notification.Id,
                notification.FoglalasId,
                notification.ErtesitesTipus,
                notification.IdoPont,
                notification.Allapot,
                notification.CreatedAt,
                notification.Foglalas.StartTime,
                notification.Foglalas.EndTime))
            .ToListAsync(cancellationToken);

        return Results.Ok(items);
    }

    public sealed record NotificationResponse(
        int Id,
        int FoglalasId,
        string ErtesitesTipus,
        DateTime IdoPont,
        string Allapot,
        DateTime CreatedAt,
        DateTime FoglalasKezdete,
        DateTime FoglalasVege);
}
