namespace ProjektLabor.Services;

/// <summary>
/// Egységes időkezelés: az adatbázisban és a szerveroldali logikában minden időpont UTC.
/// A megjelölés nélküli (<see cref="DateTimeKind.Unspecified"/>) időpontokat az intézmény
/// helyi idejének (alapértelmezés: Europe/Budapest) tekintjük, és UTC-re alakítjuk.
/// </summary>
public static class AppTime
{
    public const string DefaultTimeZoneId = "Europe/Budapest";

    private static TimeZoneInfo _zone = ResolveZone(DefaultTimeZoneId) ?? TimeZoneInfo.Utc;

    public static TimeZoneInfo Zone => _zone;

    /// <summary>
    /// Beállítja az intézmény időzónáját. Ismeretlen azonosító esetén az alapértelmezett marad.
    /// </summary>
    public static bool Configure(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return true;
        }

        var zone = ResolveZone(timeZoneId);
        if (zone is null)
        {
            return false;
        }

        _zone = zone;
        return true;
    }

    /// <summary>
    /// UTC-re alakít: UTC változatlan, Local a gép idejéből, Unspecified az intézmény helyi idejéből.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => ConvertLocalToUtc(value)
    };

    public static DateTime? ToUtc(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;

    /// <summary>
    /// Az intézmény helyi faliórájának megfelelő időpont (Unspecified kind).
    /// </summary>
    public static DateTime ToLocal(DateTime value) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(ToUtc(value), _zone), DateTimeKind.Unspecified);

    /// <summary>
    /// Egy helyi nap adott helyi időpontja UTC-ben.
    /// </summary>
    public static DateTime LocalToUtc(DateOnly date, TimeOnly time) =>
        ConvertLocalToUtc(date.ToDateTime(time, DateTimeKind.Unspecified));

    public static DateOnly LocalDate(DateTime value) => DateOnly.FromDateTime(ToLocal(value));

    private static DateTime ConvertLocalToUtc(DateTime localValue)
    {
        var unspecified = DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);
        if (_zone.IsInvalidTime(unspecified))
        {
            // Tavaszi óraátállításkor kimaradó óra: a következő érvényes időpontra léptetünk.
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, _zone);
    }

    private static TimeZoneInfo? ResolveZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
