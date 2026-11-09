namespace ProjektLabor.Services;

internal static class KeyReturnDeadlineCalculator
{
    public static DateTime GetDeadlineUtc(
        DateTime issuedAtUtc,
        DateTime? bookingEndUtc,
        TimeOnly closingTime,
        TimeZoneInfo timeZone)
    {
        if (bookingEndUtc.HasValue)
        {
            return DateTime.SpecifyKind(bookingEndUtc.Value, DateTimeKind.Utc);
        }

        var localIssuedAt = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(issuedAtUtc, DateTimeKind.Utc),
            timeZone);
        var deadlineDate = DateOnly.FromDateTime(localIssuedAt);
        if (TimeOnly.FromDateTime(localIssuedAt) > closingTime)
        {
            deadlineDate = deadlineDate.AddDays(1);
        }

        var localDeadline = deadlineDate.ToDateTime(closingTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localDeadline, timeZone);
    }
}
