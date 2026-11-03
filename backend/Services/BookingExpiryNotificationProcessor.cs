using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services;

public sealed class BookingExpiryNotificationProcessor
{
    private const string NotificationType = "BeforeEnd";
    private readonly AppDbContext _db;
    private readonly ILogger<BookingExpiryNotificationProcessor> _logger;

    public BookingExpiryNotificationProcessor(
        AppDbContext db,
        ILogger<BookingExpiryNotificationProcessor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> ProcessAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var dueBefore = nowUtc.AddMinutes(10);
        var bookings = await _db.Foglalasok
            .Where(booking =>
                booking.Status == "Aktiv" &&
                booking.EndTime > nowUtc &&
                booking.EndTime <= dueBefore &&
                !_db.Ertesitesek.Any(notification =>
                    notification.FoglalasId == booking.Id &&
                    notification.ErtesitesTipus == NotificationType))
            .ToListAsync(cancellationToken);

        if (bookings.Count == 0)
        {
            return 0;
        }

        _db.Ertesitesek.AddRange(bookings.Select(booking => new Ertesites
        {
            FoglalasId = booking.Id,
            ErtesitesTipus = NotificationType,
            IdoPont = nowUtc,
            Allapot = "Kesz",
            AutomationKey = $"{NotificationType}:{booking.Id}",
            CreatedAt = nowUtc
        }));

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Created {NotificationCount} booking expiry notifications at {ProcessedAtUtc}.",
            bookings.Count,
            nowUtc);
        return bookings.Count;
    }
}
