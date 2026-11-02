using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Bookings;
using ProjektLabor.Models;
using ProjektLabor.Services;
using Xunit;

namespace backend.Tests
{
    public class BookingEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _dbName;

        public BookingEndpointTests(WebApplicationFactory<Program> factory)
        {
            _dbName = "BookingTestDb_" + Guid.NewGuid();
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

        private async Task<(HttpClient Client, Felhasznalo User)> CreateAuthenticatedUserAsync(string role)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();

            var user = new Felhasznalo
            {
                Username = $"booking_u_{role.ToLower()}_{Guid.NewGuid().ToString("N")[..6]}",
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

        private async Task<int> SeedRoomAsync(string name)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var room = new Terem { Nev = name, Kapacitas = 50, Aktivalva = true };
            db.Termek.Add(room);
            await db.SaveChangesAsync();
            return room.Id;
        }

        [Fact]
        public async Task CreateBooking_Succeeds_AndPreventsOverlappingBooking()
        {
            // Arrange
            _ = await CreateAuthenticatedUserAsync("Oktato");
            var (client, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Foglalas Terem 1");

            var start = DateTime.UtcNow.Date.AddDays(1).AddHours(10);
            var end = start.AddHours(2);

            // 1. Create first booking
            var response1 = await client.PostAsJsonAsync("/api/bookings", new
            {
                felhasznaloId = 1,
                TeremId = roomId,
                StartTime = start,
                EndTime = end
            });

            Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
            var b1 = await response1.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(b1);
            Assert.Equal("Aktiv", b1.Status);
            Assert.NotEqual(1, b1.FelhasznaloId);

            // 2. Try creating overlapping booking in same room
            var overlapStart = start.AddMinutes(30);
            var overlapEnd = end.AddMinutes(30);

            var (client2, _) = await CreateAuthenticatedUserAsync("Oktato");
            var response2 = await client2.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = overlapStart,
                EndTime = overlapEnd
            });

            // Assert conflict 409
            Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);
        }

        [Fact]
        public async Task GetMyBookings_ReturnsOnlyCurrentUserBookings()
        {
            // Arrange
            var (client1, _) = await CreateAuthenticatedUserAsync("Oktato");
            var (client2, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Foglalas Terem 2");

            var start = DateTime.UtcNow.Date.AddDays(2).AddHours(10);
            var end = start.AddHours(2);

            await client1.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = start,
                EndTime = end
            });

            // Act
            var myBookingsRes = await client1.GetAsync("/api/bookings/my");
            var otherBookingsRes = await client2.GetAsync("/api/bookings/my");

            // Assert
            Assert.Equal(HttpStatusCode.OK, myBookingsRes.StatusCode);
            var myBookings = await myBookingsRes.Content.ReadFromJsonAsync<List<BookingDto>>();
            Assert.NotNull(myBookings);
            Assert.Single(myBookings);

            var otherBookings = await otherBookingsRes.Content.ReadFromJsonAsync<List<BookingDto>>();
            Assert.NotNull(otherBookings);
            Assert.Empty(otherBookings);

            var forbiddenDetails = await client2.GetAsync($"/api/bookings/{myBookings[0].Id}");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDetails.StatusCode);

            var forbiddenList = await client1.GetAsync("/api/bookings");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenList.StatusCode);
        }

        [Fact]
        public async Task CancelBooking_SetsStatusToMegszuntetve()
        {
            // Arrange
            var (client, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Foglalas Terem 3");

            var start = DateTime.UtcNow.Date.AddDays(3).AddHours(14);
            var end = start.AddHours(1);

            var createRes = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = start,
                EndTime = end
            });
            var b = await createRes.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(b);

            // Act
            var deleteRes = await client.DeleteAsync($"/api/bookings/{b.Id}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, deleteRes.StatusCode);

            // Verify status
            var getRes = await client.GetAsync($"/api/bookings/{b.Id}");
            var updated = await getRes.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(updated);
            Assert.Equal("Megszuntetve", updated.Status);
        }
    }
}
