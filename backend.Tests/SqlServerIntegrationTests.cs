using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.DTOs.KeyMovements;
using ProjektLabor.DTOs.Keys;
using ProjektLabor.Models;
using ProjektLabor.Security;
using ProjektLabor.Services;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace backend.Tests;

public sealed class SqlServerIntegrationTests
{
    private const string PreviousMigration = "20261102140952_AddAutomationIdempotencyIndexes";
    private readonly string _connectionString;

    public SqlServerIntegrationTests()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configuredConnection))
        {
            throw new InvalidOperationException(
                "Set SQLSERVER_TEST_CONNECTION to a SQL Server connection for the SQL integration tests.");
        }

        _connectionString = configuredConnection;
    }

    [Fact]
    public async Task SqlServer_MigratesBackfillsAndSupportsAutomationTransactionsAndEndpoints()
    {
        var databaseName = $"ProjektLaborIntegration_{Guid.NewGuid():N}";
        var testConnection = await CreateIsolatedDatabaseAsync(databaseName);

        try
        {
            int instructorId;
            int operatorId;
            int legacyBookingMovementId;
            int unresolvedMovementId;
            int bookingId;
            int keyId;
            var testDate = new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(testConnection)
                .Options;

            await using (var db = new AppDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                var instructor = CreateUser("Oktato");
                var keyOperator = CreateUser("Portas");
                var room = new Terem
                {
                    Nev = $"Integration {Guid.NewGuid():N}",
                    Kapacitas = 20,
                    Aktivalva = true
                };
                db.Felhasznalok.AddRange(instructor, keyOperator);
                db.Termek.Add(room);
                await db.SaveChangesAsync();

                var booking = new Foglalas
                {
                    FelhasznaloId = instructor.Id,
                    TeremId = room.Id,
                    StartTime = testDate.AddHours(-1),
                    EndTime = testDate.AddHours(1),
                    Status = "Aktiv"
                };
                var key = new Kulcs
                {
                    Szam = $"IT-{Guid.NewGuid():N}"[..20],
                    Tipus = "Osztalykulcs",
                    TeremId = room.Id,
                    Aktivalva = true
                };
                db.Foglalasok.Add(booking);
                db.Kulcsok.Add(key);
                await db.SaveChangesAsync();

                instructorId = instructor.Id;
                operatorId = keyOperator.Id;
                bookingId = booking.Id;
                keyId = key.Id;
                legacyBookingMovementId = await InsertLegacyMovementAsync(
                    db,
                    key.Id,
                    booking.Id,
                    keyOperator.Id,
                    testDate);
                unresolvedMovementId = await InsertLegacyMovementAsync(
                    db,
                    key.Id,
                    null,
                    keyOperator.Id,
                    testDate.AddMinutes(1));

                await db.Database.MigrateAsync();
                instructor.PinHash = BCryptHasher.HashPassword("1234");
                await db.SaveChangesAsync();

                var oldRowsAfterMigration = await db.Kulcsmozgasok
                    .AsNoTracking()
                    .OrderBy(movement => movement.Id)
                    .ToListAsync();
                Assert.Equal(2, oldRowsAfterMigration.Count);
                Assert.Equal(instructorId, oldRowsAfterMigration
                    .Single(movement => movement.Id == legacyBookingMovementId)
                    .AtvevoFelhasznaloId);
                Assert.Null(oldRowsAfterMigration
                    .Single(movement => movement.Id == unresolvedMovementId)
                    .AtvevoFelhasznaloId);

                await AssertRecipientSchemaAsync(db, testConnection);

                var recipientLess = new Kulcsmozgas
                {
                    KulcsId = keyId,
                    OperátorId = operatorId,
                    AtvevoFelhasznaloId = instructorId + 100000,
                    MozgasTipus = "Kiadás",
                    Idopont = testDate.AddMinutes(2)
                };
                db.Kulcsmozgasok.Add(recipientLess);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                db.Entry(recipientLess).State = EntityState.Detached;

                var rollbackUser = CreateUser("Oktato");
                rollbackUser.Username = $"rollback_{Guid.NewGuid():N}"[..20];
                rollbackUser.Email = $"{Guid.NewGuid():N}@test.hu";
                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    db.Felhasznalok.Add(rollbackUser);
                    await db.SaveChangesAsync();
                    await transaction.RollbackAsync();
                }

                Assert.False(await db.Felhasznalok
                    .AnyAsync(user => user.Username == rollbackUser.Username));
            }

            await ExerciseSqlBackedEndpointsAsync(testConnection, keyId, instructorId, operatorId);

            await using (var db = new AppDbContext(options))
            {
                var closingProcessor = new DailyKeyClosingProcessor(
                    db,
                    new ConfigurationBuilder().AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Automation:TimeZoneId"] = "UTC" }).Build(),
                    NullLogger<DailyKeyClosingProcessor>.Instance);
                Assert.Equal("22:00", await db.Rendszerparameterek
                    .Where(parameter => parameter.Kulcs == "napi_kulcsleadasi_zarasi_idopont")
                    .Select(parameter => parameter.Ertek)
                    .SingleAsync());

                var closingAt = new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc);
                Assert.Equal(1, await closingProcessor.ProcessAsync(closingAt, default));
                Assert.Equal(0, await closingProcessor.ProcessAsync(closingAt.AddMinutes(1), default));
                Assert.Equal(instructorId, await db.MegbizhatosagiEsemenyek
                    .Where(item => item.KiadasiKulcsmozgasId == legacyBookingMovementId)
                    .Select(item => item.FelhasznaloId)
                    .SingleAsync());
                Assert.False(await db.MegbizhatosagiEsemenyek
                    .AnyAsync(item => item.KiadasiKulcsmozgasId == unresolvedMovementId));
                Assert.Equal(
                    $"DailyKeyClosing:UnresolvedRecipient:{unresolvedMovementId}",
                    await db.AuditLogok
                        .Where(entry => entry.EntitasId == unresolvedMovementId.ToString())
                        .Select(entry => entry.AutomationKey)
                        .SingleAsync());
                Assert.Equal(bookingId, await db.MegbizhatosagiEsemenyek
                    .Where(item => item.KiadasiKulcsmozgasId == legacyBookingMovementId)
                    .Select(item => item.FoglalasId)
                    .SingleAsync());

                var duplicateAudit = new AuditLog
                {
                    MuveletTipus = "Automatizálási adatprobléma",
                    EntitasTipus = "Kulcsmozgas",
                    EntitasId = unresolvedMovementId.ToString(),
                    AutomationKey = $"DailyKeyClosing:UnresolvedRecipient:{unresolvedMovementId}",
                    Indoklas = "Duplikációvédelem integrációs teszt",
                    LetrehozasIdeje = closingAt
                };
                db.AuditLogok.Add(duplicateAudit);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                db.Entry(duplicateAudit).State = EntityState.Detached;

                await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                Assert.False(await ColumnExistsAsync(testConnection, "Kulcsmozgasok", "AtvevoFelhasznaloId"));
                Assert.Equal(4, await db.Kulcsmozgasok.CountAsync());

                await db.GetService<IMigrator>().MigrateAsync();
                Assert.True(await ColumnExistsAsync(testConnection, "Kulcsmozgasok", "AtvevoFelhasznaloId"));
                Assert.Equal(4, await db.Kulcsmozgasok.CountAsync());
                await AssertRecipientSchemaAsync(db, testConnection);
            }
        }
        finally
        {
            await DropIsolatedDatabaseAsync(databaseName);
        }
    }

    private static async Task ExerciseSqlBackedEndpointsAsync(
        string connectionString,
        int keyId,
        int instructorId,
        int operatorId)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                var optionsRegistration = services.SingleOrDefault(
                    descriptor => descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (optionsRegistration is not null)
                {
                    services.Remove(optionsRegistration);
                }

                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            });
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var operatorUser = await db.Felhasznalok.SingleAsync(user => user.Id == operatorId);
        var roomId = await db.Kulcsok
            .Where(key => key.Id == keyId)
            .Select(key => key.TeremId)
            .SingleAsync();
        var endpointKey = new Kulcs
        {
            Szam = $"API-{Guid.NewGuid():N}"[..20],
            Tipus = "Osztalykulcs",
            TeremId = roomId,
            Aktivalva = true
        };
        db.Kulcsok.Add(endpointKey);
        await db.SaveChangesAsync();
        var token = authService.GenerateJwtToken(operatorUser);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var issueResponse = await client.PostAsJsonAsync(
            "/api/key-movements/issue",
            new IssueKeyRequestDto
            {
                KulcsId = endpointKey.Id,
                AtvevoFelhasznaloId = instructorId,
                Pin = "1234",
                TeremAllapot = "Rendben"
            });
        Assert.Equal(HttpStatusCode.Created, issueResponse.StatusCode);
        var issuedMovement = await issueResponse.Content.ReadFromJsonAsync<KeyMovementItemDto>();
        Assert.NotNull(issuedMovement);
        Assert.Equal(instructorId, issuedMovement.AtvevoFelhasznaloId);
        Assert.Equal(operatorId, issuedMovement.OperatorId);

        using var returnResponse = await client.PostAsJsonAsync(
            "/api/key-movements/return",
            new ReturnKeyRequestDto
            {
                KulcsId = endpointKey.Id,
                Pin = "1234",
                TeremAllapot = "Rendben"
            });
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        var returnedMovement = await returnResponse.Content.ReadFromJsonAsync<KeyMovementItemDto>();
        Assert.NotNull(returnedMovement);
        Assert.Equal(instructorId, returnedMovement.AtvevoFelhasznaloId);

        Assert.Equal(instructorId, await db.Kulcsmozgasok
            .AsNoTracking()
            .Where(movement => movement.Id == issuedMovement.Id)
            .Select(movement => movement.AtvevoFelhasznaloId)
            .SingleAsync());
    }

    private static async Task AssertRecipientSchemaAsync(
        AppDbContext db,
        string connectionString)
    {
        Assert.True(await ColumnExistsAsync(
            connectionString,
            "Kulcsmozgasok",
            "AtvevoFelhasznaloId"));
        Assert.True(await ForeignKeyExistsAsync(db));
        Assert.True(await IndexExistsAsync(db));
        Assert.True(await AuditIndexExistsAsync(db));
    }

    private static async Task<bool> ColumnExistsAsync(
        string connectionString,
        string tableName,
        string columnName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            WHERE t.name = @tableName AND c.name = @columnName;
            """;
        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ForeignKeyExistsAsync(AppDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sys.foreign_keys
            WHERE name = N'FK_Kulcsmozgasok_Felhasznalok_AtvevoFelhasznaloId';
            """;
        if (command.Connection!.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> IndexExistsAsync(AppDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE name = N'IX_Kulcsmozgasok_AtvevoFelhasznaloId'
              AND is_unique = 0;
            """;
        if (command.Connection!.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> AuditIndexExistsAsync(AppDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE name = N'IX_AuditLogok_AutomationKey'
              AND is_unique = 1
              AND has_filter = 1;
            """;
        if (command.Connection!.State != ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private async Task<string> CreateIsolatedDatabaseAsync(string databaseName)
    {
        var masterConnectionString = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5
        }.ConnectionString;

        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT DB_ID(@databaseName);";
        check.Parameters.AddWithValue("@databaseName", databaseName);
        if (await check.ExecuteScalarAsync() is not DBNull)
        {
            throw new InvalidOperationException("The generated SQL integration database already exists.");
        }

        await using var create = connection.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{databaseName}];";
        await create.ExecuteNonQueryAsync();

        return new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = databaseName,
            TrustServerCertificate = true
        }.ConnectionString;
    }

    private async Task DropIsolatedDatabaseAsync(string databaseName)
    {
        SqlConnection.ClearAllPools();
        var masterConnectionString = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5
        }.ConnectionString;

        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE [{databaseName}];";
        await drop.ExecuteNonQueryAsync();
    }

    private static Felhasznalo CreateUser(string role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new Felhasznalo
        {
            Username = suffix[..12],
            Email = $"{suffix}@test.hu",
            JelszoHash = "integration-test-hash",
            Nev = role,
            Role = role,
            PinHash = role == RoleNames.Oktato ? BCryptHasher.HashPassword("1234") : null
        };
    }

    private static async Task<int> InsertLegacyMovementAsync(
        AppDbContext db,
        int keyId,
        int? bookingId,
        int operatorId,
        DateTime time)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            """
            INSERT INTO [Kulcsmozgasok]
                ([KulcsId], [FoglalasId], [MozgasTipus], [Idopont], [OperátorId], [Megjegyzes], [CreatedAt])
            OUTPUT INSERTED.[Id]
            VALUES
                (@keyId, @bookingId, N'Kiadás', @time, @operatorId, N'', @time);
            """;

        var keyParameter = command.CreateParameter();
        keyParameter.ParameterName = "@keyId";
        keyParameter.Value = keyId;
        command.Parameters.Add(keyParameter);

        var bookingParameter = command.CreateParameter();
        bookingParameter.ParameterName = "@bookingId";
        bookingParameter.Value = bookingId.HasValue ? bookingId.Value : DBNull.Value;
        command.Parameters.Add(bookingParameter);

        var operatorParameter = command.CreateParameter();
        operatorParameter.ParameterName = "@operatorId";
        operatorParameter.Value = operatorId;
        command.Parameters.Add(operatorParameter);

        var timeParameter = command.CreateParameter();
        timeParameter.ParameterName = "@time";
        timeParameter.Value = time;
        command.Parameters.Add(timeParameter);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task IsolatedSqlServerDatabase_AppliesMigrationsAndTestsBootstrapAuthentication()
    {
        var databaseName = $"ProjektLaborBootstrap_{Guid.NewGuid():N}";
        var connectionString = await CreateIsolatedDatabaseAsync(databaseName);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();

            Assert.True(await db.Database.CanConnectAsync());
            Assert.NotEmpty(db.Model.FindEntityType(typeof(Foglalas))!.GetForeignKeys());

            await using var transaction = await db.Database.BeginTransactionAsync();
            var room = new Terem { Nev = $"SQL test {Guid.NewGuid():N}", Kapacitas = 1 };
            db.Termek.Add(room);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();

            var suffix = Guid.NewGuid().ToString("N")[..10];
            var roleConfiguration = RoleNames.All.ToDictionary(
                role => role,
                role => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)));
            var settings = new Dictionary<string, string?>
            {
                ["BootstrapDemoUsers:Enabled"] = "true"
            };
            foreach (var role in RoleNames.All)
            {
                var username = $"sql_{role.ToLowerInvariant()}_{suffix}";
                settings[$"BootstrapDemoUsers:{role}:Username"] = username;
                settings[$"BootstrapDemoUsers:{role}:Password"] = roleConfiguration[role];
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            await DemoUserBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);
            await DemoUserBootstrapper.InitializeAsync(db, configuration, NullLogger.Instance);

            var users = await db.Felhasznalok
                .Where(user => user.Username.EndsWith(suffix))
                .ToListAsync();
            Assert.Equal(RoleNames.All.Length, users.Count);

            var jwtSettings = new JwtSettings(
                "sql-server-integration-tests",
                "sql-server-integration-tests-api",
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            var authService = new AuthService(db, configuration, jwtSettings, NullLogger<AuthService>.Instance);
            foreach (var role in RoleNames.All)
            {
                var user = Assert.Single(users, account => account.Role == role);
                var password = roleConfiguration[role];
                Assert.True(BCryptHasher.Verify(password, user.JelszoHash));
                Assert.NotEqual(password, user.JelszoHash);

                var login = await authService.LoginAsync(new LoginRequestDto
                {
                    Username = user.Username,
                    Jelszo = password
                });
                Assert.NotNull(login);
                Assert.Equal(role, login.Felhasznalo.Role);
                var serializedLogin = System.Text.Json.JsonSerializer.Serialize(login);
                Assert.DoesNotContain(password, serializedLogin, StringComparison.Ordinal);
                Assert.DoesNotContain("JelszoHash", serializedLogin, StringComparison.Ordinal);
            }
        }
        finally
        {
            await DropIsolatedDatabaseAsync(databaseName);
        }
    }
}
