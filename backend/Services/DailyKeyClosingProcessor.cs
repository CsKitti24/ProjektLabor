using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services;

public sealed class DailyKeyClosingProcessor
{
    private const string IssueMovement = "Kiadás";
    private const string LegacyIssueMovement = "Kiadas";
    private const string ReturnMovement = "Visszavétel";
    private const string PenaltyEventType = "LeNemAdott";
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

    public async Task<int> ProcessAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var closingTime = await AutomationParameterReader.GetClosingTimeAsync(
            _db, _logger, cancellationToken);
        var timeZone = AutomationParameterReader.GetTimeZone(_configuration, _logger);

        var issuedMovements = await _db.Kulcsmozgasok
            .Include(movement => movement.Foglalas)
            .Where(movement =>
                (movement.MozgasTipus == IssueMovement ||
                 movement.MozgasTipus == LegacyIssueMovement))
            .ToListAsync(cancellationToken);

        var createdCount = 0;
        var unresolvedRecipientCount = 0;
        foreach (var movement in issuedMovements)
        {
            var issuedAtUtc = AsUtc(movement.Idopont);
            var deadlineUtc = KeyReturnDeadlineCalculator.GetDeadlineUtc(
                issuedAtUtc,
                null,
                closingTime,
                timeZone);
            if (nowUtc < deadlineUtc)
            {
                continue;
            }

            var alreadyReturnedByDeadline = await _db.Kulcsmozgasok.AnyAsync(
                candidate =>
                    candidate.KulcsId == movement.KulcsId &&
                    candidate.MozgasTipus == ReturnMovement &&
                    candidate.Idopont > movement.Idopont &&
                    candidate.Idopont <= deadlineUtc,
                cancellationToken);
            if (alreadyReturnedByDeadline)
            {
                continue;
            }

            var recipientId = movement.AtvevoFelhasznaloId ?? movement.Foglalas?.FelhasznaloId;
            if (!recipientId.HasValue)
            {
                unresolvedRecipientCount++;
                var automationKey = $"DailyKeyClosing:UnresolvedRecipient:{movement.Id}";
                var alreadyLogged = await _db.AuditLogok
                    .AnyAsync(entry => entry.AutomationKey == automationKey, cancellationToken);
                if (!alreadyLogged)
                {
                    var loggedAt = AsUtc(nowUtc);
                    _db.AuditLogok.Add(new AuditLog
                    {
                        MuveletTipus = "Automatizálási adatprobléma",
                        EntitasTipus = "Kulcsmozgas",
                        EntitasId = movement.Id.ToString(CultureInfo.InvariantCulture),
                        AutomationKey = automationKey,
                        UjErtek = $"KulcsId: {movement.KulcsId}; FoglalasId: nincs; AtvevoFelhasznaloId: nincs",
                        Indoklas = "Napi zárás: a kiadáshoz nem tartozik foglalás vagy rögzített átvevő; pontlevonás nem történt.",
                        LetrehozasIdeje = loggedAt
                    });
                }

                continue;
            }

            var alreadyScored = await _db.MegbizhatosagiEsemenyek.AnyAsync(
                item => item.KiadasiKulcsmozgasId == movement.Id,
                cancellationToken);
            if (alreadyScored)
            {
                continue;
            }

            var now = AsUtc(nowUtc);
            _db.MegbizhatosagiEsemenyek.Add(new MegbizhatosagiEsemeny
            {
                FelhasznaloId = recipientId.Value,
                KulcsId = movement.KulcsId,
                FoglalasId = movement.FoglalasId,
                KiadasiKulcsmozgasId = movement.Id,
                EsemenyTipus = PenaltyEventType,
                Indoklas = "A kulcs a napi zárási időpontig nem került vissza.",
                EredetiHatarido = deadlineUtc,
                Pont = PenaltyPoints,
                Automatikus = true,
                EsemenyIdopontja = now,
                Tanev = GetAcademicYear(now),
                CreatedAt = now
            });
            _db.AuditLogok.Add(new AuditLog
            {
                MuveletTipus = "Automatikus büntetőpont",
                EntitasTipus = "Kulcsmozgas",
                EntitasId = movement.Id.ToString(CultureInfo.InvariantCulture),
                UjErtek = $"{PenaltyEventType}: +{PenaltyPoints}; FelhasznaloId: {recipientId.Value}",
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

        if (createdCount > 0 || _db.ChangeTracker.HasChanges())
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

        return createdCount;
    }

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string GetAcademicYear(DateTime date)
    {
        var startYear = date.Month >= 9 ? date.Year : date.Year - 1;
        return $"{startYear}/{startYear + 1}";
    }
}
