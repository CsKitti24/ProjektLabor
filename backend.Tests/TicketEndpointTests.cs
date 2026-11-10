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

        private async Task<(HttpClient Client, int UserId)> CreateClientWithRoleAsync(string role)
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
            return (client, user.Id);
        }

        [Fact]
        public async Task InstructorCanCreateAndOnlyReadOwnTickets()
        {
            var (teacherClient, teacherId) = await CreateClientWithRoleAsync("Oktato");
            var (otherTeacherClient, otherTeacherId) = await CreateClientWithRoleAsync("Oktato");
            var (portasClient, _) = await CreateClientWithRoleAsync("Portas");
            var (adminClient, _) = await CreateClientWithRoleAsync("Adminisztrator");

            int roomId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var room = new Terem { Nev = "Hiba Teszt Terem", Kapacitas = 30, Aktivalva = true };
                db.Termek.Add(room);
                await db.SaveChangesAsync();
                roomId = room.Id;
            }

            var ownResponse = await teacherClient.PostAsJsonAsync("/api/tickets", new
            {
                felhasznaloId = otherTeacherId,
                TeremId = roomId,
                Kategoria = "Projektor",
                Leiras = "Nem kapcsol be a kivetítő"
            });
            Assert.Equal(HttpStatusCode.Created, ownResponse.StatusCode);
            var ownTicket = await ownResponse.Content.ReadFromJsonAsync<TicketDto>();
            Assert.NotNull(ownTicket);
            Assert.Equal(teacherId, ownTicket.FelhasznaloId);
            Assert.Equal("Nyitva", ownTicket.Statusz);

            var otherResponse = await otherTeacherClient.PostAsJsonAsync("/api/tickets", new
            {
                TeremId = roomId,
                Kategoria = "Projektor",
                Leiras = "A másik oktató hibajegye"
            });
            Assert.Equal(HttpStatusCode.Created, otherResponse.StatusCode);
            var otherTicket = await otherResponse.Content.ReadFromJsonAsync<TicketDto>();
            Assert.NotNull(otherTicket);

            var ownList = await teacherClient.GetFromJsonAsync<TicketListDto>("/api/tickets");
            Assert.NotNull(ownList);
            Assert.Equal(1, ownList.TotalCount);
            Assert.Equal(teacherId, Assert.Single(ownList.Items).FelhasznaloId);
            Assert.Equal(HttpStatusCode.OK, (await teacherClient.GetAsync($"/api/tickets/{ownTicket.Id}")).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await teacherClient.GetAsync($"/api/tickets/{otherTicket.Id}")).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await teacherClient.PutAsJsonAsync(
                    $"/api/tickets/{ownTicket.Id}/status",
                    new UpdateTicketStatusRequestDto { Statusz = "Megoldva" })).StatusCode);

            // Portas and Admin retain access to all tickets; Admin retains status updates.
            Assert.Equal(HttpStatusCode.Created, (await portasClient.PostAsJsonAsync("/api/tickets", new
            {
                TeremId = roomId,
                Kategoria = "Projektor",
                Leiras = "Portás által rögzített hiba"
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await adminClient.PostAsJsonAsync("/api/tickets", new
            {
                TeremId = roomId,
                Kategoria = "Projektor",
                Leiras = "Admin által rögzített hiba"
            })).StatusCode);
            var allTickets = await portasClient.GetFromJsonAsync<TicketListDto>("/api/tickets");
            Assert.NotNull(allTickets);
            Assert.Equal(4, allTickets.TotalCount);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var owner = await db.Felhasznalok.FindAsync(ownTicket.FelhasznaloId);
                Assert.NotNull(owner);
                Assert.Equal("Oktato", owner.Role);
            }

            var updateRes = await adminClient.PutAsJsonAsync($"/api/tickets/{ownTicket.Id}/status", new UpdateTicketStatusRequestDto
            {
                Statusz = "Megoldva"
            });

            Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
            var updated = await updateRes.Content.ReadFromJsonAsync<TicketDto>();
            Assert.NotNull(updated);
            Assert.Equal("Megoldva", updated.Statusz);
        }

        [Fact]
        public async Task TicketCreation_RejectsUnauthorizedRole()
        {
            var (directorClient, _) = await CreateClientWithRoleAsync("UzemeltetesiIgazgato");
            int roomId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var room = new Terem { Nev = "Ticket role test", Kapacitas = 20, Aktivalva = true };
                db.Termek.Add(room);
                await db.SaveChangesAsync();
                roomId = room.Id;
            }

            var response = await directorClient.PostAsJsonAsync("/api/tickets", new
            {
                TeremId = roomId,
                Kategoria = "Projektor",
                Leiras = "Nem kapcsol be"
            });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
