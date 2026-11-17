namespace ProjektLabor.Security;

/// <summary>
/// A böngészős bejelentkezés tokenje HttpOnly sütiben utazik, így JavaScriptből (pl. egy esetleges
/// XSS-hibán keresztül) nem olvasható ki. Az Authorization: Bearer fejléc továbbra is működik
/// (Swagger, tesztek, más API-kliensek).
/// </summary>
public static class AuthCookie
{
    public const string Name = "projektlabor_auth";

    public static CookieOptions CreateOptions(HttpRequest request, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        // Az nginx proxy X-Forwarded-Proto fejléce alapján: HTTPS-en Secure, helyi http-n nem.
        Secure = request.IsHttps,
        // Más oldalról indított kérésekhez a böngésző nem küldi el (CSRF elleni védelem).
        SameSite = SameSiteMode.Strict,
        Path = "/api",
        Expires = expires
    };
}
