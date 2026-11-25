using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services;

/// <summary>
/// Átfedő foglalás (vagy karbantartás) miatt a foglalás nem menthető. Magyar üzenetet hordoz, amely
/// közvetlenül a felhasználónak szól (409 Conflict).
/// </summary>
public sealed class BookingConflictException(string message) : InvalidOperationException(message);

public static class BookingRules
{
    public const string OverlapMessage = "A megadott idősáv ütközik egy már meglévő foglalással.";
    public const string MaintenanceOverlapMessage = "A terem a megadott időszakban karbantartás alatt áll.";

    // Legfeljebb ennyi nappal előre lehet foglalni (kb. egy félév).
    public const int MaxAdvanceDays = 180;

    // Egy oktatónak egyszerre legfeljebb ennyi jövőbeli, aktív foglalása lehet.
    public const int MaxActiveFutureBookingsPerInstructor = 40;
    public const string ActiveStatus = "Aktiv";
    public const string DoneStatus = "Kesz";
    public const string CancelledStatus = "Megszuntetve";
    public const string NoShowStatus = "NoShow";

    public static Expression<Func<Foglalas, bool>> ActiveBookings =>
        booking => booking.Status != CancelledStatus && booking.Status != NoShowStatus;

    public const int SlotMinutes = 45;

    // A kulcs legkorábban ennyivel a foglalás kezdete előtt vehető fel.
    public static readonly TimeSpan KeyIssueLeadTime = TimeSpan.FromHours(2);
    public static readonly TimeSpan OpeningTime = new(8, 0, 0);
    public static readonly TimeSpan ClosingTime = new(20, 0, 0);

    /// <summary>
    /// A nyitvatartási és idősáv-szabályokat az intézmény helyi idejében ellenőrzi.
    /// A megjelölés nélküli időpontokat helyi időnek, a többit a megadott időzónájuk szerint értelmezi.
    /// </summary>
    public static string? ValidateTimeRange(DateTime start, DateTime end)
    {
        var localStart = AppTime.ToLocal(start);
        var localEnd = AppTime.ToLocal(end);

        if (localStart >= localEnd)
        {
            return "A kezdési időpontnak meg kell előznie a befejezési időpontot.";
        }

        if (localStart.TimeOfDay < OpeningTime || localEnd.TimeOfDay > ClosingTime ||
            localStart.Date != localEnd.Date)
        {
            return "A foglalásnak egy napon belül 08:00 és 20:00 közé kell esnie.";
        }

        var startOffset = (localStart - localStart.Date.Add(OpeningTime)).TotalMinutes;
        var duration = (localEnd - localStart).TotalMinutes;
        if (startOffset % SlotMinutes != 0 || duration % SlotMinutes != 0)
        {
            return "A foglalás kezdete és hossza csak 45 perces időszelethez igazodhat.";
        }

        return null;
    }

    /// <summary>
    /// Foglalási korlátok: hétvégére nem lehet foglalni, és legfeljebb <see cref="MaxAdvanceDays"/> nappal előre.
    /// </summary>
    public static string? ValidateBookingWindow(DateTime start, DateTime nowUtc)
    {
        var localStart = AppTime.ToLocal(start);
        if (localStart.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return "Hétvégére nem lehet foglalni.";
        }

        if (AppTime.LocalDate(start) > AppTime.LocalDate(nowUtc).AddDays(MaxAdvanceDays))
        {
            return $"Legfeljebb {MaxAdvanceDays} nappal előre lehet foglalni.";
        }

        return null;
    }

    /// <summary>
    /// Az oktató jövőbeli, aktív foglalásainak száma elérte-e a korlátot.
    /// </summary>
    public static async Task<string?> ValidateInstructorQuotaAsync(
        AppDbContext db,
        int userId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var activeFutureCount = await db.Foglalasok.CountAsync(booking =>
            booking.FelhasznaloId == userId &&
            booking.Status == ActiveStatus &&
            booking.EndTime > nowUtc, cancellationToken);
        return activeFutureCount >= MaxActiveFutureBookingsPerInstructor
            ? $"Egy oktatónak egyszerre legfeljebb {MaxActiveFutureBookingsPerInstructor} jövőbeli foglalása lehet."
            : null;
    }

    public static bool IsActive(string status) =>
        !status.Equals(CancelledStatus, StringComparison.OrdinalIgnoreCase) &&
        !status.Equals(NoShowStatus, StringComparison.OrdinalIgnoreCase);

    public static async Task<string?> ValidateAsync(
        AppDbContext db,
        int roomId,
        DateTime start,
        DateTime end,
        int? bookingId = null,
        CancellationToken cancellationToken = default)
    {
        var timeError = ValidateTimeRange(start, end);
        if (timeError is not null)
        {
            return timeError;
        }

        start = AppTime.ToUtc(start);
        end = AppTime.ToUtc(end);

        if (await db.Foglalasok
                .Where(ActiveBookings)
                .AnyAsync(f =>
                    f.Id != bookingId &&
                    f.TeremId == roomId &&
                    start < f.EndTime &&
                    end > f.StartTime, cancellationToken))
        {
            return OverlapMessage;
        }

        if (await db.KarbantartasiIdosavok.AnyAsync(m =>
                m.TeremId == roomId &&
                start < m.EndTime &&
                end > m.StartTime, cancellationToken))
        {
            return MaintenanceOverlapMessage;
        }

        return null;
    }

    public static bool IsUserStatusChangeAllowed(
        string currentStatus,
        string requestedStatus,
        bool isAdmin)
    {
        if (requestedStatus.Equals("NoShow", StringComparison.OrdinalIgnoreCase) ||
            requestedStatus.Equals("Kesz", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (requestedStatus.Equals("Aktiv", StringComparison.OrdinalIgnoreCase))
        {
            return currentStatus.Equals("Megszuntetve", StringComparison.OrdinalIgnoreCase) && isAdmin;
        }

        return requestedStatus.Equals("Megszuntetve", StringComparison.OrdinalIgnoreCase) &&
               currentStatus.Equals("Aktiv", StringComparison.OrdinalIgnoreCase);
    }
}
