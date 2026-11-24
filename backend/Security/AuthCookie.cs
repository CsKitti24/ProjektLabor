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
        // Az nginx proxy X-Forwarded-Proto fejléce alapján HTTPS-en Secure. Éles üzemben, HTTPS-t lezáró
        // proxy mögött az Authentication:Cookie:SecureOnly=true mindig Secure sütit ad (http-n nem küldi a böngésző).
        Secure = request.IsHttps ||
                 request.HttpContext.RequestServices.GetService<IConfiguration>()?
                     .GetValue("Authentication:Cookie:SecureOnly", false) == true,
        // Más oldalról indított kérésekhez a böngésző nem küldi el (CSRF elleni védelem).
        SameSite = SameSiteMode.Strict,
        Path = "/api",
        Expires = expires
    };
}
