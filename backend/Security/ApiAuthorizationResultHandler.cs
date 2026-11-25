using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace ProjektLabor.Security;

/// <summary>
/// A szerepkör-szabályok (policyk) elutasításakor is magyar nyelvű JSON-üzenetet adunk vissza
/// (üres 401/403 helyett), hogy a felület és az API-t hívó meg tudja mondani, miért nem sikerült a kérés.
/// A tényleges kihívást és tiltást (WWW-Authenticate fejléc, státuszkód) az alapértelmezett kezelő végzi.
/// </summary>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);

        if (context.Response.HasStarted)
        {
            return;
        }

        if (authorizeResult.Challenged)
        {
            await context.Response.WriteAsJsonAsync(new
            {
                message = "A művelethez bejelentkezés szükséges, vagy a munkamenet lejárt."
            });
        }
        else if (authorizeResult.Forbidden)
        {
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Ehhez a művelethez nincs jogosultságod a jelenlegi szerepköröddel."
            });
        }
    }
}
