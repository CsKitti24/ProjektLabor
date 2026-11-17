using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Security;

/// <summary>
/// Naplózza a megtagadott hozzáféréseket (401/403) és az elutasított módosító kéréseket (4xx).
/// - A naplózás hibája soha nem rontja el a kliensnek adott választ (csak a szervernaplóba kerül).
/// - Azonosítatlan (anonim) kérésekből kliens-IP-nként percenként legfeljebb
///   <see cref="AnonymousEntriesPerMinute"/> bejegyzés kerül az audit táblába, így azt nem lehet teleírni.
/// </summary>
public sealed class AccessAuditMiddleware
{
    public const int AnonymousEntriesPerMinute = 20;

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AccessAuditMiddleware> _logger;

    public AccessAuditMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<AccessAuditMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        await _next(context);

        var statusCode = context.Response.StatusCode;
        var accessDenied = statusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;
        var method = context.Request.Method;
        var isMutation =
            HttpMethods.IsPost(method) ||
            HttpMethods.IsPut(method) ||
            HttpMethods.IsPatch(method) ||
            HttpMethods.IsDelete(method);
        var rejectedMutation = isMutation &&
            statusCode is >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError;
        if ((!accessDenied && !rejectedMutation) ||
            context.Request.Path.StartsWithSegments("/api/auth/login"))
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            int? userId = null;
            var subject = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst("sub")?.Value
                : null;
            if (int.TryParse(subject, out var parsedUserId) &&
                parsedUserId > 0 &&
                await db.Felhasznalok.AnyAsync(user => user.Id == parsedUserId))
            {
                userId = parsedUserId;
            }

            if (userId is null && !TryReserveAnonymousEntry(context))
            {
                return;
            }

            var path = context.Request.Path.Value ?? "/";
            db.AuditLogok.Add(new AuditLog
            {
                FelhasznaloId = userId,
                MuveletTipus = accessDenied
                    ? statusCode == StatusCodes.Status401Unauthorized
                        ? "ACCESS_UNAUTHENTICATED"
                        : "ACCESS_FORBIDDEN"
                    : "OPERATION_REJECTED",
                EntitasTipus = "API",
                EntitasId = path.Length > 50 ? path[..50] : path,
                Indoklas = $"{context.Request.Method} request returned {statusCode}.",
                LetrehozasIdeje = DateTime.UtcNow
            });
            // Szándékosan nem a kérés megszakítási tokenjét használjuk: a naplózás a kliens
            // kapcsolatbontása után is fusson le.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to write access audit entry for {Method} {Path} ({StatusCode}).",
                context.Request.Method,
                context.Request.Path,
                statusCode);
        }
    }

    private bool TryReserveAnonymousEntry(HttpContext context)
    {
        var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var cacheKey = $"anonymous-audit:{clientKey}:{window}";
        var counter = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return new StrongBox<int>(0);
        })!;

        var count = Interlocked.Increment(ref counter.Value);
        if (count == AnonymousEntriesPerMinute + 1)
        {
            _logger.LogWarning(
                "Anonymous access audit limit reached for client {Client}; further entries are skipped this minute.",
                clientKey);
        }

        return count <= AnonymousEntriesPerMinute;
    }
}
