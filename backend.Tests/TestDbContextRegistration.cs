using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjektLabor.Data;

namespace backend.Tests;

internal static class TestDbContextRegistration
{
    /// <summary>
    /// Az EF Core 9+ az AddDbContext konfigurációját (pl. UseSqlServer) külön IDbContextOptionsConfiguration
    /// szolgáltatásként is regisztrálja; tesztadatbázisra cseréléskor ezt is el kell távolítani, különben két
    /// adatbázis-szolgáltató kerülne ugyanabba a kontextusba.
    /// </summary>
    public static IServiceCollection RemoveAppDbContextConfiguration(this IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<AppDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
        return services;
    }
}
