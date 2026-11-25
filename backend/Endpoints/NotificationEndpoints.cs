using ProjektLabor.Services;
using System.Globalization;
using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Security;

namespace ProjektLabor.Endpoints;

public static class NotificationEndpoints
{
    private const string PendingState = "KuldesAlatt";
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;

    // Közös leképezés. A helyiség mesterkulcsnál az a terem, amelyre a kulcsot ténylegesen kiadták
    // (Kulcsmozgas.TeremId), nem a kulcs saját terme.
    private static Expression<Func<Ertesites, NotificationResponse>> ToResponse(AppDbContext db) =>
        notification => new NotificationResponse(
            notification.Id,
            notification.FoglalasId,
            notification.ErtesitesTipus,
            notification.IdoPont,
            notification.Allapot,
            notification.CreatedAt,
            notification.Foglalas == null ? null : notification.Foglalas.StartTime,
            notification.Foglalas == null ? null : notification.Foglalas.EndTime,
            notification.Olvasva,
            notification.Kulcsmozgas == null ? null : notification.Kulcsmozgas.Kulcs.Szam,
            notification.Kulcsmozgas == null
                ? null
                : db.Termek
                    .Where(room => room.Id == (notification.Kulcsmozgas.TeremId ??
                        (notification.Kulcsmozgas.Foglalas != null
                            ? notification.Kulcsmozgas.Foglalas.TeremId
                            : notification.Kulcsmozgas.Kulcs.TeremId)))
                    .Select(room => room.Nev)
                    .FirstOrDefault(),
            notification.Hatarido,
            notification.Uzenet);

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications");

        notifications.MapGet("/my", GetMyNotificationsAsync)
            .RequireAuthorization("OktatoOrPortas")
            .WithName("GetMyNotifications");

        notifications.MapPatch("/{id:int}/read", MarkAsReadAsync)
            .RequireAuthorization("OktatoOrPortas")
            .WithName("MarkMyNotificationAsRead");

        notifications.MapGet("/pending", GetPendingNotificationsAsync)
            .RequireAuthorization("AdminOrPortas")
            .WithName("GetPendingNotifications");

        return endpoints;
    }

    private static async Task<IResult> GetMyNotificationsAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        HttpContext httpContext,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        // Lapozás: a válasz továbbra is lista, az összes elem számát az X-Total-Count fejléc adja.
        var normalizedPage = Pagination.NormalizePage(page);
        var normalizedPageSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaximumPageSize) : DefaultPageSize;

        var sub = principal.FindFirstValue("sub") ??
                  principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) ||
            userId <= 0)
        {
            return Results.Unauthorized();
        }

        var userExists = await db.Felhasznalok
            .AnyAsync(
                user => user.Id == userId &&
                        (user.Role == RoleNames.Oktato || user.Role == RoleNames.Portas),
                cancellationToken);
        if (!userExists)
        {
            return Results.Forbid();
        }

        var query = db.Ertesitesek
            .AsNoTracking()
            .Where(notification =>
                notification.CimzettFelhasznaloId == userId ||
                (notification.CimzettFelhasznaloId == null &&
                 notification.Foglalas != null &&
                 notification.Foglalas.FelhasznaloId == userId));
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(notification => notification.IdoPont)
            .ThenByDescending(notification => notification.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(ToResponse(db))
            .ToListAsync(cancellationToken);

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);

        return Results.Ok(items);
    }

    private static async Task<IResult> MarkAsReadAsync(
        int id,
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
            .AnyAsync(
                user => user.Id == userId &&
                        (user.Role == RoleNames.Oktato || user.Role == RoleNames.Portas),
                cancellationToken);
        if (!userExists)
        {
            return Results.Forbid();
        }

        var notification = await db.Ertesitesek
            .SingleOrDefaultAsync(
                item => item.Id == id &&
                        (item.CimzettFelhasznaloId == userId ||
                         (item.CimzettFelhasznaloId == null &&
                          item.Foglalas != null &&
                          item.Foglalas.FelhasznaloId == userId)),
                cancellationToken);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.Olvasva)
        {
            notification.Olvasva = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetPendingNotificationsAsync(
        AppDbContext db,
        HttpContext httpContext,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        // Lapozás, mint a /my végpontnál: a válasz lista, az összes elem száma az X-Total-Count fejlécben.
        var normalizedPage = Pagination.NormalizePage(page);
        var normalizedPageSize = pageSize is > 0 ? Math.Min(pageSize.Value, MaximumPageSize) : DefaultPageSize;

        var query = db.Ertesitesek
            .AsNoTracking()
            // Függő: még el nem olvasott, vagy (külső csatornánál) küldés alatt álló értesítés.
            .Where(notification => !notification.Olvasva || notification.Allapot == PendingState);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(ToResponse(db))
            .ToListAsync(cancellationToken);

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(items);
    }

    public sealed record NotificationResponse(
        int Id,
        int? FoglalasId,
        string ErtesitesTipus,
        DateTime IdoPont,
        string Allapot,
        DateTime CreatedAt,
        DateTime? FoglalasKezdete,
        DateTime? FoglalasVege,
        bool Olvasva,
        string? KulcsSzam,
        string? TeremNev,
        DateTime? Hatarido,
        string? Uzenet);
}
