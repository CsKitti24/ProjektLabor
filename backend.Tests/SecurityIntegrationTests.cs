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
            (UserId: 1, Role: RoleNames.Oktato, ExpectedStatus: System.Net.HttpStatusCode.Forbidden),
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
    public async Task ProtectedEndpoints_RejectExpiredJwt()
    {
        using var expiredClient = CreateAuthenticatedClient(
            1,
            RoleNames.Adminisztrator,
            DateTime.UtcNow.AddMinutes(-2));

        var response = await expiredClient.GetAsync("/api/maintenance");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JwtWithoutUserSubject_IsRejectedAsUnauthorized()
    {
        var token = new JwtSecurityToken(
            issuer: TestJwtConfiguration.Issuer,
            audience: TestJwtConfiguration.Audience,
            claims: [new Claim("role", RoleNames.Adminisztrator)],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtConfiguration.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.GetAsync("/api/maintenance");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
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
    public async Task ImportantReadEndpoints_EnforceTheDocumentedRoleMatrix()
    {
        await SeedUsersAndMasterKeyAsync();

        var endpoints = new[]
        {
            (Path: "/api/rooms", AllowedRoles: RoleNames.All),
            (Path: "/api/keys", AllowedRoles: new[] { RoleNames.Portas, RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/rooms/1/keys", AllowedRoles: new[] { RoleNames.Portas, RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/key-movements?page=1&pageSize=20", AllowedRoles: new[] { RoleNames.Portas, RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/bookings?page=1&pageSize=20", AllowedRoles: new[] { RoleNames.Portas, RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/tickets?page=1&pageSize=20", AllowedRoles: new[] { RoleNames.Portas, RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/users?page=1&pageSize=20", AllowedRoles: new[] { RoleNames.Adminisztrator }),
            (Path: "/api/parameters", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/audit", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/reports/daily?datum=2027-01-01", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/keys/10/master-access", AllowedRoles: new[] { RoleNames.Adminisztrator }),
            (Path: "/api/reliability/users/1", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/reliability/users/1/events", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato })
        };

        foreach (var (path, allowedRoles) in endpoints)
        {
            foreach (var (userId, role) in AllRoles())
            {
                using var client = CreateAuthenticatedClient(userId, role);
                var response = await client.GetAsync(path);
                var expected = allowedRoles.Contains(role)
                    ? System.Net.HttpStatusCode.OK
                    : System.Net.HttpStatusCode.Forbidden;
                Assert.True(
                    response.StatusCode == expected,
                    $"{role} GET {path} returned {response.StatusCode}.");
            }
        }

        foreach (var (userId, role) in AllRoles())
        {
            using var client = CreateAuthenticatedClient(userId, role);
            var ownBookings = await client.GetAsync("/api/bookings/my");
            Assert.Equal(
                role == RoleNames.Oktato
                    ? System.Net.HttpStatusCode.OK
                    : System.Net.HttpStatusCode.Forbidden,
                ownBookings.StatusCode);

            var ownReliability = await client.GetAsync("/api/reliability/my");
            Assert.Equal(
                role == RoleNames.Oktato
                    ? System.Net.HttpStatusCode.OK
                    : System.Net.HttpStatusCode.Forbidden,
                ownReliability.StatusCode);
        }
    }

    [Fact]
    public async Task CreateAndStatusRoutes_EnforceRoleMatrixAndUseTokenOperatorId()
    {
        await SeedUsersAndMasterKeyAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Kulcsok.Add(new Kulcs
            {
                Id = 11,
                Szam = "K-11",
                Tipus = "Osztalykulcs",
                TeremId = 1,
                Aktivalva = true
            });
            await db.SaveChangesAsync();
        }

        var ticketRequest = new
        {
            teremId = 1,
            kategoria = "Teszt",
            leiras = "Teszt hibajegy"
        };

        foreach (var (userId, role) in AllRoles().Where(user =>
                     user.Role != RoleNames.Portas && user.Role != RoleNames.Adminisztrator))
        {
            using var deniedClient = CreateAuthenticatedClient(userId, role);
            var denied = await deniedClient.PostAsJsonAsync("/api/tickets", ticketRequest);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var porter = CreateAuthenticatedClient(2, RoleNames.Portas);
        var createdTicket = await porter.PostAsJsonAsync("/api/tickets", ticketRequest);
        Assert.Equal(System.Net.HttpStatusCode.Created, createdTicket.StatusCode);
        var ticket = await createdTicket.Content.ReadFromJsonAsync<TicketTestResponse>();
        Assert.NotNull(ticket);
        Assert.Equal(2, ticket.FelhasznaloId);

        foreach (var (userId, role) in AllRoles())
        {
            using var client = CreateAuthenticatedClient(userId, role);
            var bookingRequest = new
            {
                teremId = 1,
                felhasznaloId = 2,
                startTime = new DateTime(2027, 1, 3, 8, 0, 0, DateTimeKind.Utc).AddDays(userId),
                endTime = new DateTime(2027, 1, 3, 8, 45, 0, DateTimeKind.Utc).AddDays(userId)
            };
            var response = await client.PostAsJsonAsync("/api/bookings", bookingRequest);
            Assert.Equal(
                role is RoleNames.Oktato or RoleNames.Adminisztrator
                    ? System.Net.HttpStatusCode.Created
                    : System.Net.HttpStatusCode.Forbidden,
                response.StatusCode);
            if (role == RoleNames.Oktato)
            {
                var booking = await response.Content.ReadFromJsonAsync<BookingTestResponse>();
                Assert.NotNull(booking);
                Assert.Equal(1, booking.FelhasznaloId);
            }
            else if (role == RoleNames.Adminisztrator)
            {
                var booking = await response.Content.ReadFromJsonAsync<BookingTestResponse>();
                Assert.NotNull(booking);
                Assert.Equal(2, booking.FelhasznaloId);
            }
        }

        using var instructor = CreateAuthenticatedClient(1, RoleNames.Oktato);
        var deniedStatusChange = await instructor.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/status",
            new { statusz = "Megoldva" });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, deniedStatusChange.StatusCode);

        using var director = CreateAuthenticatedClient(4, RoleNames.UzemeltetesiIgazgato);
        deniedStatusChange = await director.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/status",
            new { statusz = "Megoldva" });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, deniedStatusChange.StatusCode);

        using var admin = CreateAuthenticatedClient(3, RoleNames.Adminisztrator);
        var updatedTicket = await admin.PutAsJsonAsync(
            $"/api/tickets/{ticket.Id}/status",
            new { statusz = "Megoldva" });
        Assert.Equal(System.Net.HttpStatusCode.OK, updatedTicket.StatusCode);

        var issue = await porter.PostAsJsonAsync(
            "/api/key-movements/issue",
            new
            {
                kulcsId = 11,
                atvevoFelhasznaloId = 1,
                pin = "1234",
                teremAllapot = "Rendben"
            });
        Assert.Equal(System.Net.HttpStatusCode.Created, issue.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var movement = await db.Kulcsmozgasok.SingleAsync();
            Assert.Equal(2, movement.OperátorId);
            Assert.Equal(1, movement.AtvevoFelhasznaloId);
        }

        foreach (var (userId, role) in AllRoles().Where(user => user.Role != RoleNames.Portas))
        {
            using var deniedClient = CreateAuthenticatedClient(userId, role);
            var deniedReturn = await deniedClient.PostAsJsonAsync(
                "/api/key-movements/return",
                new { kulcsId = 11 });
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, deniedReturn.StatusCode);
        }

        var returned = await porter.PostAsJsonAsync(
            "/api/key-movements/return",
            new { kulcsId = 11, pin = "1234", teremAllapot = "Rendben" });
        Assert.Equal(System.Net.HttpStatusCode.OK, returned.StatusCode);
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
    public async Task DemoUserBootstrap_CreatesBcryptAccountsOnce_AndNeverOverwritesExistingUsers()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DemoBootstrapTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        const string existingPassword = "Existing-account-password-2026";
        var existing = new Felhasznalo
        {
            Username = "demo.oktato",
            Email = "demo.oktato@demo.projektlabor.invalid",
            Nev = "Meglévő fiók",
            Role = RoleNames.Oktato,
            JelszoHash = BCryptHasher.HashPassword(existingPassword)
        };
        db.Felhasznalok.Add(existing);
        await db.SaveChangesAsync();

        var settings = new Dictionary<string, string?>
        {
            ["BootstrapDemoUsers:Enabled"] = "true"
        };
        foreach (var (configKey, username) in new[]
                 {
                     ("Oktato", "demo.oktato"),
                     ("Portas", "demo.portas"),
                     ("Adminisztrator", "demo.admin"),
                     ("UzemeltetesiIgazgato", "demo.igazgato")
                 })
        {
            settings[$"BootstrapDemoUsers:{configKey}:Username"] = username;
            settings[$"BootstrapDemoUsers:{configKey}:Password"] = $"Demo-{configKey}-password-2026";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        await DemoUserBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);
        await DemoUserBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);

        var accounts = await db.Felhasznalok.OrderBy(user => user.Username).ToListAsync();
        Assert.Equal(4, accounts.Count);
        Assert.Equal(RoleNames.Oktato, existing.Role);
        Assert.True(BCryptHasher.Verify(existingPassword, existing.JelszoHash));

        foreach (var (configKey, role) in new[]
                 {
                     ("Portas", RoleNames.Portas),
                     ("Adminisztrator", RoleNames.Adminisztrator),
                     ("UzemeltetesiIgazgato", RoleNames.UzemeltetesiIgazgato)
                 })
        {
            var account = Assert.Single(accounts, user => user.Role == role);
            var password = settings[$"BootstrapDemoUsers:{configKey}:Password"];
            Assert.NotNull(password);
            Assert.True(BCryptHasher.Verify(password, account.JelszoHash));
            Assert.NotEqual(password, account.JelszoHash);
        }
    }

    [Fact]
    public async Task DemoUserBootstrap_CreatesAllFourCanonicalRolesFromConfiguration()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DemoBootstrapAllRolesTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var settings = new Dictionary<string, string?>
        {
            ["BootstrapDemoUsers:Enabled"] = "true"
        };
        var expectedRoles = new[]
        {
            (ConfigKey: "Oktato", Role: RoleNames.Oktato),
            (ConfigKey: "Portas", Role: RoleNames.Portas),
            (ConfigKey: "Adminisztrator", Role: RoleNames.Adminisztrator),
            (ConfigKey: "UzemeltetesiIgazgato", Role: RoleNames.UzemeltetesiIgazgato)
        };
        foreach (var (configKey, _) in expectedRoles)
        {
            settings[$"BootstrapDemoUsers:{configKey}:Username"] = $"demo.{configKey.ToLowerInvariant()}";
            settings[$"BootstrapDemoUsers:{configKey}:Password"] = $"Demo-{configKey}-password-2026";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        await DemoUserBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);

        var accounts = await db.Felhasznalok.ToListAsync();
        Assert.Equal(4, accounts.Count);
        foreach (var (configKey, role) in expectedRoles)
        {
            var account = Assert.Single(accounts, user => user.Role == role);
            Assert.True(BCryptHasher.Verify(
                settings[$"BootstrapDemoUsers:{configKey}:Password"]!,
                account.JelszoHash));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DemoUserBootstrapper.InitializeAsync(
                db,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["BootstrapDemoUsers:Enabled"] = "true"
                    })
                    .Build(),
                NullLogger.Instance));
    }

    [Fact]
    public async Task DemoUserBootstrap_IsDisabledUnlessExplicitlyEnabled()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DemoBootstrapDisabledTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);

        await DemoUserBootstrapper.InitializeAsync(
            db,
            new ConfigurationBuilder().Build(),
            NullLogger.Instance);

        Assert.Empty(await db.Felhasznalok.ToListAsync());
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

        var previouslyCheckedInKeyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = TestJwtConfiguration.Issuer,
                ["Authentication:Jwt:Audience"] = TestJwtConfiguration.Audience,
                ["Authentication:Jwt:SigningKey"] =
                    "ProjektLaborDevSecretKey_ChangeInProduction_MinLength32Chars!"
            })
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(
                previouslyCheckedInKeyConfiguration,
                CreateEnvironment(Environments.Production)));

        var placeholderKeyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = TestJwtConfiguration.Issuer,
                ["Authentication:Jwt:Audience"] = TestJwtConfiguration.Audience,
                ["Authentication:Jwt:SigningKey"] =
                    "REPLACE_WITH_A_RANDOM_JWT_SIGNING_KEY_AT_LEAST_32_BYTES"
            })
            .Build();
        Assert.Throws<InvalidOperationException>(
            () => JwtSettings.FromConfiguration(
                placeholderKeyConfiguration,
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

    private HttpClient CreateAuthenticatedClient(int userId, string role, DateTime? expiresAt = null)
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
            expires: expiresAt ?? DateTime.UtcNow.AddMinutes(10),
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
        Role = role,
        PinHash = role == RoleNames.Oktato ? BCryptHasher.HashPassword("1234") : null
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
    private sealed record TicketTestResponse(int Id, int FelhasznaloId);
    private sealed record BookingTestResponse(int Id, int FelhasznaloId);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
