using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Users;
using ProjektLabor.Models;
using ProjektLabor.Services;
using Xunit;

namespace backend.Tests
{
    public class UserEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _dbName;

        public UserEndpointTests(WebApplicationFactory<Program> factory)
        {
            _dbName = "UserTestDb_" + Guid.NewGuid();
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

        private async Task<(HttpClient Client, Felhasznalo User)> CreateAuthenticatedClientAsync(string role)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            var user = new Felhasznalo
            {
                Username = $"user_{role.ToLower()}_{Guid.NewGuid().ToString("N")[..6]}",
                Nev = $"Teszt {role}",
                Email = $"{Guid.NewGuid().ToString("N")[..6]}@test.hu",
                Role = role,
                JelszoHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Felhasznalok.Add(user);
            await db.SaveChangesAsync();

            var token = auth.GenerateJwtToken(user);

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, user);
        }

        [Fact]
        public async Task GetUsers_ReturnsForbidden_WhenNonAdmin()
        {
            // Arrange
            var (client, _) = await CreateAuthenticatedClientAsync("Oktato");

            // Act
            var response = await client.GetAsync("/api/users");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetUsers_ReturnsOk_WhenAdmin()
        {
            // Arrange
            var (client, _) = await CreateAuthenticatedClientAsync("Adminisztrator");

            // Act
            var response = await client.GetAsync("/api/users?page=1&pageSize=10");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<UserListDto>();
            Assert.NotNull(result);
            Assert.True(result.TotalCount > 0);
        }

        [Fact]
        public async Task CreateUser_Succeeds_WhenValidAdmin()
        {
            // Arrange
            var (client, _) = await CreateAuthenticatedClientAsync("Adminisztrator");
            var newUser = new CreateUserRequestDto
            {
                Username = "uj_oktato_" + Guid.NewGuid().ToString("N")[..5],
                Email = "uj_oktato_" + Guid.NewGuid().ToString("N")[..5] + "@uni.hu",
                Nev = "Új Oktató",
                Jelszo = "BiztonsagosJelszo1!",
                Role = "Oktato"
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/users", newUser);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<UserDto>();
            Assert.NotNull(created);
            Assert.Equal(newUser.Username, created.Username);
            Assert.Equal("Oktato", created.Role);
        }

        [Fact]
        public async Task CreateUser_ReturnsConflict_WhenUsernameAlreadyExists()
        {
            // Arrange
            var (client, existingUser) = await CreateAuthenticatedClientAsync("Adminisztrator");
            var duplicate = new CreateUserRequestDto
            {
                Username = existingUser.Username,
                Email = "mas_email@uni.hu",
                Nev = "Más Név",
                Jelszo = "BiztonsagosJelszo1!",
                Role = "Portas"
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/users", duplicate);

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task ChangeUserRole_UpdatesRoleSuccessfully()
        {
            // Arrange
            var (client, _) = await CreateAuthenticatedClientAsync("Adminisztrator");
            var (_, targetUser) = await CreateAuthenticatedClientAsync("Oktato");

            // Act
            var response = await client.PutAsJsonAsync($"/api/users/{targetUser.Id}/role", new ChangeRoleRequestDto
            {
                UjRole = "Portas"
            });

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = await response.Content.ReadFromJsonAsync<UserDto>();
            Assert.NotNull(updated);
            Assert.Equal("Portas", updated.Role);
        }
    }
}
