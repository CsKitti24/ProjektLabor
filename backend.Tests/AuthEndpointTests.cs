using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Models;
using ProjektLabor.Security;
using Xunit;

namespace backend.Tests
{
    public class AuthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _dbName;

        public AuthEndpointTests(WebApplicationFactory<Program> factory)
        {
            _dbName = "AuthTestDb_" + Guid.NewGuid();
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.RemoveAppDbContextConfiguration();

                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(_dbName);
                    });
                });
            });
        }

        private async Task SeedUserAsync(string username, string rawPassword, string role = "Oktato")
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existing = await db.Felhasznalok.FirstOrDefaultAsync(u => u.Username == username);
            if (existing == null)
            {
                var user = new Felhasznalo
                {
                    Username = username,
                    Nev = "Teszt Elek",
                    Email = $"{username}@test.hu",
                    Role = role,
                    JelszoHash = BCrypt.Net.BCrypt.HashPassword(rawPassword),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Felhasznalok.Add(user);
                await db.SaveChangesAsync();
            }
        }

        // A token csak a HttpOnly sütiben érkezik (a JSON-válaszban nincs).
        private static string ReadAuthCookie(HttpResponseMessage response)
        {
            Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
            var cookie = cookies.Single(value => value.StartsWith(AuthCookie.Name + "=", StringComparison.Ordinal));
            var token = cookie[(AuthCookie.Name.Length + 1)..].Split(';')[0];
            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            return token;
        }

        [Fact]
        public async Task Login_WithValidCredentials_ReturnsOkAndToken()
        {
            // Arrange
            await SeedUserAsync("oktato1", "TitkosJelszo123");
            var client = _factory.CreateClient();

            // Act
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "oktato1",
                Jelszo = "TitkosJelszo123"
            });

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.NotNull(result);
            var token = ReadAuthCookie(response);
            Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal("oktato1", result.Felhasznalo.Username);
            Assert.Equal("Oktato", result.Felhasznalo.Role);
        }

        [Fact]
        public async Task Login_SucceedsForEveryCanonicalRole_WithoutReturningPassword()
        {
            var client = _factory.CreateClient();
            foreach (var role in RoleNames.All)
            {
                var username = $"login_{role}";
                var password = $"Strong-{role}-password-2026";
                await SeedUserAsync(username, password, role);

                var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
                {
                    Username = username,
                    Jelszo = password
                });

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
                Assert.NotNull(result);
                Assert.Equal(role, result.Felhasznalo.Role);
                ReadAuthCookie(response);

                var responseBody = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(password, responseBody, StringComparison.Ordinal);
                Assert.DoesNotContain("JelszoHash", responseBody, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
        {
            // Arrange
            await SeedUserAsync("oktato2", "HelyesJelszo");
            var client = _factory.CreateClient();

            // Act
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "oktato2",
                Jelszo = "RosszJelszo"
            });

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Login_WithNonExistentUser_ReturnsUnauthorized()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "nemletezo_user",
                Jelszo = "Valami123"
            });

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AuthenticationAttempts_AreAuditedWithoutRecordingCredentialsOrTokens()
        {
            const string username = "audit_login";
            const string password = "Audit-test-password-2026";
            await SeedUserAsync(username, password);
            var client = _factory.CreateClient();

            var failedLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = username,
                Jelszo = "incorrect-password"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);

            var successfulLogin = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = username,
                Jelszo = password
            });
            Assert.Equal(HttpStatusCode.OK, successfulLogin.StatusCode);
            var loginToken = ReadAuthCookie(successfulLogin);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", loginToken);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auditEntries = await db.AuditLogok
                .Where(entry => entry.EntitasTipus == "Authentication")
                .OrderBy(entry => entry.Id)
                .ToListAsync();
            Assert.Collection(
                auditEntries,
                failed => Assert.Equal("LOGIN_FAILED", failed.MuveletTipus),
                succeeded => Assert.Equal("LOGIN_SUCCESS", succeeded.MuveletTipus),
                logout => Assert.Equal("LOGOUT", logout.MuveletTipus));
            Assert.All(auditEntries, entry =>
            {
                Assert.DoesNotContain(password, entry.RegiErtek ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(password, entry.UjErtek ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(loginToken, entry.RegiErtek ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(loginToken, entry.UjErtek ?? string.Empty, StringComparison.Ordinal);
            });
            Assert.Null(auditEntries[0].FelhasznaloId);
            Assert.NotNull(auditEntries[1].FelhasznaloId);
            Assert.Equal(auditEntries[1].FelhasznaloId, auditEntries[2].FelhasznaloId);
        }

        [Fact]
        public async Task Login_WithEmptyCredentials_ReturnsBadRequest()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "",
                Jelszo = ""
            });

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task GetCurrentUser_WithValidToken_ReturnsCurrentUser()
        {
            // Arrange
            await SeedUserAsync("portas1", "PortasJelszo!", "Portas");
            var client = _factory.CreateClient();

            var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "portas1",
                Jelszo = "PortasJelszo!"
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ReadAuthCookie(loginResponse));

            // Act
            var meResponse = await client.GetAsync("/api/auth/me");

            // Assert
            Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
            var meBody = await meResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Jelszo", meBody, StringComparison.OrdinalIgnoreCase);
            var meResult = await meResponse.Content.ReadFromJsonAsync<CurrentUserDto>();
            Assert.NotNull(meResult);
            Assert.Equal("portas1", meResult.Username);
            Assert.Equal("Portas", meResult.Role);
        }

        [Fact]
        public async Task GetCurrentUser_WithoutToken_ReturnsUnauthorized()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/auth/me");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Logout_WithValidToken_ReturnsOk()
        {
            // Arrange
            await SeedUserAsync("admin1", "AdminJelszo!", "Adminisztrator");
            var client = _factory.CreateClient();

            var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            {
                Username = "admin1",
                Jelszo = "AdminJelszo!"
            });
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ReadAuthCookie(loginResponse));

            // Act
            var logoutResponse = await client.PostAsync("/api/auth/logout", null);

            // Assert
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        }

        [Fact]
        public async Task Logout_WithoutToken_ReturnsUnauthorized()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.PostAsync("/api/auth/logout", null);

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    
        [Fact]
        public async Task ChangePassword_RequiresCurrentPassword_AndReplacesTheOldOne()
        {
            await SeedUserAsync("jelszocsere", "RegiJelszo12345");
            var client = _factory.CreateClient();

            var anonymous = await client.PostAsJsonAsync("/api/auth/change-password",
                new ChangePasswordRequestDto { JelenlegiJelszo = "RegiJelszo12345", UjJelszo = "UjJelszo1234567" });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            var oldToken = ReadAuthCookie(await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequestDto { Username = "jelszocsere", Jelszo = "RegiJelszo12345" }));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);

            var wrongCurrent = await client.PostAsJsonAsync("/api/auth/change-password",
                new ChangePasswordRequestDto { JelenlegiJelszo = "RosszJelszo12345", UjJelszo = "UjJelszo1234567" });
            Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

            var tooShort = await client.PostAsJsonAsync("/api/auth/change-password",
                new ChangePasswordRequestDto { JelenlegiJelszo = "RegiJelszo12345", UjJelszo = "rovid" });
            Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

            var changed = await client.PostAsJsonAsync("/api/auth/change-password",
                new ChangePasswordRequestDto { JelenlegiJelszo = "RegiJelszo12345", UjJelszo = "UjJelszo1234567" });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var newToken = ReadAuthCookie(changed);

            // A jelszócsere előtt kiadott token (pl. egy másik gépen) már nem érvényes, az új igen.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

            var anon = _factory.CreateClient();
            var oldLogin = await anon.PostAsJsonAsync("/api/auth/login",
                new LoginRequestDto { Username = "jelszocsere", Jelszo = "RegiJelszo12345" });
            Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
            var newLogin = await anon.PostAsJsonAsync("/api/auth/login",
                new LoginRequestDto { Username = "jelszocsere", Jelszo = "UjJelszo1234567" });
            Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditLogok.AnyAsync(entry => entry.MuveletTipus == "PASSWORD_CHANGE"));
            Assert.True(await db.AuditLogok.AnyAsync(entry => entry.MuveletTipus == "PASSWORD_CHANGE_FAILED"));
        }

        [Fact]
        public async Task Login_WithPublicSamplePassword_IsRejectedUnlessSampleAccountsAreEnabled()
        {
            const string samplePassword = "Oktatojelszo1!";
            var sampleHash = AdminBootstrapper.KnownSampleAccountPasswordHashes
                .Single(hash => BCrypt.Net.BCrypt.Verify(samplePassword, hash));
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Felhasznalok.Add(new Felhasznalo
                {
                    Username = "minta_oktato",
                    Nev = "Minta Oktató",
                    Email = "minta_oktato@test.hu",
                    Role = RoleNames.Oktato,
                    JelszoHash = sampleHash
                });
                await db.SaveChangesAsync();
            }

            var production = _factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["SampleAccounts:Enabled"] = "false"
                    })));
            var rejected = await production.CreateClient().PostAsJsonAsync("/api/auth/login",
                new LoginRequestDto { Username = "minta_oktato", Jelszo = samplePassword });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.False(rejected.Headers.Contains("Set-Cookie"));

            // Fejlesztői környezetben (SampleAccounts:Enabled=true) a mintafiók használható.
            var allowed = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
                new LoginRequestDto { Username = "minta_oktato", Jelszo = samplePassword });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
    }
}
