using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Models;
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
            Assert.False(string.IsNullOrWhiteSpace(result.Token));
            Assert.Equal("oktato1", result.Felhasznalo.Username);
            Assert.Equal("Oktato", result.Felhasznalo.Role);
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
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.NotNull(loginResult);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", loginResult.Token);

            // Act
            var meResponse = await client.GetAsync("/api/auth/me");

            // Assert
            Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
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
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.NotNull(loginResult);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", loginResult.Token);

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
    }
}
