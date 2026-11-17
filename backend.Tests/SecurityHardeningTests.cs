using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using ProjektLabor.Security;

namespace backend.Tests;

/// <summary>
/// Bejelentkezési kéréskorlát, anonim auditbejegyzések korlátja és CORS-szabályok.
/// </summary>
public class SecurityHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecurityHardeningTests(WebApplicationFactory<Program> factory)
    {
        var dbName = "SecurityHardening_" + Guid.NewGuid();
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

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });
    }

    [Fact]
    public async Task Login_IsRateLimitedPerClient()
    {
        using var client = _factory.CreateClient();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var rejected = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { username = "nincs-ilyen", jelszo = "rossz-jelszo" });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        var limited = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "nincs-ilyen", jelszo = "rossz-jelszo" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task AnonymousDeniedRequests_AreAuditedOnlyUpToLimit()
    {
        const int limit = AccessAuditMiddleware.AnonymousEntriesPerMinute;
        const int requests = limit * 2 + 5;
        using var client = _factory.CreateClient();
        for (var request = 0; request < requests; request++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/audit")).StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.AuditLogok.CountAsync(entry => entry.MuveletTipus == "ACCESS_UNAUTHENTICATED");

        // Ha a futás közben percet vált az óra, a számláló egyszer újraindulhat: így legfeljebb
        // két ablaknyi bejegyzés keletkezhet, de a kérések egy része biztosan nem kerül a táblába.
        Assert.InRange(entries, limit, limit * 2);
    }

    [Fact]
    public async Task Login_SetsHttpOnlyCookie_ThatAuthenticatesAndIsClearedOnLogout()
    {
        const string password = "CookieJelszo1!";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Felhasznalok.Add(new ProjektLabor.Models.Felhasznalo
            {
                Username = "suti.teszt",
                Email = "suti.teszt@test.hu",
                Nev = "Süti Teszt",
                Role = RoleNames.Oktato,
                JelszoHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4)
            });
            await db.SaveChangesAsync();
        }

        // A kliens kezeli a sütiket, Authorization fejlécet nem küldünk.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "suti.teszt", jelszo = password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var setCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{AuthCookie.Name}=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", setCookie, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:3000", true)]
    [InlineData("https://evil.example", false)]
    public async Task Cors_AllowsOnlyConfiguredOrigins(string origin, bool allowed)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Add("Origin", origin);

        var response = await client.SendAsync(request);

        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
