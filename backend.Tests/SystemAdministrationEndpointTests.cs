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

public class SystemAdministrationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbName = "SystemAdministrationTestDb_" + Guid.NewGuid();

    public SystemAdministrationEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task GetParameters_AllowsAdministratorAndDirector_ButForbidsInstructor()
    {
        var (administratorClient, _) = await CreateAuthenticatedClientAsync("Adminisztrator");
        var (directorClient, _) = await CreateAuthenticatedClientAsync("UzemeltetesiIgazgato");
        var (instructorClient, _) = await CreateAuthenticatedClientAsync("Oktato");

        Assert.Equal(HttpStatusCode.OK, (await administratorClient.GetAsync("/api/parameters")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await directorClient.GetAsync("/api/parameters")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await instructorClient.GetAsync("/api/parameters")).StatusCode);
    }

    [Fact]
    public async Task UpdateParameter_StoresValueAndAuditEntry()
    {
        var (client, administrator) = await CreateAuthenticatedClientAsync("Adminisztrator");

        var response = await client.PutAsJsonAsync(
            "/api/parameters/napi_kulcsleadasi_zarasi_idopont",
            new SystemAdministrationEndpoints.UpdateParameterRequest("21:45", "Új zárási idő"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedParameter = await response.Content.ReadFromJsonAsync<SystemAdministrationEndpoints.ParameterResponse>();
        Assert.NotNull(updatedParameter);
        Assert.Equal("21:45", updatedParameter.Ertek);

        var parameterResponse = await client.GetAsync(
            "/api/parameters/napi_kulcsleadasi_zarasi_idopont");
        Assert.Equal(HttpStatusCode.OK, parameterResponse.StatusCode);

        var auditResponse = await client.GetAsync(
            $"/api/audit?felhasznaloId={administrator.Id}&muveletTipus={Uri.EscapeDataString("Rendszerparaméter módosítása")}");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var audit = await auditResponse.Content.ReadFromJsonAsync<SystemAdministrationEndpoints.AuditLogListResponse>();
        Assert.NotNull(audit);
        var entry = Assert.Single(audit.Items);
        Assert.Equal("22:00", entry.RegiErtek);
        Assert.Equal("21:45", entry.UjErtek);
        Assert.Equal(administrator.Nev, entry.FelhasznaloNev);

        var auditDetailResponse = await client.GetAsync($"/api/audit/{entry.Id}");
        Assert.Equal(HttpStatusCode.OK, auditDetailResponse.StatusCode);

        var start = Uri.EscapeDataString(entry.LetrehozasIdeje.AddSeconds(-1).ToString("O"));
        var end = Uri.EscapeDataString(entry.LetrehozasIdeje.AddSeconds(1).ToString("O"));
        var dateFilteredResponse = await client.GetAsync(
            $"/api/audit?kezdet={start}&veg={end}");
        Assert.Equal(HttpStatusCode.OK, dateFilteredResponse.StatusCode);
        var dateFilteredAudit =
            await dateFilteredResponse.Content.ReadFromJsonAsync<SystemAdministrationEndpoints.AuditLogListResponse>();
        Assert.NotNull(dateFilteredAudit);
        Assert.Contains(dateFilteredAudit.Items, item => item.Id == entry.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedParameter = await db.Rendszerparameterek.FindAsync("napi_kulcsleadasi_zarasi_idopont");
        Assert.Equal("21:45", storedParameter?.Ertek);
    }

    [Theory]
    [InlineData("napi_kulcsleadasi_zarasi_idopont", "10:5")]
    [InlineData("no_show_idokorlat_perc", "0")]
    [InlineData("no_show_idokorlat_perc", "-5")]
    [InlineData("no_show_idokorlat_perc", "15.5")]
    public async Task UpdateParameter_ReturnsBadRequestForInvalidKnownValues(string key, string value)
    {
        var (client, _) = await CreateAuthenticatedClientAsync("Adminisztrator");

        var response = await client.PutAsJsonAsync(
            $"/api/parameters/{key}",
            new SystemAdministrationEndpoints.UpdateParameterRequest(value, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateParameter_ReturnsNotFoundForUnknownParameter()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("Adminisztrator");

        var response = await client.PutAsJsonAsync(
            "/api/parameters/unknown_setting",
            new SystemAdministrationEndpoints.UpdateParameterRequest("value", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateParameter_IsForbiddenForOperationsDirector()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("UzemeltetesiIgazgato");

        var response = await client.PutAsJsonAsync(
            "/api/parameters/napi_kulcsleadasi_zarasi_idopont",
            new SystemAdministrationEndpoints.UpdateParameterRequest("21:45", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditLog_ReturnsNotFoundForMissingEntry()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("Adminisztrator");

        var response = await client.GetAsync("/api/audit/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(HttpClient Client, Felhasznalo User)> CreateAuthenticatedClientAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new Felhasznalo
        {
            Username = $"system_{suffix}",
            Nev = $"Teszt {role}",
            Email = $"system_{suffix}@test.hu",
            Role = role,
            JelszoHash = BCrypt.Net.BCrypt.HashPassword("Test1234!"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Felhasznalok.Add(user);
        if (!await db.Rendszerparameterek.AnyAsync())
        {
            db.Rendszerparameterek.AddRange(
                new Rendszerparameter
                {
                    Kulcs = "napi_kulcsleadasi_zarasi_idopont",
                    Ertek = "22:00",
                    Leiras = "Napi kulcsleadási zárási időpont"
                },
                new Rendszerparameter
                {
                    Kulcs = "no_show_idokorlat_perc",
                    Ertek = "15",
                    Leiras = "No-show időkorlát percben"
                });
        }

        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authService.GenerateJwtToken(user));
        return (client, user);
    }
}
