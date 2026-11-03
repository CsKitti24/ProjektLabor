using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Security;
using ProjektLabor.Services;
using Xunit;

namespace backend.Tests;

public sealed class DemoUserSeedTests
{
    [Fact]
    public async Task Seed_IsIdempotent_AndStoresBCryptHashes()
    {
        await using var db = CreateContext();
        var configuration = CreateConfiguration();

        await AdminBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);
        await AdminBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);

        var users = await db.Felhasznalok.OrderBy(user => user.Username).ToListAsync();

        Assert.Equal(5, users.Count);
        Assert.Equal(5, users.Select(user => user.Username).Distinct().Count());
        Assert.All(users, user => Assert.StartsWith("$2", user.JelszoHash));
        Assert.Equal(
            new[] { "Adminisztrator", "Oktato", "Portas", "UzemeltetesiIgazgato" },
            users.Select(user => user.Role).Distinct().OrderBy(role => role));
    }

    [Fact]
    public async Task DemoUserPasswords_CreateTokensWithExpectedRoles()
    {
        await using var db = CreateContext();
        var configuration = CreateConfiguration();
        await AdminBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);

        var authConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = "test-issuer",
                ["Authentication:Jwt:Audience"] = "test-audience",
                ["Authentication:Jwt:SigningKey"] = "test-signing-key-with-at-least-32-bytes"
            })
            .Build();
        var authService = new AuthService(
            db,
            authConfiguration,
            new JwtSettings("test-issuer", "test-audience", "test-signing-key-with-at-least-32-bytes"),
            NullLogger<AuthService>.Instance);

        foreach (var (username, password, role) in DemoUsers())
        {
            var result = await authService.LoginAsync(new LoginRequestDto
            {
                Username = username,
                Jelszo = password
            });

            Assert.NotNull(result);
            Assert.Equal(role, result.Felhasznalo.Role);
            var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
            Assert.Contains(token.Claims, claim => claim.Type == "role" && claim.Value == role);
        }
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DemoUserSeed_{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = "bootstrap-admin",
                ["BootstrapAdmin:Password"] = "BootstrapPassword2026!",
                ["DemoUsers:Oktato:Password"] = "DemoOktato2026!",
                ["DemoUsers:Portas:Password"] = "DemoPortas2026!",
                ["DemoUsers:Adminisztrator:Password"] = "DemoAdmin2026!",
                ["DemoUsers:UzemeltetesiIgazgato:Password"] = "DemoIgazgato2026!"
            })
            .Build();
    }

    private static IEnumerable<(string Username, string Password, string Role)> DemoUsers() =>
    [
        ("demo-oktato", "DemoOktato2026!", "Oktato"),
        ("demo-portas", "DemoPortas2026!", "Portas"),
        ("demo-admin", "DemoAdmin2026!", "Adminisztrator"),
        ("demo-igazgato", "DemoIgazgato2026!", "UzemeltetesiIgazgato")
    ];
}
