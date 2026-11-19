namespace ProjektLabor.Services;

internal static class KeyReturnDeadlineCalculator
{
    /// <summary>
    /// A kulcs visszahozatali határideje: a kiadás (helyi) napjának zárási időpontja, vagy ha a kiadás
    /// a zárás után történt, a következő napé. Ugyanezt használja a napi zárás (büntetőpont, esti
    /// értesítés) és a lejárt kulcsok listája, így a kettő mindig egyezik.
    /// </summary>
    public static DateTime GetDeadlineUtc(
        DateTime issuedAtUtc,
        TimeOnly closingTime,
        TimeZoneInfo timeZone)
    {
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
