using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace backend.Tests;

public class AppTimeTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void ToUtc_TreatsUnspecifiedAsBudapestTime_InWinterAndSummer()
    {
        Assert.Equal(
            new DateTime(2026, 11, 2, 9, 15, 0, DateTimeKind.Utc),
            AppTime.ToUtc(new DateTime(2026, 11, 2, 10, 15, 0)));
        Assert.Equal(
            new DateTime(2026, 7, 1, 6, 0, 0, DateTimeKind.Utc),
            AppTime.LocalToUtc(new DateOnly(2026, 7, 1), new TimeOnly(8, 0)));
        Assert.Equal(DateTimeKind.Utc, AppTime.ToUtc(new DateTime(2026, 11, 2, 10, 15, 0)).Kind);
    }

    [Fact]
    public void ValidateTimeRange_ChecksSlotsInLocalTime_ForUtcInput()
    {
        // 09:15 UTC = 10:15 helyi idő (érvényes sáv), 10:15 UTC = 11:15 helyi idő (érvénytelen).
        Assert.Null(BookingRules.ValidateTimeRange(
            new DateTime(2026, 11, 2, 9, 15, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc)));
        Assert.NotNull(BookingRules.ValidateTimeRange(
            new DateTime(2026, 11, 2, 10, 15, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 2, 11, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task DbContext_StoresLocalInputAsUtc_AndReadsBackWithUtcKind()
    {
        await using var db = CreateDb();
        db.Foglalasok.Add(new Foglalas
        {
            Id = 1,
            TeremId = 1,
            FelhasznaloId = 1,
            StartTime = new DateTime(2026, 11, 2, 10, 15, 0),
            EndTime = new DateTime(2026, 11, 2, 11, 0, 0),
            Status = "Aktiv"
        });
        await db.SaveChangesAsync();

        var stored = await db.Foglalasok.AsNoTracking().SingleAsync();

        Assert.Equal(DateTimeKind.Utc, stored.StartTime.Kind);
        Assert.Equal(new DateTime(2026, 11, 2, 9, 15, 0, DateTimeKind.Utc), stored.StartTime);
    }

    [Fact]
    public async Task NoShow_UsesRealInstant_ForBookingEnteredInLocalTime()
    {
        await using var db = CreateDb();
        var user = new Felhasznalo
        {
            Id = 1,
            Username = "oktato",
            Email = "oktato@example.invalid",
            Nev = "Oktato",
            JelszoHash = "x",
            Role = "Oktato"
        };
        db.Felhasznalok.Add(user);
        db.Termek.Add(new Terem { Id = 1, Nev = "T1", Aktivalva = true });
        db.Foglalasok.Add(new Foglalas
        {
            Id = 1,
            TeremId = 1,
            FelhasznaloId = 1,
            // 10:15 helyi idő (téli időszámítás) = 09:15 UTC.
            StartTime = new DateTime(2026, 11, 2, 10, 15, 0),
            EndTime = new DateTime(2026, 11, 2, 11, 0, 0),
            Status = "Aktiv"
        });
        await db.SaveChangesAsync();

        var processor = new NoShowProcessor(db, NullLogger<NoShowProcessor>.Instance);

        Assert.Equal(0, await processor.ProcessAsync(
            new DateTime(2026, 11, 2, 9, 29, 59, DateTimeKind.Utc), default));
        Assert.Equal(1, await processor.ProcessAsync(
            new DateTime(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc), default));
    }
}
