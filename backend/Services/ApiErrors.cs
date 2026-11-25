namespace ProjektLabor.Services;

/// <summary>
/// Egységes hibaválaszok magyar üzenettel (üres 403/404 helyett), hogy a felület meg tudja mondani,
/// miért nem sikerült a művelet.
/// </summary>
public static class ApiErrors
{
    public const string AdminOnly = "Ehhez a művelethez adminisztrátori jogosultság szükséges.";

    public static IResult Forbidden(string message) =>
        Results.Json(new { message }, statusCode: StatusCodes.Status403Forbidden);

    public static IResult NotFound(string message) =>
        Results.NotFound(new { message });
}
