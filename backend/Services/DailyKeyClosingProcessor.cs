using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Security;

namespace ProjektLabor.Services;

public sealed class DailyKeyClosingProcessor
{
    private const string IssueMovement = "Kiadás";
    private const string LegacyIssueMovement = "Kiadas";
    private const string ReturnMovement = "Visszavétel";
    private const string PenaltyEventType = "LeNemAdott";
    private const string EveningNotificationType = "EveningKeyReturn";
    private const int PenaltyPoints = 3;

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailyKeyClosingProcessor> _logger;

    public DailyKeyClosingProcessor(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<DailyKeyClosingProcessor> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    // A kiadás határideje legkésőbb a kiadást követő napi zárás; ennél régebbi, már visszahozott
    // kiadásokkal nincs teendő. A tartalék a leállás utáni pótlást (catch-up) is lefedi.
    private static readonly TimeSpan ProcessingWindow = TimeSpan.FromDays(7);

    public async Task<int> ProcessAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var closingTime = await AutomationParameterReader.GetClosingTimeAsync(
            _db, _logger, cancellationToken);
        var timeZone = AutomationParameterReader.GetTimeZone(_configuration, _logger);
        var windowStart = nowUtc.Subtract(ProcessingWindow);

        // Csak az ablakon belüli és a még vissza nem hozott kiadásokat töltjük be.
        var issuedMovements = await _db.Kulcsmozgasok
            .AsNoTracking()
            .Where(movement =>
                (movement.MozgasTipus == IssueMovement || movement.MozgasTipus == LegacyIssueMovement) &&
                (movement.Idopont >= windowStart ||
                 !_db.Kulcsmozgasok.Any(candidate =>
                     candidate.KulcsId == movement.KulcsId &&
                     candidate.MozgasTipus == ReturnMovement &&
                     candidate.Idopont > movement.Idopont)))
            .Select(movement => new
            {
                movement.Id,
                movement.KulcsId,
                movement.FoglalasId,
                movement.Idopont,
                RecipientId = movement.AtvevoFelhasznaloId ??
                              (movement.Foglalas != null ? (int?)movement.Foglalas.FelhasznaloId : null)
            })
            .ToListAsync(cancellationToken);
        if (issuedMovements.Count == 0)
        {
            return 0;
        }

        // Kötegelt előtöltés: visszavételek, meglévő pontok, értesítések és auditbejegyzések.
        var movementIds = issuedMovements.Select(movement => movement.Id).ToList();
        var nullableMovementIds = movementIds.Select(id => (int?)id).ToList();
        var keyIds = issuedMovements.Select(movement => movement.KulcsId).Distinct().ToList();
        var earliestIssue = issuedMovements.Min(movement => movement.Idopont);
        var returnsByKey = (await _db.Kulcsmozgasok
                .AsNoTracking()
                .Where(candidate =>
                    keyIds.Contains(candidate.KulcsId) &&
                    candidate.MozgasTipus == ReturnMovement &&
                    candidate.Idopont > earliestIssue)
                .Select(candidate => new { candidate.KulcsId, candidate.Idopont })
                .ToListAsync(cancellationToken))
            .ToLookup(candidate => candidate.KulcsId, candidate => candidate.Idopont);
        var scoredMovementIds = (await _db.MegbizhatosagiEsemenyek
                .AsNoTracking()
                .Where(item => nullableMovementIds.Contains(item.KiadasiKulcsmozgasId))
                .Select(item => item.KiadasiKulcsmozgasId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var existingNotificationKeys = (await _db.Ertesitesek
                .AsNoTracking()
                .Where(notification =>
                    nullableMovementIds.Contains(notification.KulcsmozgasId) &&
                    notification.AutomationKey != null)
                .Select(notification => notification.AutomationKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var candidateAuditKeys = movementIds
            .SelectMany(id => new[]
            {
                $"{EveningNotificationType}:{id}",
                $"DailyKeyClosing:UnresolvedRecipient:{id}"
            })
            .ToList();
        var existingAuditKeys = (await _db.AuditLogok
                .AsNoTracking()
                .Where(entry => entry.AutomationKey != null && candidateAuditKeys.Contains(entry.AutomationKey))
                .Select(entry => entry.AutomationKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var porterIds = await _db.Felhasznalok
            .Where(user => user.Role == RoleNames.Portas && user.Aktiv)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        var createdCount = 0;
        var createdNotificationCount = 0;
        var unresolvedRecipientCount = 0;
        var now = AsUtc(nowUtc);
        foreach (var movement in issuedMovements)
        {
            var issuedAtUtc = AsUtc(movement.Idopont);
            var deadlineUtc = KeyReturnDeadlineCalculator.GetDeadlineUtc(
                issuedAtUtc,
                closingTime,
                timeZone);
            if (nowUtc < deadlineUtc)
            {
                continue;
            }

            var returnTimes = returnsByKey[movement.KulcsId];
            var returnedByNow = returnTimes.Any(returnedAt => returnedAt > movement.Idopont && returnedAt <= nowUtc);
            if (!returnedByNow)
            {
                foreach (var porterId in porterIds)
                {
                    var automationKey = $"{EveningNotificationType}:{movement.Id}:{porterId}";
                    if (!existingNotificationKeys.Add(automationKey))
                    {
                        continue;
                    }

                    _db.Ertesitesek.Add(new Ertesites
                    {
                        FoglalasId = movement.FoglalasId,
                        CimzettFelhasznaloId = porterId,
                        KulcsmozgasId = movement.Id,
                        ErtesitesTipus = EveningNotificationType,
                        IdoPont = now,
                        Hatarido = deadlineUtc,
                        Allapot = "Kesz",
                        AutomationKey = automationKey,
                        CreatedAt = now
                    });
                    createdNotificationCount++;
                }

                var auditAutomationKey = $"{EveningNotificationType}:{movement.Id}";
                if (porterIds.Count > 0 && existingAuditKeys.Add(auditAutomationKey))
                {
                    _db.AuditLogok.Add(new AuditLog
                    {
                        MuveletTipus = "AUTOMATIC_NOTIFICATION",
                        EntitasTipus = "Kulcsmozgas",
                        EntitasId = movement.Id.ToString(CultureInfo.InvariantCulture),
                        AutomationKey = auditAutomationKey,
                        UjErtek = EveningNotificationType,
                        Indoklas = "Esti kulcsvisszaadási értesítés létrehozása a portaszolgálatnak",
                        LetrehozasIdeje = now
                    });
                }
            }

            var returnedByDeadline = returnTimes.Any(returnedAt => returnedAt > movement.Idopont && returnedAt <= deadlineUtc);
            if (returnedByDeadline)
            {
                continue;
            }

            if (!movement.RecipientId.HasValue)
            {
                unresolvedRecipientCount++;
                var automationKey = $"DailyKeyClosing:UnresolvedRecipient:{movement.Id}";
                if (existingAuditKeys.Add(automationKey))
                {
                    _db.AuditLogok.Add(new AuditLog
                    {
                        MuveletTipus = "AUTOMATION_DATA_ISSUE",
                        EntitasTipus = "Kulcsmozgas",
                        EntitasId = movement.Id.ToString(CultureInfo.InvariantCulture),
                        AutomationKey = automationKey,
                        UjErtek = $"KulcsId: {movement.KulcsId}; FoglalasId: nincs; AtvevoFelhasznaloId: nincs",
                        Indoklas = "Napi zárás: a kiadáshoz nem tartozik foglalás vagy rögzített átvevő; pontlevonás nem történt.",
                        LetrehozasIdeje = now
                    });
                }

                continue;
            }

            if (!scoredMovementIds.Add(movement.Id))
            {
                continue;
            }

            _db.MegbizhatosagiEsemenyek.Add(new MegbizhatosagiEsemeny
            {
                FelhasznaloId = movement.RecipientId.Value,
                KulcsId = movement.KulcsId,
                FoglalasId = movement.FoglalasId,
                KiadasiKulcsmozgasId = movement.Id,
                EsemenyTipus = PenaltyEventType,
                Indoklas = "A kulcs a napi zárási időpontig nem került vissza.",
                EredetiHatarido = deadlineUtc,
                Pont = PenaltyPoints,
                Automatikus = true,
                EsemenyIdopontja = now,
                Tanev = AppTime.AcademicYear(now),
                CreatedAt = now
            });
            _db.AuditLogok.Add(new AuditLog
            {
                MuveletTipus = "AUTOMATIC_PENALTY",
                EntitasTipus = "Kulcsmozgas",
                EntitasId = movement.Id.ToString(CultureInfo.InvariantCulture),
                UjErtek = $"{PenaltyEventType}: +{PenaltyPoints}; FelhasznaloId: {movement.RecipientId.Value}",
                Indoklas = "Napi zárási feldolgozás",
                LetrehozasIdeje = now
            });
            createdCount++;
        }

        if (unresolvedRecipientCount > 0)
        {
            _logger.LogWarning(
                "Skipped {MovementCount} overdue key issues without a booking or recorded recipient.",
                unresolvedRecipientCount);
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            if (createdCount > 0)
            {
                _logger.LogInformation(
                    "Created {PenaltyCount} unreturned-key reliability penalties at {ProcessedAtUtc}.",
                    createdCount,
                    nowUtc);
            }
        }

        if (createdNotificationCount > 0)
        {
            _logger.LogInformation(
                "Created {NotificationCount} evening key return notifications at {ProcessedAtUtc}.",
                createdNotificationCount,
                nowUtc);
        }

        return createdCount;
    }

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

}
