using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace backend.Tests;

public sealed class SqlServerIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION")
        ?? throw new InvalidOperationException(
            "SQLSERVER_TEST_CONNECTION must point to a dedicated disposable SQL Server database.");

    [Fact]
    public async Task DedicatedSqlServerDatabase_AppliesMigrationsAndCreatesExpectedSchema()
    {
        if (!HasSqlServerConnection())
        {
            return;
        }
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        var tableNames = await db.Database
            .SqlQueryRaw<string>("SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        Assert.All(
            new[] { "Felhasznalok", "Termek", "Kulcsok", "Foglalasok", "Kulcsmozgasok", "Ertesitesek", "AuditLogok" },
            table => Assert.Contains(table, tableNames));
        Assert.Contains(
            db.Model.FindEntityType(typeof(Foglalas))!.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Terem));
    }

    [Fact]
    public async Task DedicatedSqlServerDatabase_PersistsBusinessDataAndEnforcesConstraints()
    {
        if (!HasSqlServerConnection())
        {
            return;
        }
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var user = new Felhasznalo
        {
            Username = $"sql_{suffix}",
            Email = $"{suffix}@example.test",
            JelszoHash = "test-hash",
            Nev = "SQL integration test",
            Role = "Oktato"
        };
        var room = new Terem { Nev = $"SQL room {suffix}", Kapacitas = 10 };
        var key = new Kulcs { Szam = $"SQL-{suffix[..12]}", Tipus = "Osztalykulcs", Terem = room };
        db.AddRange(user, room, key);
        await db.SaveChangesAsync();

        var booking = new Foglalas
        {
            FelhasznaloId = user.Id,
            TeremId = room.Id,
            StartTime = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 11, 4, 10, 45, 0, DateTimeKind.Utc),
            Status = "Aktiv"
        };
        db.Foglalasok.Add(booking);
        await db.SaveChangesAsync();

        db.Kulcsmozgasok.Add(new Kulcsmozgas
        {
            KulcsId = key.Id,
            FoglalasId = booking.Id,
            MozgasTipus = "Kiadás",
            Idopont = DateTime.UtcNow,
            OperátorId = user.Id
        });
        db.Ertesitesek.Add(new Ertesites
        {
            FoglalasId = booking.Id,
            ErtesitesTipus = "BeforeEnd",
            IdoPont = booking.EndTime,
            Allapot = "Kesz"
        });
        db.AuditLogok.Add(new AuditLog
        {
            FelhasznaloId = user.Id,
            MuveletTipus = "TEST",
            EntitasTipus = "Foglalas",
            EntitasId = booking.Id.ToString()
        });
        await db.SaveChangesAsync();

        var persisted = await db.Foglalasok
            .Include(item => item.Kulcsmozgasok)
            .Include(item => item.Ertesitesek)
            .SingleAsync(item => item.Id == booking.Id);
        Assert.Single(persisted.Kulcsmozgasok);
        Assert.Single(persisted.Ertesitesek);

        db.MesterkulcsJogosultsagok.Add(new MesterkulcsJogosultsag
        {
            FelhasznaloId = user.Id,
            KulcsId = key.Id
        });
        await db.SaveChangesAsync();
        db.MesterkulcsJogosultsagok.Add(new MesterkulcsJogosultsag
        {
            FelhasznaloId = user.Id,
            KulcsId = key.Id
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.Kulcsmozgasok.Add(new Kulcsmozgas
        {
            KulcsId = -1,
            MozgasTipus = "Kiadás",
            Idopont = DateTime.UtcNow,
            OperátorId = user.Id
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DedicatedSqlServerDatabase_RollsBackTransactions()
    {
        if (!HasSqlServerConnection())
        {
            return;
        }
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var username = $"rollback_{Guid.NewGuid():N}";
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Felhasznalok.Add(new Felhasznalo
            {
                Username = username,
                Email = $"{username}@example.test",
                JelszoHash = "test-hash",
                Nev = "Rollback test",
                Role = "Oktato"
            });
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        Assert.False(await db.Felhasznalok.AnyAsync(user => user.Username == username));
    }

    [Fact]
    public async Task DedicatedSqlServerDatabase_AcquiresApplicationLocksInsideTransactions()
    {
        if (!HasSqlServerConnection())
        {
            return;
        }

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await SqlServerLocking.AcquireAsync(db, $"integration-test:{Guid.NewGuid():N}");

        await transaction.CommitAsync();
    }

    [Fact]
    public async Task DedicatedSqlServerDatabase_SerializesConcurrentApplicationLocks()
    {
        if (!HasSqlServerConnection())
        {
            return;
        }

        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();
        await firstDb.Database.MigrateAsync();
        await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
        await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
        var resource = $"integration-concurrency:{Guid.NewGuid():N}";

        await SqlServerLocking.AcquireAsync(firstDb, resource);
        var secondLock = SqlServerLocking.AcquireAsync(secondDb, resource);

        await Task.Delay(250);
        Assert.False(secondLock.IsCompleted);

        await firstTransaction.CommitAsync();
        await secondLock;
        await secondTransaction.CommitAsync();
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    private static bool HasSqlServerConnection()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION")))
        {
            Console.WriteLine(
                "SQLSERVER_TEST_CONNECTION is not configured; SQL Server integration test was not executed.");
            return false;
        }

        return true;
    }
}
