using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace backend.Tests;

public sealed class MaintenanceEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _databaseName = $"Maintenance_{Guid.NewGuid():N}";

    public MaintenanceEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    service => service.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        });
    }

    [Fact]
    public async Task MaintenanceConflict_ReturnsBookingsAndRequiresAdminCancellationOrRescheduling()
    {
        int adminId;
        int userId;
        int roomId;
        int alternateRoomId;
        int firstBookingId;
        int secondBookingId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = CreateUser("maintenance_admin", "Adminisztrator");
            var teacher = CreateUser("maintenance_teacher", "Oktato");
            var room = new Terem { Nev = "Karbantartási terem", Kapacitas = 20 };
            var alternateRoom = new Terem { Nev = "Áthelyezési terem", Kapacitas = 20 };
            db.AddRange(admin, teacher, room, alternateRoom);
            await db.SaveChangesAsync();
            adminId = admin.Id;
            userId = teacher.Id;
            roomId = room.Id;
            alternateRoomId = alternateRoom.Id;

            var firstBooking = new Foglalas
            {
                FelhasznaloId = userId,
                TeremId = roomId,
                StartTime = new DateTime(2027, 1, 1, 8, 45, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2027, 1, 1, 9, 30, 0, DateTimeKind.Utc),
                Status = "Aktiv"
            };
            var secondBooking = new Foglalas
            {
                FelhasznaloId = userId,
                TeremId = roomId,
                StartTime = new DateTime(2027, 1, 1, 9, 30, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2027, 1, 1, 10, 15, 0, DateTimeKind.Utc),
                Status = "Aktiv"
            };
            db.Foglalasok.AddRange(firstBooking, secondBooking);
            await db.SaveChangesAsync();
            firstBookingId = firstBooking.Id;
            secondBookingId = secondBooking.Id;
        }

        using var client = CreateAdminClient(adminId);
        var maintenanceRequest = new
        {
            teremId = roomId,
            startTime = new DateTime(2027, 1, 1, 8, 45, 0, DateTimeKind.Utc),
            endTime = new DateTime(2027, 1, 1, 10, 15, 0, DateTimeKind.Utc),
            leiras = "Projektor csere"
        };

        var conflict = await client.PostAsJsonAsync("/api/maintenance", maintenanceRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var document = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()))
        {
            Assert.Contains("törölni vagy áthelyezni", document.RootElement.GetProperty("message").GetString());
            var conflicts = document.RootElement.GetProperty("conflicts");
            Assert.Equal(2, conflicts.GetArrayLength());
            Assert.Contains(conflicts.EnumerateArray(), item =>
                item.GetProperty("foglalasId").GetInt32() == firstBookingId);
            Assert.Contains(conflicts.EnumerateArray(), item =>
                item.GetProperty("foglalasId").GetInt32() == secondBookingId);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.KarbantartasiIdosavok.ToListAsync());
        }

        var moved = await client.PutAsJsonAsync(
            $"/api/bookings/{firstBookingId}",
            new { teremId = alternateRoomId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var cancelled = await client.PutAsJsonAsync(
            $"/api/bookings/{secondBookingId}",
            new { status = "Megszuntetve" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var created = await client.PostAsJsonAsync("/api/maintenance", maintenanceRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private HttpClient CreateAdminClient(int adminId)
    {
        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var user = scope.ServiceProvider.GetRequiredService<AppDbContext>().Felhasznalok.Find(adminId)!;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authService.GenerateJwtToken(user));
        return client;
    }

    private static Felhasznalo CreateUser(string username, string role) => new()
    {
        Username = username,
        Nev = username,
        Email = $"{username}@test.hu",
        Role = role,
        JelszoHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!")
    };
}
