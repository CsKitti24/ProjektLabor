using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Security;
using BCryptHasher = BCrypt.Net.BCrypt;
using Xunit;

namespace backend.Tests;

public sealed class SecurityIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityIntegrationTests(WebApplicationFactory<Program> factory)
    {
        TestJwtConfiguration.ConfigureEnvironment();

        var dbName = $"SecurityTestDb_{Guid.NewGuid():N}";
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Jwt:Issuer"] = TestJwtConfiguration.Issuer,
                    ["Authentication:Jwt:Audience"] = TestJwtConfiguration.Audience,
                    ["Authentication:Jwt:SigningKey"] = TestJwtConfiguration.SigningKey
                });
            });
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    service => service.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });
    }

    [Fact]
    public async Task CanonicalRoles_AreAcceptedForTheirExistingPermissions()
    {
        await SeedUsersAndMasterKeyAsync();

        var roleAccess = new[]
        {
            (UserId: 1, Role: RoleNames.Oktato, ExpectedStatus: System.Net.HttpStatusCode.OK),
            (UserId: 2, Role: RoleNames.Portas, ExpectedStatus: System.Net.HttpStatusCode.Forbidden),
            (UserId: 3, Role: RoleNames.Adminisztrator, ExpectedStatus: System.Net.HttpStatusCode.OK),
            (UserId: 4, Role: RoleNames.UzemeltetesiIgazgato, ExpectedStatus: System.Net.HttpStatusCode.OK)
        };

        foreach (var (userId, role, expectedStatus) in roleAccess)
        {
            using var client = CreateAuthenticatedClient(userId, role);
            var response = await client.GetAsync("/api/reliability/users/1");
            Assert.Equal(expectedStatus, response.StatusCode);
        }

        using var instructorClient = CreateAuthenticatedClient(1, RoleNames.Oktato);
        var otherInstructorResponse = await instructorClient.GetAsync("/api/reliability/users/5");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, otherInstructorResponse.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_RejectMissingAndInvalidJwt_AndMaintenanceRequiresAuthentication()
    {
        using var anonymousClient = _factory.CreateClient();
        var missingToken = await anonymousClient.GetAsync("/api/maintenance");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, missingToken.StatusCode);

        using var invalidTokenClient = _factory.CreateClient();
        invalidTokenClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-jwt");
        var invalidToken = await invalidTokenClient.GetAsync("/api/maintenance");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, invalidToken.StatusCode);

        await SeedUsersAndMasterKeyAsync();
        foreach (var (userId, role) in AllRoles())
        {
            using var client = CreateAuthenticatedClient(userId, role);
            var response = await client.GetAsync("/api/maintenance");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task MaintenanceWrites_AreAdministratorOnly()
    {
        await SeedUsersAndMasterKeyAsync();
        var request = new
        {
            teremId = 1,
            startTime = new DateTime(2027, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            endTime = new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc),
            leiras = "Teszt karbantartás"
        };

        using var instructor = CreateAuthenticatedClient(1, RoleNames.Oktato);
        var denied = await instructor.PostAsJsonAsync("/api/maintenance", request);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);

        using var porter = CreateAuthenticatedClient(2, RoleNames.Portas);
        denied = await porter.PostAsJsonAsync("/api/maintenance", request);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);

        using var director = CreateAuthenticatedClient(4, RoleNames.UzemeltetesiIgazgato);
        denied = await director.PostAsJsonAsync("/api/maintenance", request);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);

        using var administrator = CreateAuthenticatedClient(3, RoleNames.Adminisztrator);
        var created = await administrator.PostAsJsonAsync("/api/maintenance", request);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task MasterAccess_IsAdministratorOnly_AndSupportsGrantListAndRevoke()
    {
        await SeedUsersAndMasterKeyAsync();

        foreach (var (userId, role) in AllRoles().Where(user => user.Role != RoleNames.Adminisztrator))
        {
            using var client = CreateAuthenticatedClient(userId, role);
            var listResponse = await client.GetAsync("/api/keys/10/master-access");
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, listResponse.StatusCode);

            var grantResponse = await client.PostAsJsonAsync(
                "/api/keys/10/master-access",
                new { userId = 1 });
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, grantResponse.StatusCode);

            var revokeResponse = await client.DeleteAsync("/api/keys/10/master-access/1");
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, revokeResponse.StatusCode);
        }

        using var administrator = CreateAuthenticatedClient(3, RoleNames.Adminisztrator);
        var granted = await administrator.PostAsJsonAsync(
            "/api/keys/10/master-access",
            new { userId = 1 });
        Assert.Equal(System.Net.HttpStatusCode.Created, granted.StatusCode);

        var listed = await administrator.GetFromJsonAsync<List<MasterAccessTestResponse>>(
            "/api/keys/10/master-access");
        Assert.NotNull(listed);
        Assert.Single(listed);
        Assert.Equal(1, listed[0].UserId);

        var revoked = await administrator.DeleteAsync("/api/keys/10/master-access/1");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, revoked.StatusCode);
    }

    [Fact]
    public async Task AdminBootstrap_CreatesOneBcryptAdmin_AndIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BootstrapTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var password = "Strong-bootstrap-password-2026";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = "first.admin",
                ["BootstrapAdmin:Password"] = password
            })
            .Build();

        await AdminBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);
        await AdminBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);

        var admin = await db.Felhasznalok.SingleAsync();
        Assert.Equal("first.admin", admin.Username);
        Assert.Equal(RoleNames.Adminisztrator, admin.Role);
        Assert.NotEqual(password, admin.JelszoHash);
        Assert.True(BCryptHasher.Verify(password, admin.JelszoHash));
    }

    [Fact]
    public async Task AdminBootstrap_RejectsMissingOrInvalidConfigurationForAnEmptyDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BootstrapInvalidTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var missingConfiguration = new ConfigurationBuilder().Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminBootstrapper.InitializeAsync(db, missingConfiguration, NullLogger.Instance));
        Assert.Contains("empty database requires", exception.Message);

        var invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = "admin",
                ["BootstrapAdmin:Password"] = "short"
            })
            .Build();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminBootstrapper.InitializeAsync(db, invalidConfiguration, NullLogger.Instance));
    }

    [Fact]
    public void JwtConfiguration_UsesDevelopmentOnlyValues_AndRejectsMissingOrShortSigningKeys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionConfiguration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repositoryRoot, "backend", "appsettings.json"))
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(
                productionConfiguration,
                CreateEnvironment(Environments.Production)));

        var developmentConfiguration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repositoryRoot, "backend", "appsettings.json"))
            .AddJsonFile(Path.Combine(repositoryRoot, "backend", "appsettings.Development.json"))
            .Build();
        var developmentSettings = JwtSettings.FromConfiguration(
            developmentConfiguration,
            CreateEnvironment(Environments.Development));
        Assert.Contains("development-only", developmentSettings.Issuer);
        Assert.True(Encoding.UTF8.GetByteCount(developmentSettings.SigningKey) >= 32);

        var developmentKeyInProduction = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = TestJwtConfiguration.Issuer,
                ["Authentication:Jwt:Audience"] = TestJwtConfiguration.Audience,
                ["Authentication:Jwt:SigningKey"] =
                    "development-only-signing-key-not-for-production-32bytes"
            })
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(
                developmentKeyInProduction,
                CreateEnvironment(Environments.Production)));

        var shortKeyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = TestJwtConfiguration.Issuer,
                ["Authentication:Jwt:Audience"] = TestJwtConfiguration.Audience,
                ["Authentication:Jwt:SigningKey"] = "too-short"
            })
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(
                shortKeyConfiguration,
                CreateEnvironment(Environments.Production)));
    }

    private async Task SeedUsersAndMasterKeyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Felhasznalok.AddRange(
            CreateUser(1, RoleNames.Oktato),
            CreateUser(2, RoleNames.Portas),
            CreateUser(3, RoleNames.Adminisztrator),
            CreateUser(4, RoleNames.UzemeltetesiIgazgato),
            CreateUser(5, RoleNames.Oktato));
        db.Termek.Add(new Terem
        {
            Id = 1,
            Nev = "Tesztterem",
            Kapacitas = 20,
            Leiras = string.Empty
        });
        db.Kulcsok.Add(new Kulcs
        {
            Id = 10,
            Szam = "MK-10",
            Tipus = "Mesterkulcs",
            TeremId = 1
        });
        await db.SaveChangesAsync();
    }

    private HttpClient CreateAuthenticatedClient(int userId, string role)
    {
        var client = _factory.CreateClient();
        var token = new JwtSecurityToken(
            issuer: TestJwtConfiguration.Issuer,
            audience: TestJwtConfiguration.Audience,
            claims: new[]
            {
                new Claim("sub", userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim("role", role)
            },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtConfiguration.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private static IEnumerable<(int UserId, string Role)> AllRoles() =>
    [
        (1, RoleNames.Oktato),
        (2, RoleNames.Portas),
        (3, RoleNames.Adminisztrator),
        (4, RoleNames.UzemeltetesiIgazgato)
    ];

    private static Felhasznalo CreateUser(int id, string role) => new()
    {
        Id = id,
        Username = $"user{id}",
        Email = $"user{id}@example.test",
        JelszoHash = "not-used",
        Nev = $"Teszt Felhasználó {id}",
        Role = role
    };

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "appsettings.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static IHostEnvironment CreateEnvironment(string environmentName) =>
        new TestHostEnvironment(environmentName);

    private sealed record MasterAccessTestResponse(int UserId, string Nev, DateTime CreatedAt);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
