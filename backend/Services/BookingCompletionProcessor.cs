using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services;

/// <summary>
/// A lezajlott, aktív foglalásokat "Kesz" állapotba teszi, ha a foglaláshoz felvették a kulcsot.
/// (A kulcsfelvétel nélküli foglalásokat a <see cref="NoShowProcessor"/> kezeli.)
/// </summary>
public sealed class BookingCompletionProcessor
{
    private readonly AppDbContext _db;
    private readonly ILogger<BookingCompletionProcessor> _logger;

    public BookingCompletionProcessor(AppDbContext db, ILogger<BookingCompletionProcessor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<int> ProcessAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        // Ugyanúgy tranzakcióban és zár alatt, mint a NoShow-feldolgozó: a két automatizmus és a kulcskiadás
        // nem írhatja felül egymás állapotváltását.
        SqlServerLocking.ExecuteWithRetryAsync(_db, async () =>
        {
            await using var transaction = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                ? null
                : await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            // Közös zár a NoShow-feldolgozóval, hogy ugyanazt a foglalást ne dolgozzák fel egyszerre.
            await SqlServerLocking.AcquireAsync(_db, "automation:no-show", cancellationToken);

            var completedCount = await ProcessInTransactionAsync(nowUtc, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return completedCount;
        });

    private async Task<int> ProcessInTransactionAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var finishedBookings = await _db.Foglalasok
            .Where(booking => booking.Status == BookingRules.ActiveStatus && booking.EndTime <= nowUtc)
            .ToListAsync(cancellationToken);
        if (finishedBookings.Count == 0)
        {
            return 0;
        }

        var collectedBookingIds = await BookingKeyPickup.GetBookingIdsWithKeyPickupAsync(
            _db, finishedBookings, nowUtc, cancellationToken);
        var completed = finishedBookings.Where(booking => collectedBookingIds.Contains(booking.Id)).ToList();
        if (completed.Count == 0)
        {
            return 0;
        }

        var automationKeys = completed.Select(booking => $"BookingCompleted:{booking.Id}").ToList();
        var alreadyAudited = (await _db.AuditLogok
                .Where(entry => entry.AutomationKey != null && automationKeys.Contains(entry.AutomationKey))
                .Select(entry => entry.AutomationKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var booking in completed)
        {
            booking.Status = BookingRules.DoneStatus;
            booking.UpdatedAt = nowUtc;
            var automationKey = $"BookingCompleted:{booking.Id}";
            if (!alreadyAudited.Contains(automationKey))
            {
                _db.AuditLogok.Add(new AuditLog
                {
                    MuveletTipus = "UPDATE",
                    EntitasTipus = "Foglalas",
                    EntitasId = booking.Id.ToString(),
                    AutomationKey = automationKey,
                    RegiErtek = BookingRules.ActiveStatus,
                    UjErtek = BookingRules.DoneStatus,
                    Indoklas = "Automatikus lezárás: a foglalás véget ért, a kulcsot felvették.",
                    LetrehozasIdeje = nowUtc
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Marked {BookingCount} finished bookings as completed at {ProcessedAtUtc}.",
            completed.Count,
            nowUtc);
        return completed.Count;
    }
}
