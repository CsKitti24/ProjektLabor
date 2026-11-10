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
        public async Task GetBookings_UsesDefaultPagination_WhenQueryParametersAreOmitted()
        {
            var (client, _) = await CreateAuthenticatedUserAsync("Adminisztrator");

            var response = await client.GetAsync("/api/bookings");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<BookingListDto>();
            Assert.NotNull(result);
            Assert.Equal(1, result.Page);
            Assert.Equal(20, result.PageSize);
        }

        [Fact]
        public async Task CreateBooking_Succeeds_AndPreventsOverlappingBooking()
        {
            // Arrange
            _ = await CreateAuthenticatedUserAsync("Oktato");
            var (client, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Foglalas Terem 1");

            var start = DateTime.UtcNow.Date.AddDays(1).AddHours(10).AddMinutes(15);
            var end = start.AddMinutes(90);

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
            var overlapStart = start;
            var overlapEnd = start.AddMinutes(135);

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

            var start = DateTime.UtcNow.Date.AddDays(2).AddHours(10).AddMinutes(15);
            var end = start.AddMinutes(90);

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
            var end = start.AddMinutes(45);

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

        [Fact]
        public async Task CreateBooking_WithSpecialNeeds_SavesAndReturnsSpecialNeeds()
        {
            // Arrange
            var (client, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Speciális Igény Terem");

            var start = DateTime.UtcNow.Date.AddDays(4).AddHours(8).AddMinutes(45);
            var end = start.AddMinutes(90);
            var specialNeeds = "Projektor, laboratóriumi szoftverek, beépített hangosítás";

            // Act
            var createRes = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = start,
                EndTime = end,
                SpecialisIgenyek = specialNeeds
            });

            // Assert
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var created = await createRes.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(created);
            Assert.Equal(specialNeeds, created.SpecialisIgenyek);

            // Query by ID
            var getRes = await client.GetAsync($"/api/bookings/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
            var fetched = await getRes.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(fetched);
            Assert.Equal(specialNeeds, fetched.SpecialisIgenyek);

            // Query /my
            var myRes = await client.GetAsync("/api/bookings/my");
            Assert.Equal(HttpStatusCode.OK, myRes.StatusCode);
            var myBookings = await myRes.Content.ReadFromJsonAsync<List<BookingDto>>();
            Assert.NotNull(myBookings);
            var myItem = myBookings.FirstOrDefault(b => b.Id == created.Id);
            Assert.NotNull(myItem);
            Assert.Equal(specialNeeds, myItem.SpecialisIgenyek);
        }

        [Fact]
        public async Task CreateBooking_AutomaticActivation_DirectlyActiveWithoutAdminApproval()
        {
            // Arrange
            var (client, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Auto Aktiv Terem");

            var start = DateTime.UtcNow.Date.AddDays(5).AddHours(8).AddMinutes(45); // 08:45 is a valid 45-min slot
            var end = start.AddMinutes(45);

            // Act - Instructor creates booking
            var createRes = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = start,
                EndTime = end
            });

            // Assert - booking is immediately 'Aktiv' without any admin approval required
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var created = await createRes.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(created);
            Assert.Equal("Aktiv", created.Status);
        }

        [Fact]
        public async Task CreateBooking_AdjacentConsecutiveSlots_SucceedsWithoutConflict()
        {
            // Arrange
            var (client1, _) = await CreateAuthenticatedUserAsync("Oktato");
            var (client2, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Szomszédos Sáv Terem");

            var startSlot1 = DateTime.UtcNow.Date.AddDays(6).AddHours(8);
            var endSlot1 = startSlot1.AddMinutes(45); // 08:00 - 08:45

            var startSlot2 = endSlot1; // 08:45 - 09:30 (starts exactly when previous ends)
            var endSlot2 = startSlot2.AddMinutes(45);

            // Act - slot 1
            var res1 = await client1.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = startSlot1,
                EndTime = endSlot1
            });

            // Act - slot 2
            var res2 = await client2.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = startSlot2,
                EndTime = endSlot2
            });

            // Assert - both consecutive non-overlapping bookings must succeed
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
            Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        }

        [Fact]
        public async Task CreateBooking_ConcurrentRequests_OnlyOneSucceedsAndOtherGetsConflict()
        {
            // Arrange
            var (client1, _) = await CreateAuthenticatedUserAsync("Oktato");
            var (client2, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("Konkurens Terem");

            var start = DateTime.UtcNow.Date.AddDays(7).AddHours(11);
            var end = start.AddMinutes(45);

            var req1 = new CreateBookingRequestDto { TeremId = roomId, StartTime = start, EndTime = end };
            var req2 = new CreateBookingRequestDto { TeremId = roomId, StartTime = start, EndTime = end };

            // Act - Run two booking requests concurrently
            var task1 = client1.PostAsJsonAsync("/api/bookings", req1);
            var task2 = client2.PostAsJsonAsync("/api/bookings", req2);

            var responses = await Task.WhenAll(task1, task2);

            // Assert - Exactly one must be Created (201) and the other Conflict (409)
            var statuses = responses.Select(r => r.StatusCode).ToList();
            Assert.Contains(HttpStatusCode.Created, statuses);
            Assert.Contains(HttpStatusCode.Conflict, statuses);
        }

        [Fact]
        public async Task NoShow_ReleasesRoom_AllowsNewBookingForSameSlot()
        {
            // Arrange
            var (client1, _) = await CreateAuthenticatedUserAsync("Oktato");
            var (client2, _) = await CreateAuthenticatedUserAsync("Oktato");
            var roomId = await SeedRoomAsync("NoShow Felszabadítás Terem");

            var start = DateTime.UtcNow.Date.AddDays(8).AddHours(14);
            var end = start.AddMinutes(90);

            // 1. Create first booking
            var res1 = await client1.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = start,
                EndTime = end
            });
            Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
            var b1 = await res1.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(b1);

            // 2. Booking transitions to NoShow
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var bookingEntity = await db.Foglalasok.FindAsync(b1.Id);
                Assert.NotNull(bookingEntity);
                bookingEntity.Status = "NoShow";
                bookingEntity.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            // 3. New booking for the EXACT same room and slot should succeed because NoShow released the room
            var res2 = await client2.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = start,
                EndTime = end
            });

            Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
            var b2 = await res2.Content.ReadFromJsonAsync<BookingDto>();
            Assert.NotNull(b2);
            Assert.Equal("Aktiv", b2.Status);
            Assert.NotEqual(b1.Id, b2.Id);
        }
    }
}
