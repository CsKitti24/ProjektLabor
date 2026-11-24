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

                services.RemoveAppDbContextConfiguration();

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
            startTime = new DateTime(2027, 1, 1, 8, 0, 0, DateTimeKind.Unspecified),
            endTime = new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Unspecified),
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
            (Path: "/api/tickets?page=1&pageSize=20", AllowedRoles: new[] { RoleNames.Oktato, RoleNames.Portas, RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/users?page=1&pageSize=20", AllowedRoles: new[] { RoleNames.Adminisztrator }),
            (Path: "/api/parameters", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/audit", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/reports/daily?datum=2027-01-01", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
            (Path: "/api/keys/10/master-access", AllowedRoles: new[] { RoleNames.Adminisztrator }),
            (Path: "/api/reliability/users", AllowedRoles: new[] { RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato }),
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
            kategoria = "Egyéb észrevétel",
            leiras = "Teszt hibajegy"
        };

        foreach (var (userId, role) in AllRoles().Where(user =>
                     user.Role == RoleNames.UzemeltetesiIgazgato))
        {
            using var deniedClient = CreateAuthenticatedClient(userId, role);
            var denied = await deniedClient.PostAsJsonAsync("/api/tickets", ticketRequest);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var instructorReporter = CreateAuthenticatedClient(1, RoleNames.Oktato);
        var instructorTicketResponse = await instructorReporter.PostAsJsonAsync("/api/tickets", ticketRequest);
        Assert.Equal(System.Net.HttpStatusCode.Created, instructorTicketResponse.StatusCode);
        var instructorTicket = await instructorTicketResponse.Content.ReadFromJsonAsync<TicketTestResponse>();
        Assert.NotNull(instructorTicket);
        Assert.Equal(1, instructorTicket.FelhasznaloId);

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
                // Az 5-ös felhasználó egy második oktató: foglalás csak oktató nevére szólhat.
                felhasznaloId = 5,
                startTime = new DateTime(2027, 1, 3, 8, 0, 0, DateTimeKind.Unspecified).AddDays(userId),
                endTime = new DateTime(2027, 1, 3, 8, 45, 0, DateTimeKind.Unspecified).AddDays(userId)
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
                Assert.Equal(5, booking.FelhasznaloId);
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
    public async Task AdminBootstrap_DeactivatesSampleAccountsWithKnownPasswords_UnlessExplicitlyEnabled()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BootstrapSampleTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var knownHashes = AdminBootstrapper.KnownSampleAccountPasswordHashes;
        db.Felhasznalok.AddRange(
            new Felhasznalo { Username = "admin", Email = "admin@projektlabor.local", Nev = "Admin", Role = RoleNames.Adminisztrator, JelszoHash = knownHashes[0] },
            new Felhasznalo { Username = "oktato", Email = "oktato@projektlabor.local", Nev = "Oktató", Role = RoleNames.Oktato, JelszoHash = knownHashes[1] },
            new Felhasznalo { Username = "changed", Email = "changed@projektlabor.local", Nev = "Changed", Role = RoleNames.Oktato, JelszoHash = BCryptHasher.HashPassword("Changed-password-2026", 4) });
        await db.SaveChangesAsync();

        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SampleAccounts:Enabled"] = "true" })
            .Build();
        await AdminBootstrapper.InitializeAsync(db, enabled, NullLogger.Instance);
        Assert.All(await db.Felhasznalok.ToListAsync(), user => Assert.True(user.Aktiv));

        var password = "Strong-bootstrap-password-2026";
        var production = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = "admin",
                ["BootstrapAdmin:Password"] = password
            })
            .Build();
        await AdminBootstrapper.InitializeAsync(db, production, NullLogger.Instance);

        var users = await db.Felhasznalok.ToDictionaryAsync(user => user.Username);
        Assert.False(users["oktato"].Aktiv);
        Assert.True(users["changed"].Aktiv);
        // A letiltott minta-adminisztrátort a megadott, erős jelszóval vesszük át.
        Assert.True(users["admin"].Aktiv);
        Assert.Equal(RoleNames.Adminisztrator, users["admin"].Role);
        Assert.True(BCryptHasher.Verify(password, users["admin"].JelszoHash));
        Assert.DoesNotContain(users["admin"].JelszoHash, knownHashes);
    }

    [Fact]
    public async Task AdminBootstrap_RejectsPlaceholderCredentials()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BootstrapPlaceholderTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var placeholder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Username"] = "REPLACE_WITH_BOOTSTRAP_ADMIN",
                ["BootstrapAdmin:Password"] = "REPLACE_WITH_A_BOOTSTRAP_PASSWORD_12+"
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminBootstrapper.InitializeAsync(db, placeholder, NullLogger.Instance));
        Assert.False(await db.Felhasznalok.AnyAsync());
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
