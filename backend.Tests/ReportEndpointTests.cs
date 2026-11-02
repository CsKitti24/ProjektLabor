using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.Endpoints;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace backend.Tests;

public class ReportEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTime ReportDay = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbName = "ReportTestDb_" + Guid.NewGuid();

    public ReportEndpointTests(WebApplicationFactory<Program> factory)
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

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_dbName));
            });
        });
    }

    [Fact]
    public async Task Reports_ReturnExpectedMetrics_ForAuthorizedOperationsDirector()
    {
        var client = await CreateClientWithSeededDataAsync("UzemeltetesiIgazgato");

        var daily = await client.GetFromJsonAsync<ReportEndpoints.DailyReportResponse>(
            "/api/reports/daily?datum=2026-10-12");
        Assert.NotNull(daily);
        Assert.Equal(1, daily.OsszesKulcskiadas);
        Assert.Equal(1, daily.OsszesKulcsvisszavetel);
        Assert.Equal(1, daily.OsszesKesedelmesFoglalas);
        Assert.Equal(1, daily.OsszesNoShow);
        Assert.Contains(daily.TermenkintiForgalom, room =>
            room.TeremNev == "A terem" && room.Foglalasok == 1 && room.Kulcskiadasok == 1);

        var weekly = await client.GetFromJsonAsync<ReportEndpoints.WeeklyReportResponse>(
            "/api/reports/weekly?datum=2026-10-12");
        Assert.NotNull(weekly);
        Assert.Equal(new DateOnly(2026, 10, 12), weekly.HetKezdete);
        Assert.Equal(new DateOnly(2026, 10, 18), weekly.HetVege);
        Assert.Equal(1, weekly.OsszesKulcskiadas);
        Assert.Equal(1, weekly.OsszesNoShow);

        var utilization = await client.GetFromJsonAsync<ReportEndpoints.RoomUtilizationReportResponse>(
            "/api/reports/room-utilization?kezdet=2026-10-12&veg=2026-10-12");
        Assert.NotNull(utilization);
        Assert.Equal(2, utilization.Termek.Count);
        Assert.Equal(1.5, utilization.OsszesFoglaltOra);
        Assert.Equal(24, utilization.OsszesRendelkezesreAlloOra);
        Assert.Equal(6.25, utilization.KihasznaltsagSzazalek);

        var keyTraffic = await client.GetFromJsonAsync<ReportEndpoints.KeyTrafficReportResponse>(
            "/api/reports/key-traffic?kezdet=2026-10-12&veg=2026-10-12");
        Assert.NotNull(keyTraffic);
        Assert.Equal(2, keyTraffic.OsszesMozgas);
        Assert.Equal(1, keyTraffic.OsszesKiadas);
        Assert.Equal(1, keyTraffic.OsszesVisszavetel);
        Assert.Single(keyTraffic.Kulcsok);

        var reliability = await client.GetFromJsonAsync<ReportEndpoints.ReliabilityReportResponse>(
            "/api/reports/reliability");
        Assert.NotNull(reliability);
        Assert.Equal(2, reliability.EsemenyekSzama);
        Assert.Equal(1, reliability.ErintettOktatokSzama);
        Assert.Equal(10, reliability.AtlagosPontszam);
        Assert.Equal(2, reliability.Esemenytipusok.Count);
    }

    [Fact]
    public async Task Reports_RequireOperationsDirectorOrAdministratorRole()
    {
        var client = await CreateClientWithSeededDataAsync("Oktato");

        var response = await client.GetAsync("/api/reports/daily?datum=2026-10-12");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DailyReport_RejectsMissingOrInvalidDate()
    {
        var client = await CreateClientWithSeededDataAsync("Adminisztrator");

        var missingDate = await client.GetAsync("/api/reports/daily");
        var invalidDate = await client.GetAsync("/api/reports/daily?datum=12-10-2026");

        Assert.Equal(HttpStatusCode.BadRequest, missingDate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);
    }

    [Fact]
    public async Task Reports_RequireAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/reports/reliability");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> CreateClientWithSeededDataAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new Felhasznalo
        {
            Username = $"report_{Guid.NewGuid():N}",
            Nev = "Riport teszt felhasználó",
            Email = $"{Guid.NewGuid():N}@test.hu",
            Role = role,
            JelszoHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var instructor = new Felhasznalo
        {
            Username = $"instructor_{Guid.NewGuid():N}",
            Nev = "Riport teszt oktató",
            Email = $"{Guid.NewGuid():N}@test.hu",
            Role = "Oktato",
            JelszoHash = BCrypt.Net.BCrypt.HashPassword("TestPass123!"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Felhasznalok.AddRange(user, instructor);

        var firstRoom = new Terem { Nev = "A terem", Kapacitas = 20, Aktivalva = true };
        var secondRoom = new Terem { Nev = "B terem", Kapacitas = 30, Aktivalva = true };
        db.Termek.AddRange(firstRoom, secondRoom);
        await db.SaveChangesAsync();

        var key = new Kulcs
        {
            Szam = "R-001",
            Tipus = "Osztalykulcs",
            TeremId = firstRoom.Id,
            Aktivalva = true
        };
        db.Kulcsok.Add(key);
        db.Foglalasok.AddRange(
            new Foglalas
            {
                FelhasznaloId = instructor.Id,
                TeremId = firstRoom.Id,
                StartTime = ReportDay.AddHours(9).AddMinutes(30),
                EndTime = ReportDay.AddHours(11),
                Status = "Aktiv"
            },
            new Foglalas
            {
                FelhasznaloId = instructor.Id,
                TeremId = secondRoom.Id,
                StartTime = ReportDay.AddHours(12).AddMinutes(30),
                EndTime = ReportDay.AddHours(13).AddMinutes(15),
                Status = "NoShow"
            });
        await db.SaveChangesAsync();

        db.Kulcsmozgasok.AddRange(
            new Kulcsmozgas
            {
                KulcsId = key.Id,
                MozgasTipus = "Kiadás",
                Idopont = ReportDay.AddHours(9),
                OperátorId = user.Id
            },
            new Kulcsmozgas
            {
                KulcsId = key.Id,
                MozgasTipus = "Visszavétel",
                Idopont = ReportDay.AddHours(11),
                OperátorId = user.Id
            });

        var academicYear = DateTime.UtcNow.Month >= 9
            ? $"{DateTime.UtcNow.Year}/{DateTime.UtcNow.Year + 1}"
            : $"{DateTime.UtcNow.Year - 1}/{DateTime.UtcNow.Year}";
        db.MegbizhatosagiEsemenyek.AddRange(
            new MegbizhatosagiEsemeny
            {
                FelhasznaloId = instructor.Id,
                EsemenyTipus = "Késedelmesen leadott",
                Pont = -5,
                EsemenyIdopontja = ReportDay.AddHours(11),
                Tanev = academicYear
            },
            new MegbizhatosagiEsemeny
            {
                FelhasznaloId = instructor.Id,
                EsemenyTipus = "Időben leadott",
                Pont = 10,
                EsemenyIdopontja = ReportDay.AddHours(12),
                Tanev = academicYear
            });
        await db.SaveChangesAsync();

        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authService.GenerateJwtToken(user));
        return client;
    }
}
