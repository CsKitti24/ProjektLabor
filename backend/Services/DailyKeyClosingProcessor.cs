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
                movement.FoglalasId != null &&
                (movement.MozgasTipus == IssueMovement ||
                 movement.MozgasTipus == LegacyIssueMovement))
            .ToListAsync(cancellationToken);

        var createdCount = 0;
        foreach (var movement in issuedMovements)
        {
            if (movement.Foglalas is null)
            {
                continue;
            }

            var issuedAtUtc = AsUtc(movement.Idopont);
            var localIssuedAt = TimeZoneInfo.ConvertTimeFromUtc(issuedAtUtc, timeZone);
            var deadlineDate = DateOnly.FromDateTime(localIssuedAt);
            if (TimeOnly.FromDateTime(localIssuedAt) > closingTime)
            {
                deadlineDate = deadlineDate.AddDays(1);
            }

            var localDeadline = deadlineDate.ToDateTime(closingTime, DateTimeKind.Unspecified);
            var deadlineUtc = TimeZoneInfo.ConvertTimeToUtc(localDeadline, timeZone);
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
                FelhasznaloId = movement.Foglalas.FelhasznaloId,
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
                UjErtek = $"{PenaltyEventType}: +{PenaltyPoints}; FelhasznaloId: {movement.Foglalas.FelhasznaloId}",
                Indoklas = "Napi zárási feldolgozás",
                LetrehozasIdeje = now
            });
            createdCount++;
        }

        if (createdCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Created {PenaltyCount} unreturned-key reliability penalties at {ProcessedAtUtc}.",
                createdCount,
                nowUtc);
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
