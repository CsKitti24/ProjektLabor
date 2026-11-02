using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Tickets;
using ProjektLabor.Models;
using ProjektLabor.Services;
using Xunit;

namespace backend.Tests
{
    public class TicketEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string _dbName;

        public TicketEndpointTests(WebApplicationFactory<Program> factory)
        {
            _dbName = "TicketTestDb_" + Guid.NewGuid();
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
                Username = $"ticket_u_{role.ToLower()}_{Guid.NewGuid().ToString("N")[..6]}",
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
        public async Task CreateAndResolveTicket_WorksCorrectly()
        {
            // Arrange
            var oktatoClient = await CreateClientWithRoleAsync("Oktato");
            var adminClient = await CreateClientWithRoleAsync("Adminisztrator");

            int roomId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var room = new Terem { Nev = "Hiba Teszt Terem", Kapacitas = 30, Aktivalva = true };
                db.Termek.Add(room);
                await db.SaveChangesAsync();
                roomId = room.Id;
            }

            // 1. Oktato creates ticket
            var createRes = await oktatoClient.PostAsJsonAsync("/api/tickets", new CreateTicketRequestDto
            {
                TeremId = roomId,
                Kategoria = "Projektor",
                Leiras = "Nem kapcsol be a kivetítő"
            });

            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
            var created = await createRes.Content.ReadFromJsonAsync<TicketDto>();
            Assert.NotNull(created);
            Assert.Equal("Nyitva", created.Statusz);

            // 2. Admin updates ticket status
            var updateRes = await adminClient.PutAsJsonAsync($"/api/tickets/{created.Id}/status", new UpdateTicketStatusRequestDto
            {
                Statusz = "Megoldva"
            });

            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
            var updated = await updateRes.Content.ReadFromJsonAsync<TicketDto>();
            Assert.NotNull(updated);
            Assert.Equal("Megoldva", updated.Statusz);
        }
    }
}
