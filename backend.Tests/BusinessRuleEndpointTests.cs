using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Bookings;
using ProjektLabor.DTOs.KeyMovements;
using ProjektLabor.DTOs.Users;
using ProjektLabor.Models;
using ProjektLabor.Security;
using ProjektLabor.Services;

namespace backend.Tests;

/// <summary>
/// A foglalás-, kulcs- és felhasználókezelés üzleti szabályainak végponttesztjei.
/// </summary>
public class BusinessRuleEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Pin = "1234";
    private readonly WebApplicationFactory<Program> _factory;

    public BusinessRuleEndpointTests(WebApplicationFactory<Program> factory)
    {
        var dbName = "BusinessRules_" + Guid.NewGuid();
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

                services.RemoveAppDbContextConfiguration();

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });
    }

    // ---------- Foglalások ----------

    [Fact]
    public async Task CreateBooking_InThePast_ReturnsBadRequest()
    {
        var (client, _) = await CreateUserClientAsync(RoleNames.Oktato);
        var roomId = await SeedRoomAsync();
        var start = TestTime.LocalToday.AddDays(-1).AddHours(10).AddMinutes(15);

        var response = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
        {
            TeremId = roomId,
            StartTime = start,
            EndTime = start.AddMinutes(45)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancelBooking_WhenNotActive_ReturnsConflict()
    {
        var (client, userId) = await CreateUserClientAsync(RoleNames.Oktato);
        var roomId = await SeedRoomAsync();
        var bookingId = await SeedBookingAsync(userId, roomId, DateTime.UtcNow.AddDays(-1), BookingRules.NoShowStatus);

        var response = await client.DeleteAsync($"/api/bookings/{bookingId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CancelBooking_AlreadyStarted_IsRejectedForInstructorButAllowedForAdmin()
    {
        var (instructor, userId) = await CreateUserClientAsync(RoleNames.Oktato);
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var roomId = await SeedRoomAsync();
        var bookingId = await SeedBookingAsync(userId, roomId, DateTime.UtcNow.AddMinutes(-10), BookingRules.ActiveStatus);

        var byInstructor = await instructor.DeleteAsync($"/api/bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.BadRequest, byInstructor.StatusCode);

        var byAdmin = await admin.DeleteAsync($"/api/bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
    }

    [Fact]
    public async Task UpdateBooking_AlreadyStarted_CannotBeRescheduled()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (_, instructorId) = await CreateUserClientAsync(RoleNames.Oktato);
        var roomId = await SeedRoomAsync();
        var bookingId = await SeedBookingAsync(instructorId, roomId, DateTime.UtcNow.AddMinutes(-10), BookingRules.ActiveStatus);
        var newStart = TestTime.LocalWorkday(3).AddHours(10).AddMinutes(15);

        var response = await admin.PutAsJsonAsync($"/api/bookings/{bookingId}", new UpdateBookingRequestDto
        {
            StartTime = newStart,
            EndTime = newStart.AddMinutes(45)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Kulcsok ----------

    [Fact]
    public async Task IssueKey_ForBookingStartingInTwoDays_ReturnsBadRequest()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Osztalykulcs");
        var bookingId = await SeedBookingAsync(instructorId, roomId, DateTime.UtcNow.AddDays(2), BookingRules.ActiveStatus);

        var response = await porter.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
        {
            KulcsId = keyId,
            FoglalasId = bookingId,
            Pin = Pin,
            TeremAllapot = "Rendben"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IssueKey_WithoutBooking_IsLinkedToRecipientsCurrentBookingForThatRoom()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        var otherRoomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Osztalykulcs");
        await SeedBookingAsync(instructorId, otherRoomId, DateTime.UtcNow.AddMinutes(30), BookingRules.ActiveStatus);
        var bookingId = await SeedBookingAsync(instructorId, roomId, DateTime.UtcNow.AddMinutes(30), BookingRules.ActiveStatus);

        var response = await porter.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
        {
            KulcsId = keyId,
            AtvevoFelhasznaloId = instructorId,
            Pin = Pin,
            TeremAllapot = "Rendben"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movement = await response.Content.ReadFromJsonAsync<ProjektLabor.DTOs.Keys.KeyMovementItemDto>();
        Assert.Equal(bookingId, movement!.FoglalasId);
    }

    [Fact]
    public async Task IssueKey_WithoutBooking_StaysUnlinkedWhenNoBookingIsDue()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Osztalykulcs");
        await SeedBookingAsync(instructorId, roomId, DateTime.UtcNow.AddDays(2), BookingRules.ActiveStatus);

        var response = await porter.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
        {
            KulcsId = keyId,
            AtvevoFelhasznaloId = instructorId,
            Pin = Pin,
            TeremAllapot = "Rendben"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var movement = await response.Content.ReadFromJsonAsync<ProjektLabor.DTOs.Keys.KeyMovementItemDto>();
        Assert.Null(movement!.FoglalasId);
    }

    [Fact]
    public async Task IssueKey_AfterFiveWrongPins_LocksPinEvenForCorrectPin()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Osztalykulcs");
        var request = new IssueKeyRequestDto
        {
            KulcsId = keyId,
            AtvevoFelhasznaloId = instructorId,
            Pin = "9999",
            TeremAllapot = "Rendben"
        };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await porter.PostAsJsonAsync("/api/key-movements/issue", request);
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        }

        request.Pin = Pin;
        var locked = await porter.PostAsJsonAsync("/api/key-movements/issue", request);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task IssueKey_ToDeactivatedInstructor_ReturnsBadRequest()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync(active: false);
        var roomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Osztalykulcs");

        var response = await porter.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
        {
            KulcsId = keyId,
            AtvevoFelhasznaloId = instructorId,
            Pin = Pin,
            TeremAllapot = "Rendben"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnMasterKey_WithDamage_CreatesTicketForIssuedRoom()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var homeRoomId = await SeedRoomAsync();
        var otherRoomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(homeRoomId, "Mesterkulcs");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.KulcsTermek.AddRange(
                new KulcsTerem { KulcsId = keyId, TeremId = homeRoomId },
                new KulcsTerem { KulcsId = keyId, TeremId = otherRoomId });
            db.MesterkulcsJogosultsagok.Add(new MesterkulcsJogosultsag { KulcsId = keyId, FelhasznaloId = instructorId });
            await db.SaveChangesAsync();
        }

        var issue = await porter.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
        {
            KulcsId = keyId,
            AtvevoFelhasznaloId = instructorId,
            TeremId = otherRoomId,
            Pin = Pin,
            TeremAllapot = "Rendben"
        });
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);

        var returned = await porter.PostAsJsonAsync("/api/key-movements/return", new ReturnKeyRequestDto
        {
            KulcsId = keyId,
            Pin = Pin,
            TeremAllapot = "Sérült",
            SerulesLeiras = "Eltört szék"
        });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ticket = await db.Hibajegyek.SingleAsync(item => item.Leiras == "Eltört szék");
            Assert.Equal(otherRoomId, ticket.TeremId);
        }
    }

    [Fact]
    public async Task ReturnLegacyMasterKey_WithUnknownIssueRoom_RequiresRoomAndUsesIt()
    {
        var (porter, porterId) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var homeRoomId = await SeedRoomAsync();
        var otherRoomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(homeRoomId, "Mesterkulcs");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.KulcsTermek.AddRange(
                new KulcsTerem { KulcsId = keyId, TeremId = homeRoomId },
                new KulcsTerem { KulcsId = keyId, TeremId = otherRoomId });
            // Régi kiadás: foglalás nélkül, a kiadás terme nincs tárolva.
            db.Kulcsmozgasok.Add(new Kulcsmozgas
            {
                KulcsId = keyId,
                AtvevoFelhasznaloId = instructorId,
                OperátorId = porterId,
                MozgasTipus = KeyMovementTypes.Issue,
                Idopont = DateTime.UtcNow.AddHours(-1),
                TeremAllapot = "Rendben"
            });
            await db.SaveChangesAsync();
        }

        var keys = await porter.GetFromJsonAsync<List<ProjektLabor.DTOs.Keys.KeyDto>>("/api/keys");
        Assert.True(Assert.Single(keys!, key => key.Id == keyId).KiadottTeremIsmeretlen);

        var request = new ReturnKeyRequestDto
        {
            KulcsId = keyId,
            Pin = Pin,
            TeremAllapot = "Sérült",
            SerulesLeiras = "Régi kiadás sérülése"
        };
        var withoutRoom = await porter.PostAsJsonAsync("/api/key-movements/return", request);
        Assert.Equal(HttpStatusCode.BadRequest, withoutRoom.StatusCode);

        request.TeremId = otherRoomId;
        var withRoom = await porter.PostAsJsonAsync("/api/key-movements/return", request);
        Assert.Equal(HttpStatusCode.OK, withRoom.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ticket = await db.Hibajegyek.SingleAsync(item => item.Leiras == "Régi kiadás sérülése");
            Assert.Equal(otherRoomId, ticket.TeremId);
        }
    }

    [Fact]
    public async Task IssuedKey_CannotBeDeactivatedOrMovedUntilReturned()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var instructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        var otherRoomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Osztalykulcs");
        var issue = await porter.PostAsJsonAsync("/api/key-movements/issue", new IssueKeyRequestDto
        {
            KulcsId = keyId,
            AtvevoFelhasznaloId = instructorId,
            Pin = Pin,
            TeremAllapot = "Rendben"
        });
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/keys/{keyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync($"/api/keys/{keyId}", new { teremId = otherRoomId })).StatusCode);
        var renamed = await admin.PutAsJsonAsync($"/api/keys/{keyId}", new { szam = $"UJ-{keyId}" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var renamedKey = await renamed.Content.ReadFromJsonAsync<ProjektLabor.DTOs.Keys.KeyDto>();
        Assert.Equal("Kiadva", renamedKey!.JelenlegiAllapot);

        var returned = await porter.PostAsJsonAsync("/api/key-movements/return", new ReturnKeyRequestDto
        {
            KulcsId = keyId,
            Pin = Pin,
            TeremAllapot = "Rendben"
        });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/keys/{keyId}")).StatusCode);
    }

    // ---------- Termek ----------

    [Fact]
    public async Task DeactivateRoom_WithFutureBookings_RequiresExplicitCancellation()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (_, instructorId) = await CreateUserClientAsync(RoleNames.Oktato);
        var roomId = await SeedRoomAsync();
        var futureBookingId = await SeedBookingAsync(instructorId, roomId, DateTime.UtcNow.AddDays(3), BookingRules.ActiveStatus);

        var refused = await admin.DeleteAsync($"/api/rooms/{roomId}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("\"jovobeliFoglalasok\":1", await refused.Content.ReadAsStringAsync());

        var confirmed = await admin.DeleteAsync($"/api/rooms/{roomId}?foglalasokLemondasa=true");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(BookingRules.CancelledStatus, (await db.Foglalasok.SingleAsync(item => item.Id == futureBookingId)).Status);
        Assert.False((await db.Termek.SingleAsync(item => item.Id == roomId)).Aktivalva);
    }

    // ---------- Hibajegyek ----------

    [Fact]
    public async Task Ticket_RequiresKnownCategory_AndFollowsAllowedStatusTransitions()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var roomId = await SeedRoomAsync();

        var unknownCategory = await admin.PostAsJsonAsync("/api/tickets",
            new { teremId = roomId, kategoria = "Ismeretlen", leiras = "Teszt" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownCategory.StatusCode);

        var created = await admin.PostAsJsonAsync("/api/tickets",
            new { teremId = roomId, kategoria = "technikai hiba", leiras = "Nem működik a projektor" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ticket = await created.Content.ReadFromJsonAsync<ProjektLabor.DTOs.Tickets.TicketDto>();
        Assert.Equal("Technikai hiba", ticket!.Kategoria);

        async Task<HttpStatusCode> SetStatus(string statusz) =>
            (await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { statusz })).StatusCode;

        Assert.Equal(HttpStatusCode.OK, await SetStatus("Folyamatban"));
        Assert.Equal(HttpStatusCode.Conflict, await SetStatus("Folyamatban"));
        Assert.Equal(HttpStatusCode.OK, await SetStatus("Zarva"));
        Assert.Equal(HttpStatusCode.Conflict, await SetStatus("Nyitva"));
    }

    // ---------- Felhasználók ----------

    [Fact]
    public async Task DeleteUser_DeactivatesInsteadOfRemoving_AndBlocksLogin()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (_, instructorId) = await CreateUserClientAsync(RoleNames.Oktato);
        string username;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            username = (await db.Felhasznalok.SingleAsync(user => user.Id == instructorId)).Username;
        }

        var response = await admin.DeleteAsync($"/api/users/{instructorId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await admin.GetFromJsonAsync<UserDto>($"/api/users/{instructorId}");
        Assert.NotNull(user);
        Assert.False(user.Aktiv);

        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new { username, jelszo = "TestPass123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var reactivate = await admin.PostAsync($"/api/users/{instructorId}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_Self_ReturnsBadRequest()
    {
        var (admin, adminId) = await CreateUserClientAsync(RoleNames.Adminisztrator);

        var response = await admin.DeleteAsync($"/api/users/{adminId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_OfSelf_ReturnsBadRequest()
    {
        var (admin, adminId) = await CreateUserClientAsync(RoleNames.Adminisztrator);

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{adminId}/role",
            new ChangeRoleRequestDto { UjRole = RoleNames.Oktato });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_OfDeactivatedOrDemotedUser_IsRejectedImmediately()
    {
        var (deactivated, deactivatedId) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (demoted, demotedId) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        Assert.Equal(HttpStatusCode.OK, (await deactivated.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await demoted.GetAsync("/api/users")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Felhasznalok.SingleAsync(user => user.Id == deactivatedId)).Aktiv = false;
            (await db.Felhasznalok.SingleAsync(user => user.Id == demotedId)).Role = RoleNames.Oktato;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await deactivated.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await demoted.GetAsync("/api/users")).StatusCode);
    }

    // ---------- Megbízhatóság ----------

    [Fact]
    public async Task ReliabilityList_ShowsActiveInstructorsWorstFirst_ForAdminOnly()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (instructorClient, _) = await CreateUserClientAsync(RoleNames.Oktato);
        var worstId = await SeedInstructorWithPinAsync();
        var cleanId = await SeedInstructorWithPinAsync();
        var inactiveId = await SeedInstructorWithPinAsync(active: false);
        var now = DateTime.UtcNow;
        var startYear = now.Month >= 9 ? now.Year : now.Year - 1;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // 40 × 3 = 120 pont, ami 100-nál megáll.
            for (var i = 0; i < 40; i++)
            {
                db.MegbizhatosagiEsemenyek.Add(new MegbizhatosagiEsemeny
                {
                    FelhasznaloId = worstId,
                    EsemenyTipus = "LeNemAdott",
                    Pont = 3,
                    Tanev = $"{startYear}/{startYear + 1}",
                    EsemenyIdopontja = now.AddDays(-i)
                });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await instructorClient.GetAsync("/api/reliability/users")).StatusCode);

        var list = await admin.GetFromJsonAsync<ProjektLabor.Endpoints.ReliabilityEndpoints.ReliabilityListResponse>(
            "/api/reliability/users");
        Assert.NotNull(list);
        Assert.Equal(worstId, list.Oktatok[0].UserId);
        Assert.Equal(100, list.Oktatok[0].Pontszam);
        Assert.Equal(40, list.Oktatok[0].EsemenyekSzama);
        Assert.Equal(0, Assert.Single(list.Oktatok, item => item.UserId == cleanId).Pontszam);
        Assert.DoesNotContain(list.Oktatok, item => item.UserId == inactiveId);
    }

    // ---------- Bemenet-ellenőrzés ----------

    [Fact]
    public async Task InvalidInput_IsRejectedWithBadRequest_InsteadOfReachingTheDatabase()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (instructor, _) = await CreateUserClientAsync(RoleNames.Oktato);
        var roomId = await SeedRoomAsync();

        var tooLongUsername = await admin.PostAsJsonAsync("/api/users", new
        {
            username = new string('x', 60),
            email = "valid@test.hu",
            jelszo = "Hosszujelszo1!",
            nev = "Teszt",
            role = RoleNames.Oktato
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLongUsername.StatusCode);
        Assert.Contains("50", await tooLongUsername.Content.ReadAsStringAsync());

        var invalidEmail = await admin.PostAsJsonAsync("/api/users", new
        {
            username = "ervenyes.nev",
            email = "nem-email",
            jelszo = "Hosszujelszo1!",
            nev = "Teszt",
            role = RoleNames.Oktato
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);

        var start = TestTime.LocalWorkday(5).AddHours(10).AddMinutes(15);
        var tooLongNeeds = await instructor.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
        {
            TeremId = roomId,
            StartTime = start,
            EndTime = start.AddMinutes(45),
            SpecialisIgenyek = new string('a', 501)
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLongNeeds.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Felhasznalok.AnyAsync(user => user.Email == "nem-email" || user.Username.Length > 50));
        Assert.False(await db.Foglalasok.AnyAsync(booking => booking.TeremId == roomId));
    }

    // ---------- Riportok, értesítések, paraméterek ----------

    [Fact]
    public async Task DailyReport_CountsUnreturnedKeyPenalties_IssuedRoomAndOnlyRealBookings()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (_, porterId) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var homeRoomId = await SeedRoomAsync();
        var otherRoomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(homeRoomId, "Mesterkulcs");
        var day = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Kulcsmozgasok.Add(new Kulcsmozgas
            {
                KulcsId = keyId, TeremId = otherRoomId, AtvevoFelhasznaloId = instructorId,
                OperátorId = porterId, MozgasTipus = KeyMovementTypes.Issue, Idopont = day.AddHours(8)
            });
            db.MegbizhatosagiEsemenyek.Add(new MegbizhatosagiEsemeny
            {
                FelhasznaloId = instructorId, KulcsId = keyId, EsemenyTipus = "LeNemAdott", Pont = 3,
                Automatikus = true, Tanev = "2026/2027", EsemenyIdopontja = day.AddHours(20)
            });
            db.Foglalasok.AddRange(
                new Foglalas { FelhasznaloId = instructorId, TeremId = otherRoomId, Status = BookingRules.ActiveStatus,
                    StartTime = day.AddHours(9), EndTime = day.AddHours(10) },
                new Foglalas { FelhasznaloId = instructorId, TeremId = otherRoomId, Status = BookingRules.CancelledStatus,
                    StartTime = day.AddHours(11), EndTime = day.AddHours(12) });
            await db.SaveChangesAsync();
        }

        var report = await admin.GetFromJsonAsync<ProjektLabor.Endpoints.ReportEndpoints.DailyReportResponse>(
            "/api/reports/daily?datum=2026-10-05");

        Assert.NotNull(report);
        Assert.Equal(1, report.OsszesKesedelmesFoglalas);
        var other = Assert.Single(report.TermenkintiForgalom, room => room.TeremId == otherRoomId);
        Assert.Equal(1, other.Foglalasok);
        Assert.Equal(1, other.Kulcskiadasok);
        Assert.Equal(0, Assert.Single(report.TermenkintiForgalom, room => room.TeremId == homeRoomId).Kulcskiadasok);
    }

    [Fact]
    public async Task OverdueKeys_UseDailyClosingDeadline_NotBookingEnd()
    {
        var (porter, porterId) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Osztalykulcs");
        // A foglalás már véget ért, de a napi zárás (22:00) még hátravan / a következő napra esik.
        var bookingId = await SeedBookingAsync(instructorId, roomId, DateTime.UtcNow.AddMinutes(-50), BookingRules.ActiveStatus);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Kulcsmozgasok.Add(new Kulcsmozgas
            {
                KulcsId = keyId, FoglalasId = bookingId, TeremId = roomId, AtvevoFelhasznaloId = instructorId,
                OperátorId = porterId, MozgasTipus = KeyMovementTypes.Issue, Idopont = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var overdue = await porter.GetFromJsonAsync<List<OverdueKeyDto>>("/api/key-movements/overdue");

        Assert.DoesNotContain(overdue!, item => item.KulcsId == keyId);
    }

    [Fact]
    public async Task PorterNotifications_ShowIssuedRoomForMasterKey_AreCountedAndListedAsPending()
    {
        var (porter, porterId) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var homeRoomId = await SeedRoomAsync();
        var otherRoomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(homeRoomId, "Mesterkulcs");
        string otherRoomName;
        int notificationId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            otherRoomName = (await db.Termek.SingleAsync(room => room.Id == otherRoomId)).Nev;
            var movement = new Kulcsmozgas
            {
                KulcsId = keyId, TeremId = otherRoomId, AtvevoFelhasznaloId = instructorId,
                OperátorId = porterId, MozgasTipus = KeyMovementTypes.Issue, Idopont = DateTime.UtcNow.AddDays(-1)
            };
            db.Kulcsmozgasok.Add(movement);
            await db.SaveChangesAsync();
            var notification = new Ertesites
            {
                CimzettFelhasznaloId = porterId, KulcsmozgasId = movement.Id, ErtesitesTipus = "EveningKeyReturn",
                IdoPont = DateTime.UtcNow, Allapot = "Kesz", CreatedAt = DateTime.UtcNow
            };
            db.Ertesitesek.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }

        var response = await porter.GetAsync("/api/notifications/my?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Total-Count")));
        var mine = await response.Content.ReadFromJsonAsync<List<ProjektLabor.Endpoints.NotificationEndpoints.NotificationResponse>>();
        Assert.Equal(otherRoomName, Assert.Single(mine!).TeremNev);

        var pending = await porter.GetFromJsonAsync<List<ProjektLabor.Endpoints.NotificationEndpoints.NotificationResponse>>(
            "/api/notifications/pending");
        Assert.Contains(pending!, item => item.Id == notificationId);
    }

    [Theory]
    [InlineData("napi_kulcsleadasi_zarasi_idopont", "19:00")]
    [InlineData("no_show_idokorlat_perc", "60")]
    public async Task Parameters_RejectUnreasonableValues(string key, string value)
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Rendszerparameterek.AnyAsync(item => item.Kulcs == key))
            {
                db.Rendszerparameterek.Add(new Rendszerparameter { Kulcs = key, Ertek = key.StartsWith("napi") ? "22:00" : "15" });
                await db.SaveChangesAsync();
            }
        }

        var response = await admin.PutAsJsonAsync($"/api/parameters/{key}", new { ertek = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Kitti: üzleti szabályok (2. kör) ----------

    [Fact]
    public async Task RoomUpdate_CannotDeactivateRoomWithFutureBookings()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var instructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        await SeedBookingAsync(instructorId, roomId, DateTime.UtcNow.AddDays(2), BookingRules.ActiveStatus);

        var response = await admin.PutAsJsonAsync($"/api/rooms/{roomId}", new { aktivalva = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SettingNewPin_ClearsPinLockout()
    {
        var (porter, _) = await CreateUserClientAsync(RoleNames.Portas);
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var instructorId = await SeedInstructorWithPinAsync();
        var keyId = await SeedKeyAsync(await SeedRoomAsync(), "Osztalykulcs");
        var request = new IssueKeyRequestDto { KulcsId = keyId, AtvevoFelhasznaloId = instructorId, Pin = "9999", TeremAllapot = "Rendben" };
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await porter.PostAsJsonAsync("/api/key-movements/issue", request);
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync($"/api/users/{instructorId}/pin", new { pin = "5678" })).StatusCode);

        request.Pin = "5678";
        Assert.Equal(HttpStatusCode.Created, (await porter.PostAsJsonAsync("/api/key-movements/issue", request)).StatusCode);
    }

    [Fact]
    public async Task LeadingSpaces_InUsernameOrKeyNumber_GiveConflictInsteadOfServerError()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var roomId = await SeedRoomAsync();
        var first = await admin.PostAsJsonAsync("/api/users", new
        {
            username = "szokoz.teszt", email = "szokoz@test.hu", jelszo = "Hosszujelszo1!", nev = "Szóköz", role = RoleNames.Oktato
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var duplicateUser = await admin.PostAsJsonAsync("/api/users", new
        {
            username = " szokoz.teszt", email = " Szokoz@test.hu", jelszo = "Hosszujelszo1!", nev = "Szóköz", role = RoleNames.Oktato
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateUser.StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync("/api/keys", new { szam = "SZ-1", tipus = "Osztalykulcs", teremId = roomId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/keys", new { szam = " SZ-1", tipus = "Osztalykulcs", teremId = roomId })).StatusCode);
    }

    [Fact]
    public async Task UserPasswords_FollowTheTwelveCharacterPolicy()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);

        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            username = "rovid.jelszo", email = "rovid@test.hu", jelszo = "Rovid1234!", nev = "Rövid", role = RoleNames.Oktato
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task KeyStatus_ShowsIssuedForAKeyDeactivatedWhileIssued()
    {
        var (porter, porterId) = await CreateUserClientAsync(RoleNames.Portas);
        var instructorId = await SeedInstructorWithPinAsync();
        var keyId = await SeedKeyAsync(await SeedRoomAsync(), "Osztalykulcs");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Kulcsmozgasok.Add(new Kulcsmozgas
            {
                KulcsId = keyId, AtvevoFelhasznaloId = instructorId, OperátorId = porterId,
                MozgasTipus = KeyMovementTypes.Issue, Idopont = DateTime.UtcNow.AddHours(-1)
            });
            (await db.Kulcsok.SingleAsync(key => key.Id == keyId)).Aktivalva = false;
            await db.SaveChangesAsync();
        }

        var status = await porter.GetFromJsonAsync<ProjektLabor.DTOs.Keys.KeyStatusDto>($"/api/keys/{keyId}/status");

        Assert.Equal("Kiadva", status!.Allapot);
        Assert.NotNull(status.AktualisBirtokosNev);
    }

    [Fact]
    public async Task AdminBooking_MustTargetAnInstructor()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var (_, porterId) = await CreateUserClientAsync(RoleNames.Portas);
        var roomId = await SeedRoomAsync();
        var start = TestTime.LocalWorkday(4).AddHours(10).AddMinutes(15);

        var response = await admin.PostAsJsonAsync("/api/bookings", new CreateBookingRequestDto
        {
            TeremId = roomId, FelhasznaloId = porterId, StartTime = start, EndTime = start.AddMinutes(45)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RoleChange_FromInstructor_RequiresNoActiveBookings_AndRemovesMasterKeyGrants()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var busyInstructorId = await SeedInstructorWithPinAsync();
        var freeInstructorId = await SeedInstructorWithPinAsync();
        var roomId = await SeedRoomAsync();
        var keyId = await SeedKeyAsync(roomId, "Mesterkulcs");
        await SeedBookingAsync(busyInstructorId, roomId, DateTime.UtcNow.AddDays(2), BookingRules.ActiveStatus);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MesterkulcsJogosultsagok.Add(new MesterkulcsJogosultsag { KulcsId = keyId, FelhasznaloId = freeInstructorId });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(
            $"/api/users/{busyInstructorId}/role", new ChangeRoleRequestDto { UjRole = RoleNames.Portas })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
            $"/api/users/{freeInstructorId}/role", new ChangeRoleRequestDto { UjRole = RoleNames.Portas })).StatusCode);

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.MesterkulcsJogosultsagok.AnyAsync(access => access.FelhasznaloId == freeInstructorId));
    }

    [Fact]
    public async Task MasterAccess_CannotBeGrantedToInactiveInstructor()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var inactiveInstructorId = await SeedInstructorWithPinAsync(active: false);
        var keyId = await SeedKeyAsync(await SeedRoomAsync(), "Mesterkulcs");

        var response = await admin.PostAsJsonAsync($"/api/keys/{keyId}/master-access", new { userId = inactiveInstructorId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_RejectsDeactivatedRoomAndOverlappingWindows()
    {
        var (admin, _) = await CreateUserClientAsync(RoleNames.Adminisztrator);
        var roomId = await SeedRoomAsync();
        var inactiveRoomId = await SeedRoomAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Termek.SingleAsync(room => room.Id == inactiveRoomId)).Aktivalva = false;
            await db.SaveChangesAsync();
        }
        var start = TestTime.LocalWorkday(10).AddHours(8);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/maintenance",
            new { teremId = inactiveRoomId, startTime = start, endTime = start.AddHours(2), leiras = "Festés" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/maintenance",
            new { teremId = roomId, startTime = start, endTime = start.AddHours(2), leiras = "Festés" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/maintenance",
            new { teremId = roomId, startTime = start.AddHours(1), endTime = start.AddHours(3), leiras = "Átfedő" })).StatusCode);
    }

    // ---------- Segédfüggvények ----------

    private async Task<(HttpClient Client, int UserId)> CreateUserClientAsync(string role, bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new Felhasznalo
        {
            Username = $"br_{suffix}",
            Nev = $"Teszt {role}",
            Email = $"br_{suffix}@test.hu",
            Role = role,
            Aktiv = active,
            JelszoHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!", workFactor: 4)
        };
        db.Felhasznalok.Add(user);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.GenerateJwtToken(user));
        return (client, user.Id);
    }

    private async Task<int> SeedInstructorWithPinAsync(bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var user = new Felhasznalo
        {
            Username = $"pin_{suffix}",
            Nev = "PIN oktató",
            Email = $"pin_{suffix}@test.hu",
            Role = RoleNames.Oktato,
            Aktiv = active,
            JelszoHash = "x",
            PinHash = BCrypt.Net.BCrypt.HashPassword(Pin, workFactor: 4)
        };
        db.Felhasznalok.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> SeedRoomAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = new Terem { Nev = $"Terem {Guid.NewGuid():N}"[..20], Kapacitas = 20, Aktivalva = true };
        db.Termek.Add(room);
        await db.SaveChangesAsync();
        return room.Id;
    }

    private async Task<int> SeedKeyAsync(int roomId, string type)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = new Kulcs { Szam = $"K-{Guid.NewGuid():N}"[..12], Tipus = type, Aktivalva = true, TeremId = roomId };
        db.Kulcsok.Add(key);
        await db.SaveChangesAsync();
        return key.Id;
    }

    private async Task<int> SeedBookingAsync(int userId, int roomId, DateTime startUtc, string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = new Foglalas
        {
            FelhasznaloId = userId,
            TeremId = roomId,
            StartTime = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            EndTime = DateTime.SpecifyKind(startUtc.AddMinutes(45), DateTimeKind.Utc),
            Status = status
        };
        db.Foglalasok.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }
}
