using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Models;
using ProjektLabor.Security;
using ProjektLabor.Services;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace backend.Tests;

public sealed class SqlServerIntegrationTests
{
    [Fact]
    public async Task DedicatedSqlServerDatabase_AppliesMigrationsAndEnforcesRelationships()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set SQLSERVER_TEST_CONNECTION to a dedicated, disposable SQL Server database before running this test.");
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

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var roleConfiguration = RoleNames.All.ToDictionary(
            role => role,
            role => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)));
        var settings = new Dictionary<string, string?>
        {
            ["BootstrapDemoUsers:Enabled"] = "true"
        };
        foreach (var role in RoleNames.All)
        {
            var username = $"sql_{role.ToLowerInvariant()}_{suffix}";
            settings[$"BootstrapDemoUsers:{role}:Username"] = username;
            settings[$"BootstrapDemoUsers:{role}:Password"] = roleConfiguration[role];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        await DemoUserBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);
        await DemoUserBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);

        var users = await db.Felhasznalok
            .Where(user => user.Username.EndsWith(suffix))
            .ToListAsync();
        Assert.Equal(RoleNames.All.Length, users.Count);

        var jwtSettings = new JwtSettings(
            "sql-server-integration-tests",
            "sql-server-integration-tests-api",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        var authService = new AuthService(db, configuration, jwtSettings, NullLogger<AuthService>.Instance);
        foreach (var role in RoleNames.All)
        {
            var user = Assert.Single(users, account => account.Role == role);
            var password = roleConfiguration[role];
            Assert.True(BCryptHasher.Verify(password, user.JelszoHash));
            Assert.NotEqual(password, user.JelszoHash);

            var login = await authService.LoginAsync(new LoginRequestDto
            {
                Username = user.Username,
                Jelszo = password
            });
            Assert.NotNull(login);
            Assert.Equal(role, login.Felhasznalo.Role);
            Assert.DoesNotContain(password, System.Text.Json.JsonSerializer.Serialize(login), StringComparison.Ordinal);
            Assert.DoesNotContain("JelszoHash", System.Text.Json.JsonSerializer.Serialize(login), StringComparison.Ordinal);
        }
    }
}
