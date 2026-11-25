using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Security;
using ProjektLabor.Services;
using BCryptHasher = BCrypt.Net.BCrypt;
using Xunit;

namespace backend.Tests;

public sealed class BusinessRulesRoundTwoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string InstructorPin = "4821";
    private readonly WebApplicationFactory<Program> _factory;

    public BusinessRulesRoundTwoTests(WebApplicationFactory<Program> factory)
    {
        TestJwtConfiguration.ConfigureEnvironment();

        var dbName = $"BusinessRulesRoundTwoDb_{Guid.NewGuid():N}";
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Jwt:Issuer"] = TestJwtConfiguration.Issuer,
                    ["Authentication:Jwt:Audience"] = TestJwtConfiguration.Audience,
                    ["Authentication:Jwt:SigningKey"] = TestJwtConfiguration.SigningKey
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAppDbContextConfiguration();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });
    }

    [Fact]
    public async Task IssueKey_WithWrongPin_ExplainsWhyItFailed()
    {
        var seed = await SeedAsync();
        using var porter = CreateClient(seed.Porter);

        var response = await porter.PostAsJsonAsync("/api/key-movements/issue", new
        {
            kulcsId = seed.KeyId,
            atvevoFelhasznaloId = seed.Instructor.Id,
            pin = "0000",
            teremAllapot = "Rendben"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var message = await ReadMessageAsync(response);
        Assert.StartsWith("Hibás PIN-kód.", message);
        Assert.Contains("Még 4 próbálkozás", message);
    }

    [Fact]
    public async Task ForceReturn_RecordsTheReturnWithoutPin_WithMandatoryReason()
    {
        var seed = await SeedAsync();
        using var porter = CreateClient(seed.Porter);
        var issued = await porter.PostAsJsonAsync("/api/key-movements/issue", new
        {
            kulcsId = seed.KeyId,
            atvevoFelhasznaloId = seed.Instructor.Id,
            pin = InstructorPin,
            teremAllapot = "Rendben"
        });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);

        var withoutReason = await porter.PostAsJsonAsync("/api/key-movements/force-return",
            new { kulcsId = seed.KeyId, teremAllapot = "Rendben", indoklas = " " });
        Assert.Equal(HttpStatusCode.BadRequest, withoutReason.StatusCode);

        using var instructorClient = CreateClient(seed.Instructor);
        var forbidden = await instructorClient.PostAsJsonAsync("/api/key-movements/force-return",
            new { kulcsId = seed.KeyId, teremAllapot = "Rendben", indoklas = "Kolléga hozta vissza" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var returned = await porter.PostAsJsonAsync("/api/key-movements/force-return",
            new { kulcsId = seed.KeyId, teremAllapot = "Rendben", indoklas = "Kolléga hozta vissza" });
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var last = await db.Kulcsmozgasok.Where(movement => movement.KulcsId == seed.KeyId)
            .OrderByDescending(movement => movement.Id).FirstAsync();
        Assert.Equal(KeyMovementTypes.Return, last.MozgasTipus);
        Assert.StartsWith("Kényszerített visszavétel: Kolléga hozta vissza", last.Megjegyzes);
        Assert.True(await db.AuditLogok.AnyAsync(entry =>
            entry.MuveletTipus == "KEY_FORCED_RETURN" && entry.Indoklas == "Kolléga hozta vissza"));
    }

    [Fact]
    public async Task NoShow_ConsecutiveBookingWithHeldKey_IsNotMarked_ButReturnedKeyIs()
    {
        var seed = await SeedAsync();
        var day = AppTime.LocalDate(DateTime.UtcNow).AddDays(-7);
        var firstStart = AppTime.LocalToUtc(day, new TimeOnly(9, 0));
        var secondStart = firstStart.AddMinutes(45);
        int firstId, secondId, otherRoomBookingId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = NewBooking(seed.Instructor.Id, seed.RoomId, firstStart);
            var second = NewBooking(seed.Instructor.Id, seed.RoomId, secondStart);
            // Ugyanebben az időben egy másik teremben, ahol a kulcsot vissza is adta: ez NoShow.
            var otherRoom = NewBooking(seed.Instructor.Id, seed.OtherRoomId, secondStart);
            db.Foglalasok.AddRange(first, second, otherRoom);
            await db.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
            otherRoomBookingId = otherRoom.Id;

            db.Kulcsmozgasok.AddRange(
                new Kulcsmozgas
                {
                    KulcsId = seed.KeyId, FoglalasId = first.Id, TeremId = seed.RoomId, AtvevoFelhasznaloId = seed.Instructor.Id,
                    MozgasTipus = KeyMovementTypes.Issue, Idopont = firstStart.AddMinutes(-10), OperátorId = seed.Porter.Id
                },
                new Kulcsmozgas
                {
                    KulcsId = seed.OtherKeyId, TeremId = seed.OtherRoomId, AtvevoFelhasznaloId = seed.Instructor.Id,
                    MozgasTipus = KeyMovementTypes.Issue, Idopont = firstStart.AddHours(-3), OperátorId = seed.Porter.Id
                },
                new Kulcsmozgas
                {
                    KulcsId = seed.OtherKeyId, TeremId = seed.OtherRoomId, AtvevoFelhasznaloId = seed.Instructor.Id,
                    MozgasTipus = KeyMovementTypes.Return, Idopont = firstStart.AddHours(-2).AddMinutes(-30), OperátorId = seed.Porter.Id
                });
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<NoShowProcessor>();
            await processor.ProcessAsync(secondStart.AddMinutes(40), CancellationToken.None);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var statuses = await db.Foglalasok.ToDictionaryAsync(booking => booking.Id, booking => booking.Status);
            Assert.Equal(BookingRules.ActiveStatus, statuses[firstId]);
            Assert.Equal(BookingRules.ActiveStatus, statuses[secondId]);
            Assert.Equal(BookingRules.NoShowStatus, statuses[otherRoomBookingId]);
        }
    }

    [Fact]
    public async Task Lists_WithHugePageNumber_DoNotOverflow()
    {
        var seed = await SeedAsync();
        using var admin = CreateClient(seed.Admin);

        foreach (var url in new[]
                 {
                     "/api/users?page=2147483647",
                     "/api/bookings?page=2147483647",
                     "/api/key-movements?page=2147483647",
                     "/api/tickets?page=2147483647",
                     "/api/notifications/pending?page=2147483647"
                 })
        {
            var response = await admin.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {response.StatusCode}");
        }

        using var instructor = CreateClient(seed.Instructor);
        var myBookings = await instructor.GetAsync("/api/bookings/my?page=2147483647");
        Assert.Equal(HttpStatusCode.OK, myBookings.StatusCode);
        Assert.Equal("0", myBookings.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task CreateBooking_RejectsWeekendsAndTooDistantDates()
    {
        var seed = await SeedAsync();
        using var instructor = CreateClient(seed.Instructor);

        var saturday = TestTime.LocalToday.AddDays(1);
        while (saturday.DayOfWeek != DayOfWeek.Saturday)
        {
            saturday = saturday.AddDays(1);
        }

        var weekend = await instructor.PostAsJsonAsync("/api/bookings", new
        {
            teremId = seed.RoomId,
            startTime = saturday.AddHours(8).AddMinutes(45),
            endTime = saturday.AddHours(9).AddMinutes(30)
        });
        Assert.Equal(HttpStatusCode.BadRequest, weekend.StatusCode);
        Assert.Equal("Hétvégére nem lehet foglalni.", await ReadMessageAsync(weekend));

        var farAway = TestTime.LocalToday.AddDays(BookingRules.MaxAdvanceDays + 7);
        while (farAway.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            farAway = farAway.AddDays(1);
        }

        var tooFar = await instructor.PostAsJsonAsync("/api/bookings", new
        {
            teremId = seed.RoomId,
            startTime = farAway.AddHours(8).AddMinutes(45),
            endTime = farAway.AddHours(9).AddMinutes(30)
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooFar.StatusCode);
    }

    [Fact]
    public async Task RoomDeactivation_IsBlockedWhileItsKeyIsIssued_AndKeysNeedAnActiveRoom()
    {
        var seed = await SeedAsync();
        using var porter = CreateClient(seed.Porter);
        using var admin = CreateClient(seed.Admin);
        Assert.Equal(HttpStatusCode.Created, (await porter.PostAsJsonAsync("/api/key-movements/issue", new
        {
            kulcsId = seed.KeyId,
            atvevoFelhasznaloId = seed.Instructor.Id,
            pin = InstructorPin,
            teremAllapot = "Rendben"
        })).StatusCode);

        var blocked = await admin.DeleteAsync($"/api/rooms/{seed.RoomId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("kulcsa jelenleg ki van adva", await ReadMessageAsync(blocked));

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/rooms/{seed.OtherRoomId}")).StatusCode);
        var keyForInactiveRoom = await admin.PostAsJsonAsync("/api/keys",
            new { szam = "X-1", tipus = "Osztalykulcs", teremId = seed.OtherRoomId, aktivalva = true });
        Assert.Equal(HttpStatusCode.BadRequest, keyForInactiveRoom.StatusCode);
    }

    [Fact]
    public async Task Maintenance_ValidatesWindow_AndCancelsMarkedConflictsAtomicallyWithNotification()
    {
        var seed = await SeedAsync();
        using var admin = CreateClient(seed.Admin);

        var past = await admin.PostAsJsonAsync("/api/maintenance", new
        {
            teremId = seed.RoomId,
            startTime = DateTime.UtcNow.AddDays(-2),
            endTime = DateTime.UtcNow.AddDays(-1)
        });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        var tooLong = await admin.PostAsJsonAsync("/api/maintenance", new
        {
            teremId = seed.RoomId,
            startTime = DateTime.UtcNow.AddDays(1),
            endTime = DateTime.UtcNow.AddDays(200)
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var day = TestTime.LocalWorkday(3);
        int bookingId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var booking = NewBooking(seed.Instructor.Id, seed.RoomId, AppTime.ToUtc(day.AddHours(10)));
            db.Foglalasok.Add(booking);
            await db.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var request = new
        {
            teremId = seed.RoomId,
            startTime = AppTime.ToUtc(day.AddHours(8)),
            endTime = AppTime.ToUtc(day.AddHours(12)),
            leiras = "Festés"
        };
        var conflict = await admin.PostAsJsonAsync("/api/maintenance", request);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(BookingRules.ActiveStatus, (await db.Foglalasok.SingleAsync(item => item.Id == bookingId)).Status);
        }

        var created = await admin.PostAsJsonAsync("/api/maintenance", new
        {
            request.teremId,
            request.startTime,
            request.endTime,
            request.leiras,
            lemondandoFoglalasIds = new[] { bookingId }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(BookingRules.CancelledStatus, (await db.Foglalasok.SingleAsync(item => item.Id == bookingId)).Status);
            var notification = await db.Ertesitesek.SingleAsync(item => item.FoglalasId == bookingId);
            Assert.Equal(BookingNotifications.CancelledType, notification.ErtesitesTipus);
            Assert.Equal(seed.Instructor.Id, notification.CimzettFelhasznaloId);
            Assert.Contains("karbantartás miatt", notification.Uzenet);
        }

        // Az oktató a saját értesítései között látja a lemondás okát.
        using var instructor = CreateClient(seed.Instructor);
        var mine = await instructor.GetStringAsync("/api/notifications/my");
        Assert.Contains("karbantartás miatt", mine);
    }

    [Fact]
    public async Task AdminCancellation_NotifiesTheInstructor_ButOwnCancellationDoesNot()
    {
        var seed = await SeedAsync();
        int adminCancelled, selfCancelled;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = NewBooking(seed.Instructor.Id, seed.RoomId, AppTime.ToUtc(TestTime.LocalWorkday(4).AddHours(10)));
            var second = NewBooking(seed.Instructor.Id, seed.RoomId, AppTime.ToUtc(TestTime.LocalWorkday(5).AddHours(10)));
            db.Foglalasok.AddRange(first, second);
            await db.SaveChangesAsync();
            adminCancelled = first.Id;
            selfCancelled = second.Id;
        }

        using var admin = CreateClient(seed.Admin);
        using var instructor = CreateClient(seed.Instructor);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/bookings/{adminCancelled}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await instructor.DeleteAsync($"/api/bookings/{selfCancelled}")).StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db2.Ertesitesek.AnyAsync(item =>
            item.FoglalasId == adminCancelled && item.ErtesitesTipus == BookingNotifications.CancelledType));
        Assert.False(await db2.Ertesitesek.AnyAsync(item => item.FoglalasId == selfCancelled));
    }

    [Fact]
    public async Task RoomUtilization_CountsMaintenanceOnlyWithinOpeningHours()
    {
        var seed = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Hétfő 18:00 – kedd 10:00: a foglalható időből csak 2 + 2 óra esik ki (nem 16).
            db.KarbantartasiIdosavok.Add(new KarbantartasiIdosav
            {
                TeremId = seed.RoomId,
                FelhasznaloId = seed.Admin.Id,
                StartTime = AppTime.LocalToUtc(new DateOnly(2026, 10, 12), new TimeOnly(18, 0)),
                EndTime = AppTime.LocalToUtc(new DateOnly(2026, 10, 13), new TimeOnly(10, 0))
            });
            await db.SaveChangesAsync();
        }

        using var admin = CreateClient(seed.Admin);
        var json = await admin.GetStringAsync("/api/reports/room-utilization?kezdet=2026-10-12&veg=2026-10-13");
        using var document = JsonDocument.Parse(json);
        var room = document.RootElement.GetProperty("termek").EnumerateArray()
            .Single(item => item.GetProperty("teremId").GetInt32() == seed.RoomId);
        Assert.Equal(20, room.GetProperty("rendelkezesreAlloOra").GetDouble());
    }

    private static Foglalas NewBooking(int userId, int roomId, DateTime startUtc) => new()
    {
        FelhasznaloId = userId,
        TeremId = roomId,
        StartTime = startUtc,
        EndTime = startUtc.AddMinutes(45),
        Status = BookingRules.ActiveStatus
    };

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    private sealed record Seed(
        Felhasznalo Admin,
        Felhasznalo Instructor,
        Felhasznalo Porter,
        int RoomId,
        int OtherRoomId,
        int KeyId,
        int OtherKeyId);

    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = new Felhasznalo { Username = "admin_b", Email = "admin_b@example.test", Nev = "Admin", Role = RoleNames.Adminisztrator, JelszoHash = "x" };
        var instructor = new Felhasznalo { Username = "oktato_b", Email = "oktato_b@example.test", Nev = "Oktató", Role = RoleNames.Oktato, JelszoHash = "x", PinHash = BCryptHasher.HashPassword(InstructorPin, 4) };
        var porter = new Felhasznalo { Username = "portas_b", Email = "portas_b@example.test", Nev = "Portás", Role = RoleNames.Portas, JelszoHash = "x" };
        var room = new Terem { Nev = "B-201", Kapacitas = 20 };
        var otherRoom = new Terem { Nev = "B-202", Kapacitas = 20 };
        db.AddRange(admin, instructor, porter, room, otherRoom);
        await db.SaveChangesAsync();
        var key = new Kulcs { Szam = "B201", Tipus = "Osztalykulcs", TeremId = room.Id };
        var otherKey = new Kulcs { Szam = "B202", Tipus = "Osztalykulcs", TeremId = otherRoom.Id };
        db.AddRange(key, otherKey);
        await db.SaveChangesAsync();
        return new Seed(admin, instructor, porter, room.Id, otherRoom.Id, key.Id, otherKey.Id);
    }

    private HttpClient CreateClient(Felhasznalo user)
    {
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<AuthService>().GenerateJwtToken(user);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
