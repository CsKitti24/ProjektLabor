using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace backend.Tests;

public sealed class SqlServerIntegrationTests
{
    [Fact(Skip = "Csak külön SQLSERVER_TEST_CONNECTION tesztadatbázissal futtatható.")]
    public async Task DedicatedSqlServerDatabase_AppliesMigrationsAndEnforcesRelationships()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        Assert.True(await db.Database.CanConnectAsync());
        Assert.NotEmpty(db.Model.FindEntityType(typeof(Foglalas))!.GetForeignKeys());

        await using var transaction = await db.Database.BeginTransactionAsync();
        var room = new Terem { Nev = $"SQL test {Guid.NewGuid():N}", Kapacitas = 1 };
        db.Termek.Add(room);
        await db.SaveChangesAsync();
        await transaction.RollbackAsync();
    }
}
