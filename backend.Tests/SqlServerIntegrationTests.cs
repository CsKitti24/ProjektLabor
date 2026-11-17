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
using ProjektLabor.DTOs.Bookings;
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
        // Kapcsolat nélkül a tesztek a SqlServerFact attribútum miatt kihagyásra kerülnek.
        _connectionString = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable) ?? string.Empty;
    }

    [SqlServerFact]
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
            int masterKeyId;
            int masterRoomId;
            var testDate = new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(testConnection)
                .Options;

            await using (var db = new AppDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

                var legacyData = await InsertLegacySeedDataAsync(db, testDate);
                instructorId = legacyData.InstructorId;
                operatorId = legacyData.OperatorId;
                bookingId = legacyData.BookingId;
                keyId = legacyData.KeyId;
                masterRoomId = await db.Kulcsok
                    .Where(key => key.Id == keyId)
                    .Select(key => key.TeremId)
                    .SingleAsync();
                masterKeyId = await InsertLegacyRowAsync(
                    db,
                    """
                    INSERT INTO [Kulcsok] ([Szam], [Tipus], [Aktivalva], [TeremId])
                    OUTPUT INSERTED.[Id]
                    VALUES (@number, N'Mesterkulcs', 1, @roomId);
                    """,
                    ("@number", $"MK-{Guid.NewGuid():N}"[..20]),
                    ("@roomId", masterRoomId));
                legacyBookingMovementId = await InsertLegacyMovementAsync(
                    db,
                    keyId,
                    bookingId,
                    operatorId,
                    testDate);
                unresolvedMovementId = await InsertLegacyMovementAsync(
                    db,
                    keyId,
                    null,
                    operatorId,
                    testDate.AddMinutes(1));

                await db.Database.MigrateAsync();
                Assert.True(await db.KulcsTermek.AnyAsync(access =>
                    access.KulcsId == masterKeyId && access.TeremId == masterRoomId));
                var instructorForPin = await db.Felhasznalok
                    .SingleAsync(user => user.Id == instructorId);
                instructorForPin.PinHash = BCryptHasher.HashPassword("1234");
                await db.SaveChangesAsync();

                // A mintaadat-migráció (MINTA-* kulcsok) mozgásait kihagyjuk.
                var oldRowsAfterMigration = await db.Kulcsmozgasok
                    .AsNoTracking()
                    .Where(movement => !movement.Kulcs.Szam.StartsWith("MINTA-"))
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

                var operatorUserForClosing = await db.Felhasznalok
                    .SingleAsync(user => user.Id == operatorId);
                operatorUserForClosing.Role = RoleNames.UzemeltetesiIgazgato;
                await db.SaveChangesAsync();

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
                        .Where(entry => entry.AutomationKey ==
                            $"DailyKeyClosing:UnresolvedRecipient:{unresolvedMovementId}")
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
                Assert.Equal(4, await db.Kulcsmozgasok.CountAsync(movement => !movement.Kulcs.Szam.StartsWith("MINTA-")));
                // Visszaállítás után újrafuttatva a mintaadat-migráció ismét lefut (34 kiadás + 32 visszavétel).
                Assert.Equal(66, await db.Kulcsmozgasok.CountAsync(movement => movement.Kulcs.Szam.StartsWith("MINTA-")));
                await AssertRecipientSchemaAsync(db, testConnection);
                operatorUserForClosing.Role = RoleNames.Portas;
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            await DropIsolatedDatabaseAsync(databaseName);
        }
    }

    [SqlServerFact]
    public async Task SqlServer_EveningKeyReturnNotificationIsPersistentAndUniquePerPorter()
    {
        var databaseName = $"ProjektLaborEveningNotification_{Guid.NewGuid():N}";
        var connectionString = await CreateIsolatedDatabaseAsync(databaseName);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();

            var instructor = CreateUser(RoleNames.Oktato);
            var firstPorter = CreateUser(RoleNames.Portas);
            var secondPorter = CreateUser(RoleNames.Portas);
            var room = new Terem
            {
                Nev = $"Evening notification {Guid.NewGuid():N}",
                Kapacitas = 20,
                Aktivalva = true
            };
            db.Felhasznalok.AddRange(instructor, firstPorter, secondPorter);
            db.Termek.Add(room);
            await db.SaveChangesAsync();

            var key = new Kulcs
            {
                Szam = $"EN-{Guid.NewGuid():N}"[..20],
                Tipus = "Osztalykulcs",
                TeremId = room.Id,
                Aktivalva = true
            };
            db.Kulcsok.Add(key);
            await db.SaveChangesAsync();

            var issuedAt = new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc);
            var movement = new Kulcsmozgas
            {
                KulcsId = key.Id,
                AtvevoFelhasznaloId = instructor.Id,
                OperátorId = firstPorter.Id,
                MozgasTipus = "Kiadás",
                Idopont = issuedAt
            };
            db.Kulcsmozgasok.Add(movement);
            await db.SaveChangesAsync();

            var processor = new DailyKeyClosingProcessor(
                db,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Automation:TimeZoneId"] = "UTC"
                    })
                    .Build(),
                NullLogger<DailyKeyClosingProcessor>.Instance);
            var deadline = new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc);

            Assert.Equal(0, await processor.ProcessAsync(deadline.AddSeconds(-1), default));
            Assert.Empty(await db.Ertesitesek.ToListAsync());
            Assert.Equal(1, await processor.ProcessAsync(deadline, default));
            Assert.Equal(0, await processor.ProcessAsync(deadline.AddMinutes(1), default));

            // Minden portás (a mintaadat-migráció "portas" fiókja is) kap egy értesítést.
            var porterIds = await db.Felhasznalok
                .Where(user => user.Role == RoleNames.Portas)
                .Select(user => user.Id)
                .ToListAsync();
            Assert.Contains(firstPorter.Id, porterIds);
            Assert.Contains(secondPorter.Id, porterIds);
            var notifications = await db.Ertesitesek
                .Where(item => item.KulcsmozgasId == movement.Id)
                .OrderBy(item => item.CimzettFelhasznaloId)
                .ToListAsync();
            Assert.Equal(porterIds.Count, notifications.Count);
            Assert.Equal(
                porterIds.Order(),
                notifications.Select(item => item.CimzettFelhasznaloId!.Value).Order());
            Assert.All(notifications, notification =>
            {
                Assert.Equal(movement.Id, notification.KulcsmozgasId);
                Assert.Equal(deadline, notification.Hatarido);
                Assert.False(notification.Olvasva);
            });

            db.Ertesitesek.Add(new Ertesites
            {
                CimzettFelhasznaloId = firstPorter.Id,
                KulcsmozgasId = movement.Id,
                ErtesitesTipus = "EveningKeyReturn",
                IdoPont = deadline,
                Hatarido = deadline,
                Allapot = "Kesz",
                AutomationKey = notifications[0].AutomationKey,
                CreatedAt = deadline
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            await DropIsolatedDatabaseAsync(databaseName);
        }
    }

    [SqlServerFact]
    public async Task SqlServer_ConvertsLegacyLocalBookingAndMaintenanceTimesToUtcAndBack()
    {
        const string migrationBeforeUtc = "20261110181630_BackfillMasterKeyRooms";
        const string utcMigration = "20261117190736_ConvertBookingAndMaintenanceTimesToUtc";
        var databaseName = $"ProjektLaborIntegration_{Guid.NewGuid():N}";
        var testConnection = await CreateIsolatedDatabaseAsync(databaseName);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(testConnection)
                .Options;
            int winterBookingId;
            int summerBookingId;
            int maintenanceId;

            await using (var db = new AppDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync(migrationBeforeUtc);

                // Nyers SQL, mert a migráció előtti séma még nem tartalmazza a később hozzáadott oszlopokat.
                var suffix = Guid.NewGuid().ToString("N");
                var userId = await InsertLegacyRowAsync(
                    db,
                    """
                    INSERT INTO [Felhasznalok] ([Username], [Email], [JelszoHash], [Nev], [Role], [CreatedAt], [UpdatedAt])
                    OUTPUT INSERTED.[Id]
                    VALUES (@username, @email, N'integration-test-hash', N'Oktato', N'Oktato', SYSUTCDATETIME(), SYSUTCDATETIME());
                    """,
                    ("@username", suffix[..12]),
                    ("@email", $"{suffix}@test.hu"));
                var roomId = await InsertLegacyRowAsync(
                    db,
                    """
                    INSERT INTO [Termek] ([Nev], [Kapacitas], [Aktivalva])
                    OUTPUT INSERTED.[Id]
                    VALUES (@name, 10, 1);
                    """,
                    ("@name", $"UTC-{suffix}"[..20]));

                // A régi adatok budapesti helyi időben, megjelölés nélkül kerültek az adatbázisba.
                const string insertBooking = """
                    INSERT INTO [Foglalasok] ([FelhasznaloId], [TeremId], [StartTime], [EndTime], [Status], [CreatedAt], [UpdatedAt])
                    OUTPUT INSERTED.[Id]
                    VALUES (@userId, @roomId, @start, @end, N'Aktiv', SYSUTCDATETIME(), SYSUTCDATETIME());
                    """;
                winterBookingId = await InsertLegacyRowAsync(
                    db, insertBooking,
                    ("@userId", userId), ("@roomId", roomId),
                    ("@start", new DateTime(2026, 11, 2, 10, 15, 0)),
                    ("@end", new DateTime(2026, 11, 2, 11, 0, 0)));
                summerBookingId = await InsertLegacyRowAsync(
                    db, insertBooking,
                    ("@userId", userId), ("@roomId", roomId),
                    ("@start", new DateTime(2026, 7, 1, 8, 0, 0)),
                    ("@end", new DateTime(2026, 7, 1, 8, 45, 0)));
                maintenanceId = await InsertLegacyRowAsync(
                    db,
                    """
                    INSERT INTO [KarbantartasiIdosavok] ([FelhasznaloId], [TeremId], [StartTime], [EndTime], [CreatedAt])
                    OUTPUT INSERTED.[Id]
                    VALUES (@userId, @roomId, @start, @end, SYSUTCDATETIME());
                    """,
                    ("@userId", userId), ("@roomId", roomId),
                    ("@start", new DateTime(2026, 11, 2, 12, 0, 0)),
                    ("@end", new DateTime(2026, 11, 2, 13, 0, 0)));

                await db.GetService<IMigrator>().MigrateAsync(utcMigration);
            }

            await using (var db = new AppDbContext(options))
            {
                var winter = await db.Foglalasok.AsNoTracking().SingleAsync(item => item.Id == winterBookingId);
                var summer = await db.Foglalasok.AsNoTracking().SingleAsync(item => item.Id == summerBookingId);
                var maintenance = await db.KarbantartasiIdosavok.AsNoTracking().SingleAsync(item => item.Id == maintenanceId);

                Assert.Equal(new DateTime(2026, 11, 2, 9, 15, 0, DateTimeKind.Utc), winter.StartTime);
                Assert.Equal(new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc), winter.EndTime);
                Assert.Equal(new DateTime(2026, 7, 1, 6, 0, 0, DateTimeKind.Utc), summer.StartTime);
                Assert.Equal(new DateTime(2026, 7, 1, 6, 45, 0, DateTimeKind.Utc), summer.EndTime);
                Assert.Equal(new DateTime(2026, 11, 2, 11, 0, 0, DateTimeKind.Utc), maintenance.StartTime);
                Assert.Equal(DateTimeKind.Utc, winter.StartTime.Kind);

                await db.GetService<IMigrator>().MigrateAsync(migrationBeforeUtc);
            }

            await using (var db = new AppDbContext(options))
            {
                var winter = await db.Foglalasok.AsNoTracking().SingleAsync(item => item.Id == winterBookingId);
                var summer = await db.Foglalasok.AsNoTracking().SingleAsync(item => item.Id == summerBookingId);

                Assert.Equal(new DateTime(2026, 11, 2, 10, 15, 0), winter.StartTime);
                Assert.Equal(new DateTime(2026, 7, 1, 8, 0, 0), summer.StartTime);
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

                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(
                    connectionString,
                    sqlOptions => sqlOptions.EnableRetryOnFailure()));
            });
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var operatorUser = await db.Felhasznalok.SingleAsync(user => user.Id == operatorId);
        var instructorUser = await db.Felhasznalok.SingleAsync(user => user.Id == instructorId);
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

        using var instructorClient = factory.CreateClient();
        instructorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", authService.GenerateJwtToken(instructorUser));
        var bookingDate = TestTime.LocalToday.AddDays(1);
        while (bookingDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            bookingDate = bookingDate.AddDays(1);
        }

        var bookingStart = bookingDate.AddHours(10).AddMinutes(15);
        using var bookingResponse = await instructorClient.PostAsJsonAsync(
            "/api/bookings",
            new CreateBookingRequestDto
            {
                TeremId = roomId,
                StartTime = bookingStart,
                EndTime = bookingStart.AddMinutes(45)
            });
        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        var booking = await bookingResponse.Content.ReadFromJsonAsync<BookingDto>();
        Assert.NotNull(booking);

        using var updateBookingResponse = await instructorClient.PutAsJsonAsync(
            $"/api/bookings/{booking.Id}",
            new UpdateBookingRequestDto { SpecialisIgenyek = "SQL retry strategy" });
        Assert.Equal(HttpStatusCode.OK, updateBookingResponse.StatusCode);

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

        using var cancelBookingResponse = await instructorClient.DeleteAsync($"/api/bookings/{booking.Id}");
        Assert.Equal(HttpStatusCode.OK, cancelBookingResponse.StatusCode);
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

    private static async Task<LegacySeedData> InsertLegacySeedDataAsync(
        AppDbContext db,
        DateTime testDate)
    {
        var instructorId = await InsertLegacyRowAsync(
            db,
            """
            INSERT INTO [Felhasznalok]
                ([Username], [Email], [JelszoHash], [Nev], [Role], [CreatedAt], [UpdatedAt])
            OUTPUT INSERTED.[Id]
            VALUES
                (@username, @email, N'integration-test-hash', N'Oktato', N'Oktato', @createdAt, @createdAt);
            """,
            ("@username", $"instructor_{Guid.NewGuid():N}"[..20]),
            ("@email", $"{Guid.NewGuid():N}@test.hu"),
            ("@createdAt", testDate));
        var operatorId = await InsertLegacyRowAsync(
            db,
            """
            INSERT INTO [Felhasznalok]
                ([Username], [Email], [JelszoHash], [Nev], [Role], [CreatedAt], [UpdatedAt])
            OUTPUT INSERTED.[Id]
            VALUES
                (@username, @email, N'integration-test-hash', N'Portas', N'Portas', @createdAt, @createdAt);
            """,
            ("@username", $"operator_{Guid.NewGuid():N}"[..20]),
            ("@email", $"{Guid.NewGuid():N}@test.hu"),
            ("@createdAt", testDate));
        var roomId = await InsertLegacyRowAsync(
            db,
            """
            INSERT INTO [Termek] ([Nev], [Kapacitas], [Leiras], [Aktivalva])
            OUTPUT INSERTED.[Id]
            VALUES (@name, 20, N'', 1);
            """,
            ("@name", $"Integration {Guid.NewGuid():N}"));
        var bookingId = await InsertLegacyRowAsync(
            db,
            """
            INSERT INTO [Foglalasok]
                ([FelhasznaloId], [TeremId], [StartTime], [EndTime], [Status], [CreatedAt], [UpdatedAt])
            OUTPUT INSERTED.[Id]
            VALUES (@instructorId, @roomId, @startTime, @endTime, N'Aktiv', @createdAt, @createdAt);
            """,
            ("@instructorId", instructorId),
            ("@roomId", roomId),
            ("@startTime", testDate.AddHours(-1)),
            ("@endTime", testDate.AddHours(1)),
            ("@createdAt", testDate));
        var keyId = await InsertLegacyRowAsync(
            db,
            """
            INSERT INTO [Kulcsok] ([Szam], [Tipus], [Aktivalva], [TeremId])
            OUTPUT INSERTED.[Id]
            VALUES (@number, N'Osztalykulcs', 1, @roomId);
            """,
            ("@number", $"IT-{Guid.NewGuid():N}"[..20]),
            ("@roomId", roomId));

        return new LegacySeedData(instructorId, operatorId, bookingId, keyId);
    }

    private static async Task<int> InsertLegacyRowAsync(
        AppDbContext db,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed record LegacySeedData(int InstructorId, int OperatorId, int BookingId, int KeyId);

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

    [SqlServerFact]
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

            // A SeedRoleUsersAndReliabilitySample migráció szerepkörönként egy fiókot hoz létre.
            var configuration = new ConfigurationBuilder().Build();
            var authService = new AuthService(
                db,
                configuration,
                new JwtSettings(
                    "sql-server-integration-tests",
                    "sql-server-integration-tests-api",
                    Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
                NullLogger<AuthService>.Instance,
                new AuditService(db, NullLogger<AuditService>.Instance));
            foreach (var (username, password, role) in new[]
                     {
                         ("admin", "Adminjelszo1!", RoleNames.Adminisztrator),
                         ("oktato", "Oktatojelszo1!", RoleNames.Oktato),
                         ("portas", "Portasjelszo1!", RoleNames.Portas),
                         ("igazgato", "Igazgatojelszo1!", RoleNames.UzemeltetesiIgazgato),
                         ("radli.richard", "RadliRichard1!", RoleNames.Oktato)
                     })
            {
                var login = await authService.LoginAsync(new LoginRequestDto { Username = username, Jelszo = password });
                Assert.NotNull(login);
                Assert.Equal(role, login.Felhasznalo.Role);
                var serializedLogin = System.Text.Json.JsonSerializer.Serialize(login);
                Assert.DoesNotContain(password, serializedLogin, StringComparison.Ordinal);
                Assert.DoesNotContain("JelszoHash", serializedLogin, StringComparison.Ordinal);
            }

            // Rádli Richárd: 34 nem időben visszaadott kulcs (+3 pont/db), a pontszám 100-nál megáll,
            // és két kulcs jelenleg is nála van.
            var radli = await db.Felhasznalok.SingleAsync(user => user.Username == "radli.richard");
            Assert.Equal("Rádli Richárd", radli.Nev);

            // A két mintaoktató eltérő PIN-t kap (GiveSampleInstructorsDistinctPins).
            var instructorPin = await db.Felhasznalok
                .Where(user => user.Username == "oktato")
                .Select(user => user.PinHash)
                .SingleAsync();
            Assert.True(BCryptHasher.Verify("4729", instructorPin));
            Assert.True(BCryptHasher.Verify("8153", radli.PinHash));
            Assert.False(BCryptHasher.Verify("1234", radli.PinHash));
            var now = DateTime.UtcNow;
            var startYear = now.Month >= 9 ? now.Year : now.Year - 1;
            var points = await db.MegbizhatosagiEsemenyek
                .Where(item => item.FelhasznaloId == radli.Id && item.Tanev == $"{startYear}/{startYear + 1}")
                .OrderBy(item => item.EsemenyIdopontja)
                .Select(item => item.Pont)
                .ToListAsync();
            Assert.Equal(34, points.Count);
            Assert.Equal(100, points.Aggregate(0, (score, point) => Math.Clamp(score + point, 0, 100)));

            var heldKeys = await db.Kulcsmozgasok
                .Where(movement =>
                    movement.AtvevoFelhasznaloId == radli.Id &&
                    !db.Kulcsmozgasok.Any(newer => newer.KulcsId == movement.KulcsId && newer.Idopont > movement.Idopont))
                .CountAsync(movement => movement.MozgasTipus == KeyMovementTypes.Issue);
            Assert.Equal(2, heldKeys);

            // Minden mintakiadáshoz tartozik egy lezárt foglalás (LinkSampleKeyIssuesToBookings).
            var bookings = await db.Foglalasok.Where(booking => booking.FelhasznaloId == radli.Id).ToListAsync();
            Assert.Equal(34, bookings.Count);
            Assert.All(bookings, booking => Assert.Equal(BookingRules.DoneStatus, booking.Status));
            Assert.False(await db.Kulcsmozgasok.AnyAsync(movement =>
                movement.AtvevoFelhasznaloId == radli.Id && movement.FoglalasId == null));
            Assert.False(await db.MegbizhatosagiEsemenyek.AnyAsync(item =>
                item.FelhasznaloId == radli.Id && item.FoglalasId == null));

            // A Down() kiveszi a mintaadatokat, a fiókokat pedig inaktiválja (az audit napló hivatkozik rájuk).
            await db.GetService<IMigrator>().MigrateAsync("20261117193607_AddUserSoftDeletePinLockAndMovementRoom");
            db.ChangeTracker.Clear();
            Assert.False((await db.Felhasznalok.SingleAsync(user => user.Username == "radli.richard")).Aktiv);
            Assert.False(await db.Kulcsok.AnyAsync(key => key.Szam.StartsWith("MINTA-")));
            Assert.False(await db.MegbizhatosagiEsemenyek.AnyAsync(item => item.FelhasznaloId == radli.Id));
        }
        finally
        {
            await DropIsolatedDatabaseAsync(databaseName);
        }
    }
}
