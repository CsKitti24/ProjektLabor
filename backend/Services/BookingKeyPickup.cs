using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services;

/// <summary>
/// Eldönti, mely foglalásokhoz vették fel a kulcsot. Egy foglaláshoz akkor tartozik kulcsfelvétel, ha
/// - van hozzá kötött kiadás (FoglalasId), vagy
/// - a foglaló oktató a foglalás termére kapott kulcsot a foglalás idősávjában
///   (legkorábban <see cref="BookingRules.KeyIssueLeadTime"/>-mal a kezdés előtt), akkor is,
///   ha a kiadást nem kötötték a foglaláshoz (pl. régebbi kiadások).
/// </summary>
public static class BookingKeyPickup
{
    public static async Task<HashSet<int>> GetBookingIdsWithKeyPickupAsync(
        AppDbContext db,
        IReadOnlyCollection<Foglalas> bookings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (bookings.Count == 0)
        {
            return [];
        }

        var bookingIds = bookings.Select(booking => (int?)booking.Id).ToList();
        var result = (await db.Kulcsmozgasok
                .AsNoTracking()
                .Where(movement =>
                    bookingIds.Contains(movement.FoglalasId) &&
                    (movement.MozgasTipus == KeyMovementTypes.Issue || movement.MozgasTipus == KeyMovementTypes.LegacyIssue) &&
                    movement.Idopont <= nowUtc)
                .Select(movement => movement.FoglalasId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var remaining = bookings.Where(booking => !result.Contains(booking.Id)).ToList();
        if (remaining.Count == 0)
        {
            return result;
        }

        var ownerIds = remaining.Select(booking => (int?)booking.FelhasznaloId).Distinct().ToList();
        var earliest = remaining.Min(booking => booking.StartTime).Subtract(BookingRules.KeyIssueLeadTime);
        var unlinkedIssues = await db.Kulcsmozgasok
            .AsNoTracking()
            .Where(movement =>
                movement.FoglalasId == null &&
                ownerIds.Contains(movement.AtvevoFelhasznaloId) &&
                (movement.MozgasTipus == KeyMovementTypes.Issue || movement.MozgasTipus == KeyMovementTypes.LegacyIssue) &&
                movement.Idopont >= earliest &&
                movement.Idopont <= nowUtc)
            .Select(movement => new
            {
                movement.AtvevoFelhasznaloId,
                RoomId = movement.TeremId ?? movement.Kulcs.TeremId,
                movement.Idopont
            })
            .ToListAsync(cancellationToken);

        foreach (var booking in remaining)
        {
            if (unlinkedIssues.Any(issue =>
                    issue.AtvevoFelhasznaloId == booking.FelhasznaloId &&
                    issue.RoomId == booking.TeremId &&
                    issue.Idopont >= booking.StartTime.Subtract(BookingRules.KeyIssueLeadTime) &&
                    issue.Idopont < booking.EndTime))
            {
                result.Add(booking.Id);
            }
        }

        return result;
    }
}
