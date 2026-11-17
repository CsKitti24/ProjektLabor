namespace backend.Tests;

/// <summary>
/// A tesztekben a foglalási időpontokat az intézmény helyi faliórája szerint adjuk meg
/// (megjelölés nélküli DateTime), ahogy a frontend is helyi időt kezel.
/// </summary>
internal static class TestTime
{
    public static DateTime LocalToday =>
        DateTime.SpecifyKind(ProjektLabor.Services.AppTime.ToLocal(DateTime.UtcNow).Date, DateTimeKind.Unspecified);
}
