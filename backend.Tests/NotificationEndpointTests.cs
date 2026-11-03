using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjektLabor.Data;
using ProjektLabor.Endpoints;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace backend.Tests;

public class NotificationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _databaseName = "NotificationEndpointTests_" + Guid.NewGuid();

    public NotificationEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task MyNotifications_ReturnsOnlyNotificationsForAuthenticatedInstructor()
    {
        var (ownerClient, owner) = await CreateClientAsync("Oktato");
        var (_, otherUser) = await CreateClientAsync("Oktato");
        await SeedNotificationAsync(owner.Id);
        await SeedNotificationAsync(otherUser.Id);

        var response = await ownerClient.GetAsync($"/api/notifications/my?userId={otherUser.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notifications = await response.Content.ReadFromJsonAsync<List<NotificationEndpoints.NotificationResponse>>();
        var notification = Assert.Single(notifications!);
        Assert.Equal(owner.Id, await GetBookingOwnerIdAsync(notification.FoglalasId));
    }

    [Fact]
    public async Task PendingNotifications_RequiresStaffRole()
    {
        using var anonymousClient = _factory.CreateClient();
        var (instructorClient, _) = await CreateClientAsync("Oktato");
        var (receptionistClient, owner) = await CreateClientAsync("Portas");
        await SeedNotificationAsync(owner.Id, "KuldesAlatt");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync("/api/notifications/pending")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await instructorClient.GetAsync("/api/notifications/pending")).StatusCode);

        var (administratorClient, _) = await CreateClientAsync("Adminisztrator");
        var response = await receptionistClient.GetAsync("/api/notifications/pending");
        var administratorResponse = await administratorClient.GetAsync("/api/notifications/pending");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, administratorResponse.StatusCode);
        var notifications = await response.Content.ReadFromJsonAsync<List<NotificationEndpoints.NotificationResponse>>();
        Assert.NotNull(notifications);
        var notification = Assert.Single(notifications);
        Assert.Equal("KuldesAlatt", notification.Allapot);
    }

    [Fact]
    public void AutomationBackgroundServices_AreRegisteredWithDependencyInjection()
    {
        using var client = _factory.CreateClient();
        var hostedServices = _factory.Services.GetServices<IHostedService>();
        Assert.Contains(hostedServices, service => service is NoShowBackgroundService);
        Assert.Contains(hostedServices, service => service is BookingExpiryNotificationBackgroundService);
        Assert.Contains(hostedServices, service => service is DailyKeyClosingBackgroundService);
    }

    private async Task<(HttpClient Client, Felhasznalo User)> CreateClientAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new Felhasznalo
        {
            Username = suffix[..12],
            Email = $"{suffix}@test.hu",
            JelszoHash = BCrypt.Net.BCrypt.HashPassword("Test1234!"),
            Nev = role,
            Role = role
        };
        db.Felhasznalok.Add(user);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authService.GenerateJwtToken(user));
        return (client, user);
    }

    private async Task SeedNotificationAsync(int userId, string state = "Kesz")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = new Terem
        {
            Nev = Guid.NewGuid().ToString("N"),
            Kapacitas = 20,
            Aktivalva = true
        };
        db.Termek.Add(room);
        await db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(1);
        var booking = new Foglalas
        {
            FelhasznaloId = userId,
            TeremId = room.Id,
            StartTime = start,
            EndTime = start.AddHours(1),
            Status = "Aktiv"
        };
        db.Foglalasok.Add(booking);
        await db.SaveChangesAsync();
        db.Ertesitesek.Add(new Ertesites
        {
            FoglalasId = booking.Id,
            ErtesitesTipus = "BeforeEnd",
            IdoPont = DateTime.UtcNow,
            Allapot = state
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> GetBookingOwnerIdAsync(int bookingId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Foglalasok
            .Where(booking => booking.Id == bookingId)
            .Select(booking => booking.FelhasznaloId)
            .SingleAsync();
    }
}
