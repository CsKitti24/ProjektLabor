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
            .Where(booking => booking.Status == BookingRules.ActiveStatus && booking.StartTime <= cutoff)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        // 1. Foglalások, amelyekhez már felvették a kulcsot (korai felvétel is számít,
        //    és a foglaláshoz nem kötött, de az oktatónak erre a teremre kiadott kulcs is).
        var collectedBookingIds = await BookingKeyPickup.GetBookingIdsWithKeyPickupAsync(
            _db, candidates, nowUtc, cancellationToken);

        // 2. Termek, amelyeknek valamelyik (saját vagy mester-) kulcsa jelenleg ki van adva.
        var roomIds = candidates.Select(booking => booking.TeremId).Distinct().ToList();
        var roomKeys = await _db.Kulcsok
            .Where(key => roomIds.Contains(key.TeremId))
            .Select(key => new { KulcsId = key.Id, key.TeremId })
            .ToListAsync(cancellationToken);
        var masterKeys = await _db.KulcsTermek
            .Where(link => roomIds.Contains(link.TeremId))
            .Select(link => new { link.KulcsId, link.TeremId })
            .ToListAsync(cancellationToken);
        var keyRooms = roomKeys.Concat(masterKeys).Distinct().ToList();
        var keyIds = keyRooms.Select(item => item.KulcsId).Distinct().ToList();

        var issuedKeyIds = (await _db.Kulcsmozgasok
                .Where(movement =>
                    keyIds.Contains(movement.KulcsId) &&
                    movement.Idopont <= nowUtc &&
                    !_db.Kulcsmozgasok.Any(newer =>
                        newer.KulcsId == movement.KulcsId &&
                        newer.Idopont <= nowUtc &&
                        (newer.Idopont > movement.Idopont ||
                         (newer.Idopont == movement.Idopont && newer.Id > movement.Id))))
                .Where(latest => latest.MozgasTipus == KeyMovementTypes.Issue || latest.MozgasTipus == KeyMovementTypes.LegacyIssue)
                .Select(latest => latest.KulcsId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var roomsWithIssuedKey = keyRooms
            .Where(item => issuedKeyIds.Contains(item.KulcsId))
            .Select(item => item.TeremId)
            .ToHashSet();

        var candidateAutomationKeys = candidates.Select(booking => $"NoShow:{booking.Id}").ToList();
        var auditedKeys = (await _db.AuditLogok
                .Where(entry => entry.AutomationKey != null && candidateAutomationKeys.Contains(entry.AutomationKey))
                .Select(entry => entry.AutomationKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var updatedCount = 0;
        foreach (var booking in candidates)
        {
            if (collectedBookingIds.Contains(booking.Id))
            {
                continue;
            }

            if (roomsWithIssuedKey.Contains(booking.TeremId))
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
            if (!auditedKeys.Contains(automationKey))
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
