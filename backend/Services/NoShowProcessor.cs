using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;

namespace ProjektLabor.Services;

public sealed class NoShowProcessor
{
    private readonly AppDbContext _db;
    private readonly ILogger<NoShowProcessor> _logger;

    public NoShowProcessor(AppDbContext db, ILogger<NoShowProcessor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> ProcessAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var limitMinutes = await AutomationParameterReader.GetNoShowLimitMinutesAsync(
            _db, _logger, cancellationToken);
        var cutoff = nowUtc.AddMinutes(-limitMinutes);
        var candidates = await _db.Foglalasok
            .Where(booking => booking.Status == "Aktiv" && booking.StartTime <= cutoff)
            .ToListAsync(cancellationToken);

        var updatedCount = 0;
        foreach (var booking in candidates)
        {
            var keyWasCollected = await _db.Kulcsmozgasok.AnyAsync(
                movement =>
                    movement.FoglalasId == booking.Id &&
                    (movement.MozgasTipus == "Kiadás" || movement.MozgasTipus == "Kiadas") &&
                    movement.Idopont >= booking.StartTime &&
                    movement.Idopont <= nowUtc,
                cancellationToken);
            if (keyWasCollected)
            {
                continue;
            }

            booking.Status = "NoShow";
            booking.UpdatedAt = nowUtc;
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Marked {BookingCount} bookings as no-show at {ProcessedAtUtc}.",
                updatedCount,
                nowUtc);
        }

        return updatedCount;
    }
}
