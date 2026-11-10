using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace backend.Tests;

public class BackgroundAutomationTests
{
    [Fact]
    public void AutomationDatabaseIndexesAndKeyRecipientRelationAreConfigured()
    {
        using var db = CreateDb();
        var notificationIndex = db.Model
            .FindEntityType(typeof(Ertesites))!
            .GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(Ertesites.AutomationKey)));
        var penaltyIndex = db.Model
            .FindEntityType(typeof(MegbizhatosagiEsemeny))!
            .GetIndexes()
            .Single(index => index.Properties.Any(
                property => property.Name == nameof(MegbizhatosagiEsemeny.KiadasiKulcsmozgasId)));
        var unresolvedAuditIndex = db.Model
            .FindEntityType(typeof(AuditLog))!
            .GetIndexes()
            .Single(index => index.Properties.Any(
                property => property.Name == nameof(AuditLog.AutomationKey)));
        var movementEntity = db.Model.FindEntityType(typeof(Kulcsmozgas))!;

        Assert.True(notificationIndex.IsUnique);
        Assert.True(penaltyIndex.IsUnique);
        Assert.True(unresolvedAuditIndex.IsUnique);
        Assert.Contains(
            movementEntity.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(
                property => property.Name == nameof(Kulcsmozgas.AtvevoFelhasznaloId)));
    }

    [Fact]
    public async Task NoShow_IsNotSetBeforeConfiguredWindow_AndIsSetAfterward()
    {
        await using var db = CreateDb();
        var (booking, _) = await SeedBookingAsync(db, "Aktiv", new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc));
        var processor = new NoShowProcessor(db, NullLogger<NoShowProcessor>.Instance);

        Assert.Equal(0, await processor.ProcessAsync(new DateTime(2026, 4, 4, 10, 14, 59, DateTimeKind.Utc), default));
        Assert.Equal("Aktiv", booking.Status);
        Assert.Equal(1, await processor.ProcessAsync(new DateTime(2026, 4, 4, 10, 15, 0, DateTimeKind.Utc), default));
        Assert.Equal("NoShow", booking.Status);
        var audit = await db.AuditLogok.SingleAsync();
        Assert.Equal("UPDATE", audit.MuveletTipus);
        Assert.Equal(booking.Id.ToString(), audit.EntitasId);
        Assert.Equal("Aktiv", audit.RegiErtek);
        Assert.Equal("NoShow", audit.UjErtek);
    }

    [Fact]
    public async Task NoShow_DoesNotChangeBookingWhenKeyWasCollectedOrBookingWasCancelled()
    {
        await using var db = CreateDb();
        var (bookingWithKey, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc));
        var (cancelledBooking, _) = await SeedBookingAsync(
            db, "Megszuntetve", new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc));
        var key = new Kulcs
        {
            Szam = "NS-1",
            Tipus = "Osztalykulcs",
            TeremId = bookingWithKey.TeremId,
            Aktivalva = true
        };
        db.Kulcsok.Add(key);
        await db.SaveChangesAsync();
        db.Kulcsmozgasok.Add(new Kulcsmozgas
        {
            KulcsId = key.Id,
            FoglalasId = bookingWithKey.Id,
            MozgasTipus = "Kiadás",
            Idopont = new DateTime(2026, 4, 4, 10, 1, 0, DateTimeKind.Utc),
            OperátorId = operatorUser.Id
        });
        await db.SaveChangesAsync();

        var processor = new NoShowProcessor(db, NullLogger<NoShowProcessor>.Instance);
        var count = await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 10, 16, 0, DateTimeKind.Utc), default);

        Assert.Equal(0, count);
        Assert.Equal("Aktiv", bookingWithKey.Status);
        Assert.Equal("Megszuntetve", cancelledBooking.Status);
    }

    [Fact]
    public async Task NoShow_UsesUpdatedSystemParameterOnNextRun()
    {
        await using var db = CreateDb();
        var (booking, _) = await SeedBookingAsync(db, "Aktiv", new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc));
        var parameter = new Rendszerparameter
        {
            Kulcs = "no_show_idokorlat_perc",
            Ertek = "20"
        };
        db.Rendszerparameterek.Add(parameter);
        await db.SaveChangesAsync();
        var processor = new NoShowProcessor(db, NullLogger<NoShowProcessor>.Instance);

        Assert.Equal(0, await processor.ProcessAsync(new DateTime(2026, 4, 4, 10, 15, 0, DateTimeKind.Utc), default));
        parameter.Ertek = "10";
        await db.SaveChangesAsync();
        Assert.Equal(1, await processor.ProcessAsync(new DateTime(2026, 4, 4, 10, 15, 0, DateTimeKind.Utc), default));
        Assert.Equal("NoShow", booking.Status);
    }

    [Fact]
    public async Task NoShow_DoesNotMarkNoShow_WhenKeyWasCollectedBeforeStartTime()
    {
        await using var db = CreateDb();
        var startTime = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc);
        var (booking, operatorUser) = await SeedBookingAsync(db, "Aktiv", startTime);

        var key = await AddKeyAsync(db, booking.TeremId, "NS-EARLY");
        // Key collected 10 minutes BEFORE start time (09:50)
        await AddMovementAsync(
            db,
            key.Id,
            booking.Id,
            operatorUser.Id,
            "Kiadás",
            startTime.AddMinutes(-10),
            booking.FelhasznaloId);

        var processor = new NoShowProcessor(db, NullLogger<NoShowProcessor>.Instance);
        // Run processor at 10:20 (well past the 15-minute grace period)
        var count = await processor.ProcessAsync(startTime.AddMinutes(20), default);

        Assert.Equal(0, count);
        Assert.Equal("Aktiv", booking.Status);
    }

    [Fact]
    public async Task NoShow_DoesNotMarkNoShow_WhenRoomHasActiveUnreturnedKeyMovement()
    {
        await using var db = CreateDb();
        var startTime = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc);
        var (booking, operatorUser) = await SeedBookingAsync(db, "Aktiv", startTime);

        var key = await AddKeyAsync(db, booking.TeremId, "NS-ACTIVE-KEY");
        // Active key checkout on the room (without explicit FoglalasId link, e.g. ad-hoc or previous)
        await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Kiadás",
            startTime.AddMinutes(5),
            booking.FelhasznaloId);

        var processor = new NoShowProcessor(db, NullLogger<NoShowProcessor>.Instance);
        // At 10:25, past the grace window, but room key is actively issued!
        var count = await processor.ProcessAsync(startTime.AddMinutes(25), default);

        Assert.Equal(0, count);
        Assert.Equal("Aktiv", booking.Status);
    }

    [Fact]
    public async Task NoShow_SetsAuditLogAndAllowsRebooking()
    {
        await using var db = CreateDb();
        var startTime = new DateTime(2026, 4, 4, 8, 45, 0, DateTimeKind.Utc); // 08:45 is a valid slot
        var endTime = startTime.AddMinutes(45);
        var (booking, _) = await SeedBookingAsync(db, "Aktiv", startTime);
        booking.EndTime = endTime;
        await db.SaveChangesAsync();

        var processor = new NoShowProcessor(db, NullLogger<NoShowProcessor>.Instance);
        var count = await processor.ProcessAsync(startTime.AddMinutes(16), default);

        Assert.Equal(1, count);
        Assert.Equal("NoShow", booking.Status);

        // Verify AuditLog
        var audit = await db.AuditLogok.SingleOrDefaultAsync(a => a.AutomationKey == $"NoShow:{booking.Id}");
        Assert.NotNull(audit);
        Assert.Equal("UPDATE", audit.MuveletTipus);
        Assert.Equal("Foglalas", audit.EntitasTipus);
        Assert.Equal(booking.Id.ToString(), audit.EntitasId);

        // Verify that after NoShow, the room is released and BookingRules allows new booking for the same slot
        var validationResult = await BookingRules.ValidateAsync(db, booking.TeremId, startTime, endTime);
        Assert.Null(validationResult);
    }

    [Fact]
    public async Task BookingExpiryNotification_IsCreatedOnceForActiveBooking()
    {
        await using var db = CreateDb();
        var endTime = new DateTime(2026, 4, 4, 10, 10, 0, DateTimeKind.Utc);
        var (activeBooking, _) = await SeedBookingAsync(db, "Aktiv", endTime.AddHours(-1));
        activeBooking.EndTime = endTime;
        var (cancelledBooking, _) = await SeedBookingAsync(db, "Megszuntetve", endTime.AddHours(-1).AddDays(1));
        cancelledBooking.EndTime = endTime.AddDays(1);
        var (expiredBooking, _) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 8, 0, 0, DateTimeKind.Utc));
        expiredBooking.EndTime = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        var processor = new BookingExpiryNotificationProcessor(
            db, NullLogger<BookingExpiryNotificationProcessor>.Instance);
        var now = new DateTime(2026, 4, 4, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(1, await processor.ProcessAsync(now, default));
        Assert.Equal(0, await processor.ProcessAsync(now, default));
        var notification = await db.Ertesitesek.SingleAsync();
        Assert.Equal(activeBooking.Id, notification.FoglalasId);
        Assert.Equal("BeforeEnd", notification.ErtesitesTipus);
        Assert.Equal("Kesz", notification.Allapot);
        Assert.Equal($"BeforeEnd:{activeBooking.Id}", notification.AutomationKey);
        Assert.Equal(now, notification.IdoPont);
        Assert.DoesNotContain(await db.Ertesitesek.ToListAsync(), item => item.FoglalasId == cancelledBooking.Id);
        Assert.DoesNotContain(await db.Ertesitesek.ToListAsync(), item => item.FoglalasId == expiredBooking.Id);
        var audit = await db.AuditLogok.SingleAsync();
        Assert.Equal("AUTOMATIC_NOTIFICATION", audit.MuveletTipus);
        Assert.Equal(activeBooking.Id.ToString(), audit.EntitasId);
        Assert.Equal($"BeforeEndNotification:{activeBooking.Id}", audit.AutomationKey);
    }

    [Fact]
    public async Task DailyClosing_CreatesAuditedThreePointEventOncePerUnreturnedIssue()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-1");
        var issueTime = new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc);
        var issue = await AddMovementAsync(db, key.Id, booking.Id, operatorUser.Id, "Kiadás", issueTime);
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
        await db.SaveChangesAsync();
        var processor = new DailyKeyClosingProcessor(
            db,
            new ConfigurationBuilder().Build(),
            NullLogger<DailyKeyClosingProcessor>.Instance);
        var closingTime = new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc);

        Assert.Equal(1, await processor.ProcessAsync(closingTime, default));
        Assert.Equal(0, await processor.ProcessAsync(closingTime.AddMinutes(1), default));

        var reliabilityEvent = await db.MegbizhatosagiEsemenyek.SingleAsync();
        Assert.Equal(booking.FelhasznaloId, reliabilityEvent.FelhasznaloId);
        Assert.Equal(key.Id, reliabilityEvent.KulcsId);
        Assert.Equal(booking.Id, reliabilityEvent.FoglalasId);
        Assert.Equal(issue.Id, reliabilityEvent.KiadasiKulcsmozgasId);
        Assert.Equal("LeNemAdott", reliabilityEvent.EsemenyTipus);
        Assert.Equal(3, reliabilityEvent.Pont);
        Assert.True(reliabilityEvent.Automatikus);
        Assert.Equal(closingTime, reliabilityEvent.EredetiHatarido);
        Assert.Single(await db.AuditLogok
            .Where(item => item.AutomationKey == null)
            .ToListAsync());
    }

    [Fact]
    public async Task DailyClosing_NotifiesOnlyPortersAtTheDeadlineAndDoesNotDuplicateNotifications()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        var secondPorter = CreateUser("Portas");
        db.Felhasznalok.Add(secondPorter);
        var key = await AddKeyAsync(db, booking.TeremId, "DC-NOTIFY");
        var issue = await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Kiadás",
            new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc),
            booking.FelhasznaloId);
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
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
        var beforeDeadline = new DateTime(2026, 4, 4, 21, 59, 59, DateTimeKind.Utc);
        var deadline = new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc);

        await processor.ProcessAsync(beforeDeadline, default);
        Assert.Empty(await db.Ertesitesek.ToListAsync());

        await processor.ProcessAsync(deadline, default);
        await processor.ProcessAsync(deadline.AddMinutes(1), default);

        var notifications = await db.Ertesitesek
            .OrderBy(item => item.CimzettFelhasznaloId)
            .ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, notification =>
        {
            Assert.Equal(issue.Id, notification.KulcsmozgasId);
            Assert.Equal("EveningKeyReturn", notification.ErtesitesTipus);
            Assert.Equal(deadline, notification.Hatarido);
            Assert.Equal("Kesz", notification.Allapot);
        });
        Assert.Equal(
            new[] { operatorUser.Id, secondPorter.Id }.Order(),
            notifications.Select(notification => notification.CimzettFelhasznaloId!.Value).Order());
        Assert.DoesNotContain(
            notifications,
            notification => notification.CimzettFelhasznaloId == booking.FelhasznaloId);
        Assert.Equal(
            1,
            await db.AuditLogok.CountAsync(item =>
                item.AutomationKey == $"EveningKeyReturn:{issue.Id}"));
    }

    [Fact]
    public async Task DailyClosing_DoesNotNotifyAKeyThatWasReturnedBeforeTheDeadline()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-RETURNED");
        await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Kiadás",
            new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc),
            booking.FelhasznaloId);
        await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Visszavétel",
            new DateTime(2026, 4, 4, 21, 30, 0, DateTimeKind.Utc));
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
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

        await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc),
            default);

        Assert.Empty(await db.Ertesitesek.ToListAsync());
    }

    [Fact]
    public async Task DailyClosing_UsesBudapestLocalTimeForEveningNotificationDeadline()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 18, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-BUDAPEST");
        await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Kiadás",
            new DateTime(2026, 4, 4, 19, 0, 0, DateTimeKind.Utc),
            booking.FelhasznaloId);
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
        await db.SaveChangesAsync();
        var processor = new DailyKeyClosingProcessor(
            db,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Automation:TimeZoneId"] = "Europe/Budapest"
                })
                .Build(),
            NullLogger<DailyKeyClosingProcessor>.Instance);

        await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 19, 59, 59, DateTimeKind.Utc),
            default);
        Assert.Empty(await db.Ertesitesek.ToListAsync());

        await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc),
            default);

        var notification = Assert.Single(await db.Ertesitesek.ToListAsync());
        Assert.Equal(new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc), notification.Hatarido);
    }

    [Fact]
    public async Task DailyClosing_UsesRecordedRecipientForBookinglessIssue()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-BOOKINGLESS");
        var issue = await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Kiadás",
            new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc),
            booking.FelhasznaloId);
        await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Kiadás",
            new DateTime(2026, 4, 4, 21, 1, 0, DateTimeKind.Utc));
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
        await db.SaveChangesAsync();
        var processor = new DailyKeyClosingProcessor(
            db,
            new ConfigurationBuilder().Build(),
            NullLogger<DailyKeyClosingProcessor>.Instance);

        Assert.Equal(1, await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc), default));

        var reliabilityEvent = await db.MegbizhatosagiEsemenyek.SingleAsync();
        Assert.Equal(booking.FelhasznaloId, reliabilityEvent.FelhasznaloId);
        Assert.Equal(issue.Id, reliabilityEvent.KiadasiKulcsmozgasId);
        Assert.Equal(3, reliabilityEvent.Pont);
        Assert.Empty(await db.AuditLogok
            .Where(item => item.AutomationKey ==
                $"DailyKeyClosing:UnresolvedRecipient:{issue.Id}")
            .ToListAsync());
    }

    [Fact]
    public async Task DailyClosing_AuditsUnresolvedBookinglessIssueOnceWithoutPenalty()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-UNRESOLVED");
        var issue = await AddMovementAsync(
            db,
            key.Id,
            bookingId: null,
            operatorUser.Id,
            "Kiadás",
            new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc));
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
        await db.SaveChangesAsync();
        var processor = new DailyKeyClosingProcessor(
            db,
            new ConfigurationBuilder().Build(),
            NullLogger<DailyKeyClosingProcessor>.Instance);
        var closingTime = new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc);

        Assert.Equal(0, await processor.ProcessAsync(closingTime, default));
        Assert.Equal(0, await processor.ProcessAsync(closingTime.AddMinutes(1), default));

        Assert.Empty(await db.MegbizhatosagiEsemenyek.ToListAsync());
        var audit = await db.AuditLogok.SingleAsync(
            item => item.AutomationKey == $"DailyKeyClosing:UnresolvedRecipient:{issue.Id}");
        Assert.Equal(issue.Id.ToString(), audit.EntitasId);
        Assert.Equal($"DailyKeyClosing:UnresolvedRecipient:{issue.Id}", audit.AutomationKey);
        Assert.Contains("pontlevonás nem történt", audit.Indoklas);
    }

    [Fact]
    public async Task DailyClosing_DoesNotPenalizeKeyReturnedBeforeConfiguredClosingTime()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-2");
        await AddMovementAsync(
            db, key.Id, booking.Id, operatorUser.Id, "Kiadás",
            new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc));
        await AddMovementAsync(
            db, key.Id, booking.Id, operatorUser.Id, "Visszavétel",
            new DateTime(2026, 4, 4, 21, 59, 0, DateTimeKind.Utc));
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
        await db.SaveChangesAsync();
        var processor = new DailyKeyClosingProcessor(
            db,
            new ConfigurationBuilder().Build(),
            NullLogger<DailyKeyClosingProcessor>.Instance);

        Assert.Equal(0, await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 22, 0, 0, DateTimeKind.Utc), default));
        Assert.Empty(await db.MegbizhatosagiEsemenyek.ToListAsync());
    }

    [Fact]
    public async Task DailyClosing_UsesUpdatedClosingTime()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 19, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-3");
        await AddMovementAsync(
            db, key.Id, booking.Id, operatorUser.Id, "Kiadás",
            new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        var parameter = new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        };
        db.Rendszerparameterek.Add(parameter);
        await db.SaveChangesAsync();
        var processor = new DailyKeyClosingProcessor(
            db,
            new ConfigurationBuilder().Build(),
            NullLogger<DailyKeyClosingProcessor>.Instance);

        Assert.Equal(0, await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc), default));
        parameter.Ertek = "21:00";
        await db.SaveChangesAsync();
        Assert.Equal(1, await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 21, 0, 0, DateTimeKind.Utc), default));
    }

    [Fact]
    public async Task DailyClosing_CatchesUpAfterRestartPastTheClosingTime()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 19, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-5");
        await AddMovementAsync(
            db, key.Id, booking.Id, operatorUser.Id, "Kiadás",
            new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc));
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
        await db.SaveChangesAsync();
        var processor = new DailyKeyClosingProcessor(
            db,
            new ConfigurationBuilder().Build(),
            NullLogger<DailyKeyClosingProcessor>.Instance);

        Assert.Equal(1, await processor.ProcessAsync(
            new DateTime(2026, 4, 5, 8, 0, 0, DateTimeKind.Utc), default));
        Assert.Single(await db.MegbizhatosagiEsemenyek.ToListAsync());
    }

    [Fact]
    public async Task DailyClosing_UsesConfiguredTimeZoneForClosingBoundary()
    {
        await using var db = CreateDb();
        var (booking, operatorUser) = await SeedBookingAsync(
            db, "Aktiv", new DateTime(2026, 4, 4, 18, 0, 0, DateTimeKind.Utc));
        var key = await AddKeyAsync(db, booking.TeremId, "DC-4");
        await AddMovementAsync(
            db, key.Id, booking.Id, operatorUser.Id, "Kiadás",
            new DateTime(2026, 4, 4, 19, 0, 0, DateTimeKind.Utc));
        db.Rendszerparameterek.Add(new Rendszerparameter
        {
            Kulcs = "napi_kulcsleadasi_zarasi_idopont",
            Ertek = "22:00"
        });
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Automation:TimeZoneId"] = "Europe/Budapest" })
            .Build();
        var processor = new DailyKeyClosingProcessor(
            db, configuration, NullLogger<DailyKeyClosingProcessor>.Instance);

        Assert.Equal(0, await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 19, 59, 0, DateTimeKind.Utc), default));
        Assert.Equal(1, await processor.ProcessAsync(
            new DateTime(2026, 4, 4, 20, 0, 0, DateTimeKind.Utc), default));
    }

    [Fact]
    public async Task BackgroundWorker_LogsFailureAndAcceptsNextCycle()
    {
        var worker = new FailingOnceBackgroundService(
            TimeProvider.System,
            NullLogger<FailingOnceBackgroundService>.Instance);

        await worker.RunOnceAsync();
        await worker.RunOnceAsync();

        Assert.Equal(2, worker.InvocationCount);
    }

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(Foglalas Booking, Felhasznalo Operator)> SeedBookingAsync(
        AppDbContext db,
        string status,
        DateTime startTime)
    {
        var instructor = CreateUser("Oktato");
        var operatorUser = CreateUser("Portas");
        var room = new Terem { Nev = Guid.NewGuid().ToString("N"), Kapacitas = 30, Aktivalva = true };
        db.Felhasznalok.AddRange(instructor, operatorUser);
        db.Termek.Add(room);
        await db.SaveChangesAsync();
        var booking = new Foglalas
        {
            FelhasznaloId = instructor.Id,
            TeremId = room.Id,
            StartTime = startTime,
            EndTime = startTime.AddHours(1),
            Status = status
        };
        db.Foglalasok.Add(booking);
        await db.SaveChangesAsync();
        return (booking, operatorUser);
    }

    private static Felhasznalo CreateUser(string role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new Felhasznalo
        {
            Username = suffix[..12],
            Email = $"{suffix}@test.hu",
            JelszoHash = "hash",
            Nev = role,
            Role = role
        };
    }

    private static async Task<Kulcs> AddKeyAsync(AppDbContext db, int roomId, string number)
    {
        var key = new Kulcs
        {
            Szam = number,
            Tipus = "Osztalykulcs",
            TeremId = roomId,
            Aktivalva = true
        };
        db.Kulcsok.Add(key);
        await db.SaveChangesAsync();
        return key;
    }

    private static async Task<Kulcsmozgas> AddMovementAsync(
        AppDbContext db,
        int keyId,
        int? bookingId,
        int operatorId,
        string type,
        DateTime time,
        int? recipientId = null)
    {
        var movement = new Kulcsmozgas
        {
            KulcsId = keyId,
            FoglalasId = bookingId,
            AtvevoFelhasznaloId = recipientId,
            OperátorId = operatorId,
            MozgasTipus = type,
            Idopont = time
        };
        db.Kulcsmozgasok.Add(movement);
        await db.SaveChangesAsync();
        return movement;
    }

    private sealed class FailingOnceBackgroundService : AutomationBackgroundService
    {
        public FailingOnceBackgroundService(
            TimeProvider timeProvider,
            Microsoft.Extensions.Logging.ILogger<FailingOnceBackgroundService> logger)
            : base(timeProvider, logger)
        {
        }

        public int InvocationCount { get; private set; }
        protected override string JobName => "Test failure handling";

        protected override Task ProcessOnceAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (InvocationCount == 1)
            {
                throw new InvalidOperationException("Test failure");
            }

            return Task.CompletedTask;
        }
    }
}
