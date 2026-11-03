using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;

namespace ProjektLabor.Services;

internal static class AutomationParameterReader
{
    public const string NoShowLimitKey = "no_show_idokorlat_perc";
    public const string ClosingTimeKey = "napi_kulcsleadasi_zarasi_idopont";

    public static async Task<int> GetNoShowLimitMinutesAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var value = await db.Rendszerparameterek
            .AsNoTracking()
            .Where(parameter => parameter.Kulcs == NoShowLimitKey)
            .Select(parameter => parameter.Ertek)
            .SingleOrDefaultAsync(cancellationToken);

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            minutes > 0)
        {
            return minutes;
        }

        logger.LogWarning(
            "System parameter {ParameterKey} is missing or invalid; using default value {DefaultValue}.",
            NoShowLimitKey,
            15);
        return 15;
    }

    public static async Task<TimeOnly> GetClosingTimeAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var value = await db.Rendszerparameterek
            .AsNoTracking()
            .Where(parameter => parameter.Kulcs == ClosingTimeKey)
            .Select(parameter => parameter.Ertek)
            .SingleOrDefaultAsync(cancellationToken);

        if (TimeOnly.TryParseExact(
                value,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var closingTime))
        {
            return closingTime;
        }

        logger.LogWarning(
            "System parameter {ParameterKey} is missing or invalid; using default value {DefaultValue}.",
            ClosingTimeKey,
            "22:00");
        return new TimeOnly(22, 0);
    }

    public static TimeZoneInfo GetTimeZone(IConfiguration configuration, ILogger logger)
    {
        var configuredId = configuration["Automation:TimeZoneId"];
        if (string.IsNullOrWhiteSpace(configuredId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(configuredId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            logger.LogWarning(
                exception,
                "Configured automation time zone {TimeZoneId} was not found; using UTC.",
                configuredId);
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException exception)
        {
            logger.LogWarning(
                exception,
                "Configured automation time zone {TimeZoneId} is invalid; using UTC.",
                configuredId);
            return TimeZoneInfo.Utc;
        }
    }
}
