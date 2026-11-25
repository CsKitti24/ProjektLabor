namespace backend.Tests;

/// <summary>
/// A tesztekben a foglalási időpontokat az intézmény helyi faliórája szerint adjuk meg
/// (megjelölés nélküli DateTime), ahogy a frontend is helyi időt kezel.
/// </summary>
internal static class TestTime
{
    public static DateTime LocalToday =>
        DateTime.SpecifyKind(ProjektLabor.Services.AppTime.ToLocal(DateTime.UtcNow).Date, DateTimeKind.Unspecified);

    /// <summary>
    /// A mától számított n-edik munkanap (hétfő–péntek) helyi éjfélkor. Hétvégére nem lehet foglalni,
    /// ezért a foglalási tesztek ezt használják; különböző n-ekhez mindig különböző nap tartozik.
    /// </summary>
    public static DateTime LocalWorkday(int n)
    {
        var day = LocalToday;
        for (var found = 0; found < n;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                found++;
            }
        }

        return day;
    }
}
