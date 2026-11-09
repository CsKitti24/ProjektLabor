using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs;
using ProjektLabor.DTOs.Rooms;
using ProjektLabor.Models;
using ProjektLabor.Services;
using Xunit;

namespace backend.Tests
{
    public class RoomEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _dbName;

        public RoomEndpointTests(WebApplicationFactory<Program> factory)
        {
            _dbName = "RoomTestDb_" + Guid.NewGuid();
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
                Username = $"room_test_{role.ToLower()}_{Guid.NewGuid().ToString("N")[..6]}",
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

        [Fact]
        public async Task CreateRoom_Forbidden_ForNonAdmin()
        {
            // Arrange
            var client = await CreateClientWithRoleAsync("Oktato");

            // Act
            var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = "Labor 101",
                Kapacitas = 25
            });

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task CreateRoom_Succeeds_ForAdmin()
        {
            // Arrange
            var client = await CreateClientWithRoleAsync("Adminisztrator");

            // Act
            var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = "Labor 202",
                Kapacitas = 30,
                Leiras = "Informatika labor"
            });

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var room = await response.Content.ReadFromJsonAsync<RoomDto>();
            Assert.NotNull(room);
            Assert.Equal("Labor 202", room.Nev);
            Assert.Equal(30, room.Kapacitas);
        }

        [Fact]
        public async Task UpdateRoom_UpdatesDetailsSuccessfully()
        {
            // Arrange
            var client = await CreateClientWithRoleAsync("Adminisztrator");
            var createRes = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = "Terem 303",
                Kapacitas = 40
            });
            var created = await createRes.Content.ReadFromJsonAsync<RoomDto>();
            Assert.NotNull(created);

            // Act
            var updateRes = await client.PutAsJsonAsync($"/api/rooms/{created.Id}", new UpdateRoomRequestDto
            {
                Nev = "Terem 303 Módosított",
                Kapacitas = 50
            });

            // Assert
            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
            var updated = await updateRes.Content.ReadFromJsonAsync<RoomDto>();
            Assert.NotNull(updated);
            Assert.Equal("Terem 303 Módosított", updated.Nev);
            Assert.Equal(50, updated.Kapacitas);
        }

        [Fact]
        public async Task DeleteRoom_DeactivatesRoom()
        {
            // Arrange
            var client = await CreateClientWithRoleAsync("Adminisztrator");
            var createRes = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = "Deaktiválandó Terem",
                Kapacitas = 10
            });
            var created = await createRes.Content.ReadFromJsonAsync<RoomDto>();
            Assert.NotNull(created);

            // Act
            var deleteRes = await client.DeleteAsync($"/api/rooms/{created.Id}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

            // Verify Aktivalva is false
            var getRes = await client.GetAsync($"/api/rooms/{created.Id}");
            var room = await getRes.Content.ReadFromJsonAsync<RoomDto>();
            Assert.NotNull(room);
            Assert.False(room.Aktivalva);
        }

        [Fact]
        public async Task GetRoomAvailability_ReturnsSlots()
        {
            // Arrange
            var client = await CreateClientWithRoleAsync("Oktato");
            int roomId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var r = new Terem { Nev = "Előadó Avail", Kapacitas = 100, Aktivalva = true };
                db.Termek.Add(r);
                await db.SaveChangesAsync();
                roomId = r.Id;
            }

            // Act
            var response = await client.GetAsync($"/api/rooms/{roomId}/availability?datum={DateTime.UtcNow:yyyy-MM-dd}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var avail = await response.Content.ReadFromJsonAsync<RoomAvailabilityDto>();
            Assert.NotNull(avail);
            Assert.Equal(roomId, avail.TeremId);
            Assert.NotEmpty(avail.Szabad);
        }

        [Fact]
        public async Task CreateAndUpdateRoom_SupportsFelszereltseg()
        {
            // Arrange
            var client = await CreateClientWithRoleAsync("Adminisztrator");

            // Act - Create room with equipment
            var createRes = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = "Robotika Labor",
                Kapacitas = 24,
                Felszereltseg = "Robotkarok, ipari PLC-k, projektor",
                Leiras = "Informatikai és automatizálási szárny"
            });

            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var created = await createRes.Content.ReadFromJsonAsync<RoomDto>();
            Assert.NotNull(created);
            Assert.Equal("Robotkarok, ipari PLC-k, projektor", created.Felszereltseg);

            // Act - Update equipment
            var updateRes = await client.PutAsJsonAsync($"/api/rooms/{created.Id}", new UpdateRoomRequestDto
            {
                Felszereltseg = "Bővített robotkarok, 3D nyomtatók, projektor"
            });

            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
            var updated = await updateRes.Content.ReadFromJsonAsync<RoomDto>();
            Assert.NotNull(updated);
            Assert.Equal("Bővített robotkarok, 3D nyomtatók, projektor", updated.Felszereltseg);
        }

        [Fact]
        public async Task GetRooms_FiltersByCapacityAndEquipment()
        {
            // Arrange
            var adminClient = await CreateClientWithRoleAsync("Adminisztrator");
            var oktatoClient = await CreateClientWithRoleAsync("Oktato");

            // Create distinctive rooms for testing filters
            var prefix = "FilterTest_" + Guid.NewGuid().ToString("N")[..6];

            await adminClient.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = $"{prefix}_SmallLab",
                Kapacitas = 15,
                Felszereltseg = "PC labor, projektor"
            });

            await adminClient.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = $"{prefix}_BigLecture",
                Kapacitas = 120,
                Felszereltseg = "Projektor, mikrofon, hangrendszer"
            });

            await adminClient.PostAsJsonAsync("/api/rooms", new CreateRoomRequestDto
            {
                Nev = $"{prefix}_BigLab",
                Kapacitas = 50,
                Felszereltseg = "Számítógépes labor, projektor, 3D nyomtató"
            });

            // 1. Filter by minKapacitas = 50
            var capRes = await oktatoClient.GetAsync("/api/rooms?minKapacitas=50");
            Assert.Equal(HttpStatusCode.OK, capRes.StatusCode);
            var capRooms = await capRes.Content.ReadFromJsonAsync<List<RoomDto>>();
            Assert.NotNull(capRooms);
            var testCapRooms = capRooms.Where(r => r.Nev.StartsWith(prefix)).ToList();
            Assert.Equal(2, testCapRooms.Count);
            Assert.DoesNotContain(testCapRooms, r => r.Nev.Contains("SmallLab"));

            // 2. Filter by felszereltseg = "labor"
            var equipRes = await oktatoClient.GetAsync("/api/rooms?felszereltseg=labor");
            Assert.Equal(HttpStatusCode.OK, equipRes.StatusCode);
            var equipRooms = await equipRes.Content.ReadFromJsonAsync<List<RoomDto>>();
            Assert.NotNull(equipRooms);
            var testEquipRooms = equipRooms.Where(r => r.Nev.StartsWith(prefix)).ToList();
            Assert.Equal(2, testEquipRooms.Count);
            Assert.DoesNotContain(testEquipRooms, r => r.Nev.Contains("BigLecture"));

            // 3. Combined filter: minKapacitas = 50 AND felszereltseg = "labor"
            var combinedRes = await oktatoClient.GetAsync("/api/rooms?minKapacitas=50&felszereltseg=labor");
            Assert.Equal(HttpStatusCode.OK, combinedRes.StatusCode);
            var combinedRooms = await combinedRes.Content.ReadFromJsonAsync<List<RoomDto>>();
            Assert.NotNull(combinedRooms);
            var testCombinedRooms = combinedRooms.Where(r => r.Nev.StartsWith(prefix)).ToList();
            Assert.Single(testCombinedRooms);
            Assert.Equal($"{prefix}_BigLab", testCombinedRooms[0].Nev);
        }
    }
}
