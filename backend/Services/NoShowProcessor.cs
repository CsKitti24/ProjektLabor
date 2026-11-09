using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

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
            // 1. Check if key was collected for this booking (including early pickup before start time)
            var keyWasCollected = await _db.Kulcsmozgasok.AnyAsync(
                movement =>
                    movement.FoglalasId == booking.Id &&
                    (movement.MozgasTipus == "Kiadás" || movement.MozgasTipus == "Kiadas") &&
                    movement.Idopont <= nowUtc,
                cancellationToken);
            if (keyWasCollected)
            {
                continue;
            }

            // 2. Check if room currently has any active key movement (keys assigned to this room that are currently issued and not returned)
            var roomKeyIds = await _db.Kulcsok
                .Where(k => k.TeremId == booking.TeremId)
                .Select(k => k.Id)
                .ToListAsync(cancellationToken);

            var masterKeyIds = await _db.KulcsTermek
                .Where(kt => kt.TeremId == booking.TeremId)
                .Select(kt => kt.KulcsId)
                .ToListAsync(cancellationToken);

            var allRelevantKeyIds = roomKeyIds.Concat(masterKeyIds).Distinct().ToList();

            var hasActiveKeyMovementForRoom = false;
            foreach (var keyId in allRelevantKeyIds)
            {
                var latestMovement = await _db.Kulcsmozgasok
                    .Where(m => m.KulcsId == keyId && m.Idopont <= nowUtc)
                    .OrderByDescending(m => m.Idopont)
                    .FirstOrDefaultAsync(cancellationToken);

                if (latestMovement != null && (latestMovement.MozgasTipus == "Kiadás" || latestMovement.MozgasTipus == "Kiadas"))
                {
                    hasActiveKeyMovementForRoom = true;
                    break;
                }
            }

            if (hasActiveKeyMovementForRoom)
            {
                _logger.LogInformation(
                    "Booking {BookingId} for room {RoomId} has an active key movement, skipping NoShow marking.",
                    booking.Id,
                    booking.TeremId);
                continue;
            }

            booking.Status = "NoShow";
            booking.UpdatedAt = nowUtc;
            var automationKey = $"NoShow:{booking.Id}";
            var alreadyAudited = await _db.AuditLogok.AnyAsync(a => a.AutomationKey == automationKey, cancellationToken);
            if (!alreadyAudited)
            {
                _db.AuditLogok.Add(new AuditLog
                {
                    MuveletTipus = "UPDATE",
                    EntitasTipus = "Foglalas",
                    EntitasId = booking.Id.ToString(),
                    AutomationKey = automationKey,
                    RegiErtek = "Aktiv",
                    UjErtek = "NoShow",
                    Indoklas = "Automatikus No-Show státuszváltás: a türelmi időn belül nem történt kulcsfelvétel.",
                    LetrehozasIdeje = nowUtc
                });
            }
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
