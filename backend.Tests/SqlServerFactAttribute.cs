namespace backend.Tests;

/// <summary>
/// SQL Server-integrációs teszt: kihagyásra kerül, ha nincs megadva a
/// SQLSERVER_TEST_CONNECTION környezeti változó, így adatbázis nélkül nem bukik el.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "SQLSERVER_TEST_CONNECTION";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Az SQL Server-integrációs tesztekhez állítsd be a {ConnectionVariable} változót.";
        }
    }
}
