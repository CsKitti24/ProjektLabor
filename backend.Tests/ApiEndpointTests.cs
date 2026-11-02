using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs;
using ProjektLabor.Models;
using Xunit;

namespace backend.Tests
{
    public class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ApiEndpointTests(WebApplicationFactory<Program> factory)
        {
            var dbName = "TestDb_" + Guid.NewGuid();
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove existing DbContext registration
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add in-memory database for testing
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });
                });
            });
        }

        [Fact]
        public async Task GetHealth_ReturnsOk_WhenDatabaseReachable()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/health");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(content);
            Assert.Equal("ok", content.Status);
            Assert.Equal("connected", content.Database);
        }

        [Fact]
        public async Task GetRooms_ReturnsEmptyList_WhenNoRoomsExist()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/rooms");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rooms = await response.Content.ReadFromJsonAsync<List<RoomDto>>();
            Assert.NotNull(rooms);
            Assert.Empty(rooms);
        }

        [Fact]
        public async Task GetRooms_ReturnsRooms_WhenRoomsExist()
        {
            // Arrange
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Termek.Add(new Terem
                {
                    Nev = "I. előadó",
                    Kapacitas = 120,
                    Leiras = "Főépület 1. emelet",
                    Aktivalva = true
                });
                await db.SaveChangesAsync();
            }

            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/rooms");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rooms = await response.Content.ReadFromJsonAsync<List<RoomDto>>();
            Assert.NotNull(rooms);
            Assert.NotEmpty(rooms);
            Assert.Contains(rooms, r => r.Nev == "I. előadó" && r.Kapacitas == 120);
        }

        [Fact]
        public async Task GetRoomById_ReturnsNotFound_WhenRoomDoesNotExist()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/rooms/9999");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task GetHealth_ReturnsServiceUnavailable_WhenDatabaseUnreachable()
        {
            var failingFactory = _factory.WithWebHostBuilder(builder =>
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
                        options.UseSqlServer("Server=unreachable-server-host;Database=NonExistentDb;Connect Timeout=1;");
                    });
                });
            });

            var client = failingFactory.CreateClient();
            var response = await client.GetAsync("/api/health");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var content = await response.Content.ReadFromJsonAsync<HealthResponse>();
            Assert.NotNull(content);
            Assert.Equal("unhealthy", content.Status);
            Assert.Equal("disconnected", content.Database);
        }

        private class HealthResponse
        {
            public string Status { get; set; } = string.Empty;
            public string Database { get; set; } = string.Empty;
        }
    }
}
