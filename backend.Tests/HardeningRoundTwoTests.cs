using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Users;
using ProjektLabor.Models;
using ProjektLabor.Security;
using ProjektLabor.Services;
using BCryptHasher = BCrypt.Net.BCrypt;
using Xunit;

namespace backend.Tests;

public sealed class HardeningRoundTwoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string InstructorPassword = "Oktato-jelszo-2026";
    private readonly WebApplicationFactory<Program> _factory;

    public HardeningRoundTwoTests(WebApplicationFactory<Program> factory)
    {
        TestJwtConfiguration.ConfigureEnvironment();

        var dbName = $"HardeningRoundTwoDb_{Guid.NewGuid():N}";
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
                var descriptor = services.SingleOrDefault(
                    service => service.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAppDbContextConfiguration();

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });
    }

    [Fact]
    public async Task Instructor_CanSetOwnPin_OnlyWithCurrentPassword_AndAdminSeesHasPin()
    {
        var (admin, instructor, porter) = await SeedUsersAsync();
        using var instructorClient = CreateClient(instructor);

        var wrongPassword = await instructorClient.PutAsJsonAsync("/api/auth/pin",
            new { jelenlegiJelszo = "Rossz-jelszo-2026", pin = "4821" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);

        var invalidPin = await instructorClient.PutAsJsonAsync("/api/auth/pin",
            new { jelenlegiJelszo = InstructorPassword, pin = "12a4" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPin.StatusCode);

        using var adminClient = CreateClient(admin);
        var before = await adminClient.GetFromJsonAsync<UserDto>($"/api/users/{instructor.Id}");
        Assert.False(before!.HasPin);

        var ok = await instructorClient.PutAsJsonAsync("/api/auth/pin",
            new { jelenlegiJelszo = InstructorPassword, pin = "4821" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var after = await adminClient.GetFromJsonAsync<UserDto>($"/api/users/{instructor.Id}");
        Assert.True(after!.HasPin);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Felhasznalok.SingleAsync(user => user.Id == instructor.Id);
            Assert.True(BCryptHasher.Verify("4821", stored.PinHash));
        }

        // A PIN csak az oktatóké (kulcsot csak oktató vehet át).
        using var porterClient = CreateClient(porter);
        var forbidden = await porterClient.PutAsJsonAsync("/api/auth/pin",
            new { jelenlegiJelszo = InstructorPassword, pin = "4821" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task AdminPasswordReset_InvalidatesTheUsersExistingTokens()
    {
        var (admin, instructor, _) = await SeedUsersAsync();
        using var instructorClient = CreateClient(instructor);
        Assert.Equal(HttpStatusCode.OK, (await instructorClient.GetAsync("/api/auth/me")).StatusCode);

        using var adminClient = CreateClient(admin);
        var reset = await adminClient.PutAsJsonAsync($"/api/users/{instructor.Id}",
            new { ujJelszo = "Uj-oktato-jelszo-2026" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await instructorClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RoomNames_MustBeUnique_IgnoringCase()
    {
        var (admin, _, _) = await SeedUsersAsync();
        using var client = CreateClient(admin);

        var first = await client.PostAsJsonAsync("/api/rooms", new { nev = "C-201", kapacitas = 20 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var duplicate = await client.PostAsJsonAsync("/api/rooms", new { nev = " c-201 ", kapacitas = 10 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var second = await client.PostAsJsonAsync("/api/rooms", new { nev = "C-202", kapacitas = 20 });
        var secondId = (await second.Content.ReadFromJsonAsync<RoomIdResponse>())!.Id;
        var rename = await client.PutAsJsonAsync($"/api/rooms/{secondId}", new { nev = "C-201" });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
    }

    [Fact]
    public async Task RoomCapacity_UpperLimit_AppliesOnEditToo()
    {
        var (admin, _, _) = await SeedUsersAsync();
        using var client = CreateClient(admin);

        var tooBig = await client.PostAsJsonAsync("/api/rooms", new { nev = "D-101", kapacitas = 10_001 });
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);

        var created = await client.PostAsJsonAsync("/api/rooms", new { nev = "D-101", kapacitas = 10_000 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var roomId = (await created.Content.ReadFromJsonAsync<RoomIdResponse>())!.Id;

        var edit = await client.PutAsJsonAsync($"/api/rooms/{roomId}", new { kapacitas = 50_000 });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
    }

    [Fact]
    public async Task BookingSuspension_BlocksNewBookings_UntilLifted()
    {
        var (admin, instructor, _) = await SeedUsersAsync();
        using var adminClient = CreateClient(admin);

        var missingReason = await adminClient.PutAsJsonAsync($"/api/users/{instructor.Id}/booking-suspension",
            new { eddig = DateTime.UtcNow.AddDays(7) });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var suspend = await adminClient.PutAsJsonAsync($"/api/users/{instructor.Id}/booking-suspension",
            new { eddig = DateTime.UtcNow.AddDays(7), indoklas = "Ismételt no-show" });
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        var room = await adminClient.PostAsJsonAsync("/api/rooms", new { nev = "E-101", kapacitas = 20 });
        var roomId = (await room.Content.ReadFromJsonAsync<RoomIdResponse>())!.Id;
        var start = TestTime.LocalWorkday(3).AddHours(10).AddMinutes(15);

        using var instructorClient = CreateClient(instructor);
        var blocked = await instructorClient.PostAsJsonAsync("/api/bookings",
            new { teremId = roomId, startTime = start, endTime = start.AddMinutes(90) });
        Assert.True(blocked.StatusCode == HttpStatusCode.Forbidden, await blocked.Content.ReadAsStringAsync());
        Assert.Contains("fel van függesztve", await blocked.Content.ReadAsStringAsync());

        // Rossz időpontnál is a felfüggesztés okát kapja vissza, nem az időszelet-hibát.
        var misaligned = await instructorClient.PostAsJsonAsync("/api/bookings",
            new { teremId = roomId, startTime = start.AddMinutes(7), endTime = start.AddMinutes(60) });
        Assert.Equal(HttpStatusCode.Forbidden, misaligned.StatusCode);

        var lift = await adminClient.PutAsJsonAsync($"/api/users/{instructor.Id}/booking-suspension",
            new { eddig = (DateTime?)null });
        Assert.Equal(HttpStatusCode.OK, lift.StatusCode);

        var allowed = await instructorClient.PostAsJsonAsync("/api/bookings",
            new { teremId = roomId, startTime = start, endTime = start.AddMinutes(90) });
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task SessionRefresh_ExtendsToken_ButNotBeyondMaxSession()
    {
        var (_, instructor, _) = await SeedUsersAsync();
        using var client = CreateClient(instructor);

        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var body = await refresh.Content.ReadFromJsonAsync<SessionRefreshResponse>();
        Assert.True(body!.ExpiresAt > DateTime.UtcNow.AddHours(7));
        Assert.True(body.ExpiresAt <= body.SessionEndsAt);
        Assert.Contains(refresh.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase));

        // A bejelentkezés óta eltelt idő meghaladja a munkamenet felső határát: nincs megújítás.
        using var scope = _factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        var oldToken = auth.GenerateJwtToken(instructor, DateTime.UtcNow.AddHours(-20), DateTime.UtcNow.AddMinutes(30));
        using var oldClient = _factory.CreateClient();
        oldClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        var expired = await oldClient.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task OpenTicketFilter_CountsOnlyOpenAndInProgressTickets()
    {
        var (admin, instructor, _) = await SeedUsersAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var room = new Terem { Nev = $"T-{Guid.NewGuid():N}"[..10], Kapacitas = 10, Aktivalva = true };
            db.Termek.Add(room);
            await db.SaveChangesAsync();
            foreach (var status in new[] { "Nyitva", "Folyamatban", "Megoldva", "Zarva", "Nyitva" })
            {
                db.Hibajegyek.Add(new Hibajegy { TeremId = room.Id, FelhasznaloId = instructor.Id, Kategoria = "Egyeb", Leiras = "teszt", Statusz = status });
            }
            await db.SaveChangesAsync();
        }

        using var client = CreateClient(admin);
        var open = await client.GetFromJsonAsync<TicketCountResponse>("/api/tickets?nyitott=true&page=1&pageSize=1");
        Assert.Equal(3, open!.TotalCount);
        var closed = await client.GetFromJsonAsync<TicketCountResponse>("/api/tickets?nyitott=false&page=1&pageSize=1");
        Assert.Equal(2, closed!.TotalCount);
    }

    [Fact]
    public async Task ReliabilityCorrection_RejectsOutOfRangePoints()
    {
        var (admin, instructor, _) = await SeedUsersAsync();
        using var client = CreateClient(admin);

        foreach (var points in new[] { 101, -101, int.MaxValue, int.MinValue })
        {
            var response = await client.PostAsJsonAsync("/api/reliability/correction",
                new { userId = instructor.Id, pont = points, indoklas = "Teszt" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var valid = await client.PostAsJsonAsync("/api/reliability/correction",
            new { userId = instructor.Id, pont = 100, indoklas = "Teszt" });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Theory]
    [InlineData("rossz név")]
    [InlineData("ékezetes")]
    [InlineData("ab")]
    [InlineData("<script>")]
    public async Task CreateUser_RejectsInvalidUsernames(string username)
    {
        var (admin, _, _) = await SeedUsersAsync();
        using var client = CreateClient(admin);

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            username,
            email = "uj@example.test",
            jelszo = "Eros-jelszo-2026",
            nev = "Új Felhasználó",
            role = RoleNames.Oktato
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void AcademicYear_SwitchesAtLocalMidnightOnSeptemberFirst()
    {
        // 2026-08-31 22:30 UTC = 2026-09-01 00:30 Budapest: már az új tanév.
        Assert.Equal("2026/2027", AppTime.AcademicYear(new DateTime(2026, 8, 31, 22, 30, 0, DateTimeKind.Utc)));
        Assert.Equal("2025/2026", AppTime.AcademicYear(new DateTime(2026, 8, 31, 21, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task ExecuteWithRetry_RetriesLockTimeouts_AndDiscardsChangesOfTheFailedAttempt()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"RetryTestDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var attempts = 0;

        var result = await SqlServerLocking.ExecuteWithRetryAsync(db, async () =>
        {
            attempts++;
            db.Termek.Add(new Terem { Nev = $"Próba {attempts}", Kapacitas = 10 });
            if (attempts < SqlServerLocking.MaxAttempts)
            {
                throw new DatabaseLockTimeoutException("busy");
            }

            await db.SaveChangesAsync();
            return attempts;
        });

        Assert.Equal(SqlServerLocking.MaxAttempts, result);
        // Csak a sikeres próbálkozás terme került mentésre.
        Assert.Equal([$"Próba {SqlServerLocking.MaxAttempts}"], await db.Termek.Select(room => room.Nev).ToListAsync());

        await Assert.ThrowsAsync<DatabaseLockTimeoutException>(() =>
            SqlServerLocking.ExecuteWithRetryAsync<int>(db, () => throw new DatabaseLockTimeoutException("busy")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqlServerLocking.ExecuteWithRetryAsync<int>(db, () => throw new InvalidOperationException("not transient")));
    }

    [Fact]
    public async Task SampleDataCleanup_RemovesUntouchedSampleData_ButKeepsUsedSampleKeys()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SampleCleanupDb_{Guid.NewGuid():N}")
            .Options;
        await using var db = new AppDbContext(options);
        var instructor = new Felhasznalo { Username = "radli.richard", Email = "r@example.test", Nev = "R", Role = RoleNames.Oktato };
        var porter = new Felhasznalo { Username = "portas", Email = "p@example.test", Nev = "P", Role = RoleNames.Portas };
        var sampleRoom = new Terem { Nev = "B-101", Kapacitas = 30, Leiras = SampleDataCleanup.SampleRoomDescription };
        var realRoom = new Terem { Nev = "A-001", Kapacitas = 30, Leiras = "Valódi terem" };
        db.AddRange(instructor, porter, sampleRoom, realRoom);
        await db.SaveChangesAsync();

        var sampleKey = new Kulcs { Szam = "MINTA-B101", Tipus = "Osztalykulcs", TeremId = sampleRoom.Id };
        var realKey = new Kulcs { Szam = "A-001", Tipus = "Osztalykulcs", TeremId = realRoom.Id };
        db.AddRange(sampleKey, realKey);
        await db.SaveChangesAsync();

        var booking = new Foglalas
        {
            FelhasznaloId = instructor.Id,
            TeremId = sampleRoom.Id,
            StartTime = DateTime.UtcNow.AddDays(-3),
            EndTime = DateTime.UtcNow.AddDays(-3).AddHours(1),
            Status = "Kesz"
        };
        db.Add(booking);
        await db.SaveChangesAsync();
        var issue = new Kulcsmozgas
        {
            KulcsId = sampleKey.Id, FoglalasId = booking.Id, TeremId = sampleRoom.Id, AtvevoFelhasznaloId = instructor.Id,
            MozgasTipus = KeyMovementTypes.Issue, Idopont = DateTime.UtcNow.AddDays(-3), OperátorId = porter.Id, Megjegyzes = "Mintaadat"
        };
        var realIssue = new Kulcsmozgas
        {
            KulcsId = realKey.Id, TeremId = realRoom.Id, AtvevoFelhasznaloId = instructor.Id,
            MozgasTipus = KeyMovementTypes.Issue, Idopont = DateTime.UtcNow.AddDays(-1), OperátorId = porter.Id
        };
        db.AddRange(issue, realIssue);
        await db.SaveChangesAsync();
        db.Add(new MegbizhatosagiEsemeny
        {
            FelhasznaloId = instructor.Id, KulcsId = sampleKey.Id, FoglalasId = booking.Id, KiadasiKulcsmozgasId = issue.Id,
            EsemenyTipus = "LeNemAdott", Pont = 3, Automatikus = true, EsemenyIdopontja = DateTime.UtcNow, Tanev = "2026/2027"
        });
        await db.SaveChangesAsync();

        await SampleDataCleanup.RemoveUntouchedSampleDataAsync(db, NullLogger.Instance);

        Assert.False(await db.Kulcsok.AnyAsync(key => key.Szam == "MINTA-B101"));
        Assert.False(await db.Termek.AnyAsync(room => room.Nev == "B-101"));
        Assert.False(await db.Foglalasok.AnyAsync());
        Assert.False(await db.MegbizhatosagiEsemenyek.AnyAsync());
        Assert.Equal([realIssue.Id], await db.Kulcsmozgasok.Select(movement => movement.Id).ToListAsync());
        Assert.True(await db.Kulcsok.AnyAsync(key => key.Szam == "A-001"));

        // Ha egy mintakulcsot valódi kiadásra használtak, az adatot nem töröljük.
        var usedRoom = new Terem { Nev = "B-102", Kapacitas = 24, Leiras = SampleDataCleanup.SampleRoomDescription };
        db.Add(usedRoom);
        await db.SaveChangesAsync();
        var usedKey = new Kulcs { Szam = "MINTA-B102", Tipus = "Osztalykulcs", TeremId = usedRoom.Id };
        db.Add(usedKey);
        await db.SaveChangesAsync();
        db.Add(new Kulcsmozgas
        {
            KulcsId = usedKey.Id, TeremId = usedRoom.Id, AtvevoFelhasznaloId = instructor.Id,
            MozgasTipus = KeyMovementTypes.Issue, Idopont = DateTime.UtcNow, OperátorId = porter.Id
        });
        await db.SaveChangesAsync();

        await SampleDataCleanup.RemoveUntouchedSampleDataAsync(db, NullLogger.Instance);

        Assert.True(await db.Kulcsok.AnyAsync(key => key.Szam == "MINTA-B102"));
        Assert.True(await db.Termek.AnyAsync(room => room.Nev == "B-102"));
    }

    private async Task<(Felhasznalo Admin, Felhasznalo Instructor, Felhasznalo Porter)> SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Felhasznalok.FirstOrDefaultAsync(user => user.Username == "admin_r2");
        if (admin is null)
        {
            admin = new Felhasznalo { Username = "admin_r2", Email = "admin_r2@example.test", Nev = "Admin", Role = RoleNames.Adminisztrator, JelszoHash = BCryptHasher.HashPassword("Admin-jelszo-2026", 4) };
            var instructor = new Felhasznalo { Username = "oktato_r2", Email = "oktato_r2@example.test", Nev = "Oktató", Role = RoleNames.Oktato, JelszoHash = BCryptHasher.HashPassword(InstructorPassword, 4) };
            var porter = new Felhasznalo { Username = "portas_r2", Email = "portas_r2@example.test", Nev = "Portás", Role = RoleNames.Portas, JelszoHash = BCryptHasher.HashPassword("Portas-jelszo-2026", 4) };
            db.Felhasznalok.AddRange(admin, instructor, porter);
            await db.SaveChangesAsync();
        }

        var users = await db.Felhasznalok.AsNoTracking().ToDictionaryAsync(user => user.Username);
        return (users["admin_r2"], users["oktato_r2"], users["portas_r2"]);
    }

    private HttpClient CreateClient(Felhasznalo user)
    {
        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<AuthService>().GenerateJwtToken(user);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record RoomIdResponse(int Id);

    private sealed record SessionRefreshResponse(DateTime ExpiresAt, DateTime SessionEndsAt);

    private sealed record TicketCountResponse(int TotalCount);
}
