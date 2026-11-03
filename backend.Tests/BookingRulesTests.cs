using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace backend.Tests;

public class BookingRulesTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void ValidateTimeRange_RejectsNon45MinuteSlot()
    {
        var error = BookingRules.ValidateTimeRange(
            new DateTime(2026, 11, 2, 10, 15, 0),
            new DateTime(2026, 11, 2, 11, 15, 0));

        Assert.NotNull(error);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMaintenanceAndIgnoresCancelledBooking()
    {
        await using var db = CreateDb();
        db.Foglalasok.Add(new Foglalas
        {
            Id = 1, TeremId = 1, FelhasznaloId = 1,
            StartTime = new DateTime(2026, 11, 2, 10, 15, 0),
            EndTime = new DateTime(2026, 11, 2, 11, 0, 0),
            Status = "Megszuntetve"
        });
        db.KarbantartasiIdosavok.Add(new KarbantartasiIdosav
        {
            Id = 1, TeremId = 1, FelhasznaloId = 1,
            StartTime = new DateTime(2026, 11, 2, 11, 0, 0),
            EndTime = new DateTime(2026, 11, 2, 11, 45, 0)
        });
        await db.SaveChangesAsync();

        var error = await BookingRules.ValidateAsync(
            db, 1,
            new DateTime(2026, 11, 2, 11, 0, 0),
            new DateTime(2026, 11, 2, 11, 45, 0));

        Assert.Equal("A terem a megadott időszakban karbantartás alatt áll.", error);
    }

    [Fact]
    public void IsUserStatusChangeAllowed_RejectsAutomationStatuses()
    {
        Assert.False(BookingRules.IsUserStatusChangeAllowed("Aktiv", "NoShow", false));
        Assert.False(BookingRules.IsUserStatusChangeAllowed("Aktiv", "Kesz", true));
        Assert.True(BookingRules.IsUserStatusChangeAllowed("Aktiv", "Megszuntetve", false));
    }
}
