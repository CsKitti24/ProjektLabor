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

        private async Task<(HttpClient Client, int UserId)> CreateClientWithRoleAsync(string role)
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
            return (client, user.Id);
        }

        private async Task<int> SeedInstructorAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Guid.NewGuid().ToString("N");
            var instructor = new Felhasznalo
            {
                Username = suffix[..12],
                Nev = "Teszt oktató",
                Email = $"{suffix}@test.hu",
                Role = "Oktato",
                JelszoHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!"),
                PinHash = BCrypt.Net.BCrypt.HashPassword("1234"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Felhasznalok.Add(instructor);
            await db.SaveChangesAsync();
            return instructor.Id;
        }

        private async Task<(int RoomId, int KeyId)> SeedRoomAndKeyAsync(
            string keyNumber,
            string keyType = "Osztalykulcs")
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var room = new Terem { Nev = "Terem " + keyNumber, Kapacitas = 20, Aktivalva = true };
            db.Termek.Add(room);
            await db.SaveChangesAsync();

            var key = new Kulcs
            {
                Szam = keyNumber,
                Tipus = keyType,
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
            var (client, _) = await CreateClientWithRoleAsync("Adminisztrator");
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
            var (portasClient, portasUserId) = await CreateClientWithRoleAsync("Portas");
            var (_, keyId) = await SeedRoomAndKeyAsync("K-200");
            var recipientId = await SeedInstructorAsync();

            // 1. Check initial state is Szabad
            var statusBefore = await portasClient.GetFromJsonAsync<KeyStatusDto>($"/api/keys/{keyId}/status");
            Assert.NotNull(statusBefore);
            Assert.Equal("Szabad", statusBefore.Allapot);

            // 2. Issue key
            var issueResponse = await portasClient.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
            {
                KulcsId = keyId,
                AtvevoFelhasznaloId = recipientId,
                Pin = "1234",
                TeremAllapot = "Rendben, tiszta",
                Megjegyzes = "Átadva órára"
            });
            Assert.Equal(HttpStatusCode.Created, issueResponse.StatusCode);
            var issue = await issueResponse.Content.ReadFromJsonAsync<KeyMovementItemDto>();
            Assert.NotNull(issue);
            Assert.Equal(recipientId, issue.AtvevoFelhasznaloId);
            Assert.Equal(portasUserId, issue.OperatorId);
            Assert.Equal("Rendben, tiszta", issue.TeremAllapot);
            Assert.InRange(issue.Idopont, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

            // 3. Check state is now Kiadva
            var statusIssued = await portasClient.GetFromJsonAsync<KeyStatusDto>($"/api/keys/{keyId}/status");
            Assert.NotNull(statusIssued);
            Assert.Equal("Kiadva", statusIssued.Allapot);

            // 4. Try issuing again -> should Conflict (409)
            var doubleIssue = await portasClient.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
            {
                KulcsId = keyId,
                AtvevoFelhasznaloId = recipientId,
                Pin = "1234",
                TeremAllapot = "Rendben"
            });
            Assert.Equal(HttpStatusCode.Conflict, doubleIssue.StatusCode);

            // 5. Return key
            var returnResponse = await portasClient.PostAsJsonAsync("/api/key-movements/return", new ReturnKeyRequestDto
            {
                KulcsId = keyId,
                Pin = "1234",
                TeremAllapot = "Burkolatsérülést észleltem",
                SerulesLeiras = "A projektor burkolata repedt.",
                Megjegyzes = "Visszahozva sértetlenül"
            });
            Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
            var returned = await returnResponse.Content.ReadFromJsonAsync<KeyMovementItemDto>();
            Assert.NotNull(returned);
            Assert.Equal(portasUserId, returned.OperatorId);
            Assert.Equal("A projektor burkolata repedt.", returned.SerulesLeiras);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.All(
                    await db.Kulcsmozgasok.OrderBy(movement => movement.Id).ToListAsync(),
                    movement => Assert.Equal(portasUserId, movement.OperátorId));
                var issuedMovement = await db.Kulcsmozgasok.SingleAsync(movement =>
                    movement.KulcsId == keyId && movement.MozgasTipus == "Kiadás");
                Assert.Equal(issuedMovement.Idopont, issuedMovement.CreatedAt);
                Assert.Equal(2, await db.AuditLogok.CountAsync(entry =>
                    entry.EntitasTipus == "Kulcsmozgas" &&
                    (entry.MuveletTipus == "KEY_ISSUE" || entry.MuveletTipus == "KEY_RETURN")));
                var ticket = await db.Hibajegyek.SingleAsync();
                Assert.Equal("Sérülés", ticket.Kategoria);
                Assert.Equal("A projektor burkolata repedt.", ticket.Leiras);
            }

            // 6. Check state is Szabad again
            var statusReturned = await portasClient.GetFromJsonAsync<KeyStatusDto>($"/api/keys/{keyId}/status");
            Assert.NotNull(statusReturned);
            Assert.Equal("Szabad", statusReturned.Allapot);
        }

        [Fact]
        public async Task IssueWithoutBooking_RequiresInstructorRecipient()
        {
            var (portasClient, portasUserId) = await CreateClientWithRoleAsync("Portas");
            var (_, keyId) = await SeedRoomAndKeyAsync("K-201");

            var missingRecipient = await portasClient.PostAsJsonAsync(
                "/api/key-movements/issue",
                new IssueKeyRequestDto { KulcsId = keyId });
            Assert.Equal(HttpStatusCode.BadRequest, missingRecipient.StatusCode);

            var staffAsRecipient = await portasClient.PostAsJsonAsync(
                "/api/key-movements/issue",
                new IssueKeyRequestDto
                {
                    KulcsId = keyId,
                    AtvevoFelhasznaloId = portasUserId,
                    Pin = "1234",
                    TeremAllapot = "Rendben"
                });
            Assert.Equal(HttpStatusCode.BadRequest, staffAsRecipient.StatusCode);
        }

        [Fact]
        public async Task MasterKeyIssue_RequiresGrantAndValidRecipientPin()
        {
            var (portasClient, _) = await CreateClientWithRoleAsync("Portas");
            var (adminClient, _) = await CreateClientWithRoleAsync("Adminisztrator");
            var (roomId, keyId) = await SeedRoomAndKeyAsync("MK-01", "Mesterkulcs");
            var recipientId = await SeedInstructorAsync();
            int bookingId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var booking = new Foglalas
                {
                    FelhasznaloId = recipientId,
                    TeremId = roomId,
                    StartTime = DateTime.UtcNow.AddHours(1),
                    EndTime = DateTime.UtcNow.AddHours(2),
                    Status = "Aktiv"
                };
                db.Foglalasok.Add(booking);
                await db.SaveChangesAsync();
                bookingId = booking.Id;
            }

            var issueRequest = new IssueKeyRequestDto
            {
                KulcsId = keyId,
                FoglalasId = bookingId,
                Pin = "1234",
                TeremAllapot = "Rendben"
            };
            var denied = await portasClient.PostAsJsonAsync("/api/key-movements/issue", issueRequest);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            var grant = await adminClient.PostAsJsonAsync(
                $"/api/keys/{keyId}/master-access",
                new { userId = recipientId });
            Assert.Equal(HttpStatusCode.Created, grant.StatusCode);

            issueRequest.Pin = "9999";
            var wrongPin = await portasClient.PostAsJsonAsync("/api/key-movements/issue", issueRequest);
            Assert.Equal(HttpStatusCode.Forbidden, wrongPin.StatusCode);

            issueRequest.Pin = "1234";
            var allowed = await portasClient.PostAsJsonAsync("/api/key-movements/issue", issueRequest);
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        [Fact]
        public async Task OverdueEndpoint_FindsUnreturnedKeyAfterConfiguredDailyClosing()
        {
            var (portasClient, operatorId) = await CreateClientWithRoleAsync("Portas");
            var (_, keyId) = await SeedRoomAndKeyAsync("K-OVERDUE");
            var recipientId = await SeedInstructorAsync();
            var issuedAt = DateTime.UtcNow.AddDays(-2);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Kulcsmozgasok.Add(new Kulcsmozgas
                {
                    KulcsId = keyId,
                    AtvevoFelhasznaloId = recipientId,
                    MozgasTipus = "Kiadás",
                    Idopont = issuedAt,
                    OperátorId = operatorId,
                    CreatedAt = issuedAt
                });
                await db.SaveChangesAsync();
            }

            var overdue = await portasClient.GetFromJsonAsync<List<OverdueKeyDto>>("/api/key-movements/overdue");
            Assert.NotNull(overdue);
            var key = Assert.Single(overdue);
            Assert.Equal(keyId, key.KulcsId);
            Assert.Equal("Teszt oktató", key.BirtokosNev);
            Assert.True(key.KésésPercekben > 0);
        }

        [Fact]
        public async Task AdminCanSetAHashedUserPin()
        {
            var (adminClient, _) = await CreateClientWithRoleAsync("Adminisztrator");
            var userId = await SeedInstructorAsync();

            var response = await adminClient.PutAsJsonAsync(
                $"/api/users/{userId}/pin",
                new { pin = "9876" });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Felhasznalok.FindAsync(userId);
            Assert.NotNull(user?.PinHash);
            Assert.True(BCrypt.Net.BCrypt.Verify("9876", user.PinHash));
        }
    }
}
