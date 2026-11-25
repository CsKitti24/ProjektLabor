using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services;

/// <summary>
/// Eldönti, mely foglalásokhoz vették fel a kulcsot. Egy foglaláshoz akkor tartozik kulcsfelvétel, ha
/// - van hozzá kötött kiadás (FoglalasId), vagy
/// - a foglaló oktató a foglalás termére kapott kulcsot a foglalás idősávjában
///   (legkorábban <see cref="BookingRules.KeyIssueLeadTime"/>-mal a kezdés előtt), akkor is,
///   ha a kiadást nem kötötték a foglaláshoz (pl. régebbi kiadások), vagy
/// - a foglaló oktatónál a foglalás kezdetekor már ott volt a terem kulcsa (pl. egymás utáni foglalásoknál
///   az előző foglaláshoz felvett kulcsot megtartotta), és azt a foglalás kezdete előtt nem adta vissza.
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
        var earliestWindowStart = remaining.Min(booking => booking.StartTime).Subtract(BookingRules.KeyIssueLeadTime);
        // A megtartott kulcs a foglalás előtti napon is kiadható lehetett (pl. reggeli foglaláshoz
        // előző este felvett kulcs), ezért egy nappal korábbról is figyelembe vesszük a kiadásokat.
        var lookbackStart = earliestWindowStart.AddDays(-1);
        var latestEnd = remaining.Max(booking => booking.EndTime);
        var issues = await db.Kulcsmozgasok
            .AsNoTracking()
            .Where(movement =>
                ownerIds.Contains(movement.AtvevoFelhasznaloId) &&
                (movement.MozgasTipus == KeyMovementTypes.Issue || movement.MozgasTipus == KeyMovementTypes.LegacyIssue) &&
                movement.Idopont >= lookbackStart &&
                movement.Idopont < latestEnd &&
                movement.Idopont <= nowUtc)
            .Select(movement => new
            {
                movement.Id,
                movement.KulcsId,
                movement.FoglalasId,
                movement.AtvevoFelhasznaloId,
                RoomId = movement.TeremId ?? movement.Kulcs.TeremId,
                movement.Idopont
            })
            .ToListAsync(cancellationToken);
        if (issues.Count == 0)
        {
            return result;
        }

        // Az érintett kulcsok minden mozgása a kiadások óta: ebből látszik, meddig volt a kulcs az oktatónál.
        var keyIds = issues.Select(issue => issue.KulcsId).Distinct().ToList();
        var keyMovements = (await db.Kulcsmozgasok
                .AsNoTracking()
                .Where(movement =>
                    keyIds.Contains(movement.KulcsId) &&
                    movement.Idopont >= lookbackStart &&
                    movement.Idopont <= nowUtc)
                .Select(movement => new { movement.Id, movement.KulcsId, movement.Idopont })
                .ToListAsync(cancellationToken))
            .GroupBy(movement => movement.KulcsId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(movement => movement.Idopont).ThenBy(movement => movement.Id).ToList());

        // Meddig volt a kiadott kulcs az átvevőnél: a kulcs következő mozgásáig (null: még most is nála van).
        DateTime? HeldUntil(int keyId, int movementId, DateTime issuedAt) =>
            keyMovements[keyId]
                .Where(movement => movement.Idopont > issuedAt || (movement.Idopont == issuedAt && movement.Id > movementId))
                .Select(movement => (DateTime?)movement.Idopont)
                .FirstOrDefault();

        foreach (var booking in remaining)
        {
            var windowStart = booking.StartTime.Subtract(BookingRules.KeyIssueLeadTime);
            var pickedUp = issues.Any(issue =>
                issue.AtvevoFelhasznaloId == booking.FelhasznaloId &&
                issue.RoomId == booking.TeremId &&
                issue.Idopont < booking.EndTime &&
                (
                    // Foglaláshoz nem kötött kiadás a foglalás idősávjában (a korábbi szabály).
                    (issue.FoglalasId == null && issue.Idopont >= windowStart) ||
                    // A kulcs (bármely foglaláshoz vették fel) a foglalás kezdetekor még az oktatónál volt.
                    (issue.Idopont <= booking.StartTime &&
                     (HeldUntil(issue.KulcsId, issue.Id, issue.Idopont) is not { } returnedAt || returnedAt >= booking.StartTime))
                ));
            if (pickedUp)
            {
                result.Add(booking.Id);
            }
        }

        return result;
    }
}
