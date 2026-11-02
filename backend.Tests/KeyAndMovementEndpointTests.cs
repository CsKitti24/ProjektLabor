using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs.KeyMovements;
using ProjektLabor.DTOs.Keys;
using ProjektLabor.Models;
using ProjektLabor.Services;
using Xunit;

namespace backend.Tests
{
    public class KeyAndMovementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _dbName;

        public KeyAndMovementEndpointTests(WebApplicationFactory<Program> factory)
        {
            _dbName = "KeyTestDb_" + Guid.NewGuid();
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

        private async Task<HttpClient> CreateClientWithRoleAsync(string role)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            var user = new Felhasznalo
            {
                Username = $"km_test_{role.ToLower()}_{Guid.NewGuid().ToString("N")[..6]}",
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
            return client;
        }

        private async Task<(int RoomId, int KeyId)> SeedRoomAndKeyAsync(string keyNumber)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var room = new Terem { Nev = "Terem " + keyNumber, Kapacitas = 20, Aktivalva = true };
            db.Termek.Add(room);
            await db.SaveChangesAsync();

            var key = new Kulcs
            {
                Szam = keyNumber,
                Tipus = "Osztalykulcs",
                TeremId = room.Id,
                Aktivalva = true
            };
            db.Kulcsok.Add(key);
            await db.SaveChangesAsync();

            return (room.Id, key.Id);
        }

        [Fact]
        public async Task CreateKey_Succeeds_ForAdmin()
        {
            // Arrange
            var client = await CreateClientWithRoleAsync("Adminisztrator");
            var (roomId, _) = await SeedRoomAndKeyAsync("K-100");

            var newKeyDto = new CreateKeyRequestDto
            {
                Szam = "K-101",
                Tipus = "Osztalykulcs",
                TeremId = roomId,
                Aktivalva = true
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/keys", newKeyDto);

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<KeyDto>();
            Assert.NotNull(created);
            Assert.Equal("K-101", created.Szam);
            Assert.Equal("Szabad", created.JelenlegiAllapot);
        }

        [Fact]
        public async Task IssueAndReturnKey_Lifecycle_WorksCorrectly()
        {
            // Arrange
            var portasClient = await CreateClientWithRoleAsync("Portas");
            var (_, keyId) = await SeedRoomAndKeyAsync("K-200");

            // 1. Check initial state is Szabad
            var statusBefore = await portasClient.GetFromJsonAsync<KeyStatusDto>($"/api/keys/{keyId}/status");
            Assert.NotNull(statusBefore);
            Assert.Equal("Szabad", statusBefore.Allapot);

            // 2. Issue key
            var issueResponse = await portasClient.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
            {
                KulcsId = keyId,
                Megjegyzes = "Átadva órára"
            });
            Assert.Equal(HttpStatusCode.Created, issueResponse.StatusCode);

            // 3. Check state is now Kiadva
            var statusIssued = await portasClient.GetFromJsonAsync<KeyStatusDto>($"/api/keys/{keyId}/status");
            Assert.NotNull(statusIssued);
            Assert.Equal("Kiadva", statusIssued.Allapot);

            // 4. Try issuing again -> should Conflict (409)
            var doubleIssue = await portasClient.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
            {
                KulcsId = keyId
            });
            Assert.Equal(HttpStatusCode.Conflict, doubleIssue.StatusCode);

            // 5. Return key
            var returnResponse = await portasClient.PostAsJsonAsync("/api/key-movements/return", new ReturnKeyRequestDto
            {
                KulcsId = keyId,
                Megjegyzes = "Visszahozva sértetlenül"
            });
            Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

            // 6. Check state is Szabad again
            var statusReturned = await portasClient.GetFromJsonAsync<KeyStatusDto>($"/api/keys/{keyId}/status");
            Assert.NotNull(statusReturned);
            Assert.Equal("Szabad", statusReturned.Allapot);
        }
    }
}
