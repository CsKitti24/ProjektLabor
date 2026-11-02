using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Endpoints;

public static class ReliabilityEndpoints
{
    private const string InstructorRole = "Oktató / Igénylő";
    private const string AdministratorRole = "Adminisztrátor";
    private const string OperationsDirectorRole = "Üzemeltetési Igazgató";
    private const int MaximumScore = 100;

    public static IEndpointRouteBuilder MapReliabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reliability = endpoints.MapGroup("/api/reliability")
            .RequireAuthorization();

        reliability.MapGet("/users/{userId:int}", GetScoreAsync)
            .WithName("GetReliabilityScore");
        reliability.MapGet("/users/{userId:int}/events", GetEventsAsync)
            .WithName("GetReliabilityEvents");
        reliability.MapGet("/my", GetMyScoreAsync)
            .WithName("GetMyReliabilityScore");
        reliability.MapPost("/correction", CorrectScoreAsync)
            .WithName("CreateReliabilityCorrection");

        return endpoints;
    }

    private static async Task<IResult> GetScoreAsync(
        int userId,
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var access = await GetReadAccessAsync(userId, principal, db, cancellationToken);
        if (access is null)
        {
            return Results.Forbid();
        }

        var instructor = await FindInstructorAsync(userId, db, cancellationToken);
        if (instructor is null)
        {
            return Results.NotFound(new { message = "A megadott oktató nem található." });
        }

        var academicYear = GetAcademicYear(DateTime.UtcNow);
        var score = await CalculateScoreAsync(userId, academicYear, db, cancellationToken);

        return Results.Ok(CreateScoreResponse(instructor.Id, instructor.Nev, academicYear, score));
    }

    private static async Task<IResult> GetEventsAsync(
        int userId,
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var access = await GetReadAccessAsync(userId, principal, db, cancellationToken);
        if (access is null)
        {
            return Results.Forbid();
        }

        if (await FindInstructorAsync(userId, db, cancellationToken) is null)
        {
            return Results.NotFound(new { message = "A megadott oktató nem található." });
        }

        var events = await db.MegbizhatosagiEsemenyek
            .AsNoTracking()
            .Where(item => item.FelhasznaloId == userId)
            .OrderByDescending(item => item.EsemenyIdopontja)
            .ThenByDescending(item => item.Id)
            .Select(item => new ReliabilityEventResponse(
                item.Id,
                item.FelhasznaloId,
                item.KulcsId,
                item.FoglalasId,
                item.KiadasiKulcsmozgasId,
                item.EsemenyTipus,
                item.Pont,
                item.EredetiHatarido,
                item.TenylegesLeadasIdeje,
                item.EsemenyIdopontja,
                item.Tanev,
                item.LetrehozoFelhasznaloId,
                item.Indoklas,
                item.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(events);
    }

    private static async Task<IResult> GetMyScoreAsync(
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var instructorId = await GetAuthenticatedUserIdAsync(principal, db, InstructorRole, cancellationToken);
        if (instructorId is null)
        {
            return Results.Forbid();
        }

        var instructor = await FindInstructorAsync(instructorId.Value, db, cancellationToken);
        if (instructor is null)
        {
            return Results.NotFound(new { message = "A bejelentkezett oktató nem található." });
        }

        var academicYear = GetAcademicYear(DateTime.UtcNow);
        var score = await CalculateScoreAsync(instructorId.Value, academicYear, db, cancellationToken);

        return Results.Ok(CreateScoreResponse(instructor.Id, instructor.Nev, academicYear, score));
    }

    private static async Task<IResult> CorrectScoreAsync(
        ReliabilityCorrectionRequest request,
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.UserId <= 0 ||
            request.Pont == 0 ||
            string.IsNullOrWhiteSpace(request.Indoklas) ||
            request.Indoklas.Length > 500)
        {
            return Results.BadRequest(new
            {
                message = "Érvényes oktatóazonosító, nullától eltérő pontkorrekció és legfeljebb 500 karakteres indoklás szükséges."
            });
        }

        var administratorId = await GetAuthenticatedUserIdAsync(
            principal, db, AdministratorRole, cancellationToken);
        if (administratorId is null)
        {
            return Results.Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var instructor = await FindInstructorAsync(request.UserId, db, cancellationToken);
        if (instructor is null)
        {
            return Results.NotFound(new { message = "A megadott oktató nem található." });
        }

        var academicYear = GetAcademicYear(DateTime.UtcNow);
        var previousScore = await CalculateScoreAsync(
            request.UserId, academicYear, db, cancellationToken);
        var newScore = Math.Clamp(previousScore + request.Pont, 0, MaximumScore);
        var now = DateTime.UtcNow;
        var reason = request.Indoklas.Trim();

        db.MegbizhatosagiEsemenyek.Add(new MegbizhatosagiEsemeny
        {
            FelhasznaloId = request.UserId,
            EsemenyTipus = "Manuális korrekció",
            LetrehozoFelhasznaloId = administratorId.Value,
            Indoklas = reason,
            Pont = request.Pont,
            EsemenyIdopontja = now,
            Tanev = academicYear,
            CreatedAt = now
        });
        db.AuditLogok.Add(new AuditLog
        {
            FelhasznaloId = administratorId.Value,
            MuveletTipus = "Pontkorrekció",
            EntitasTipus = "Megbízhatósági pontszám",
            EntitasId = request.UserId.ToString(CultureInfo.InvariantCulture),
            RegiErtek = previousScore.ToString(CultureInfo.InvariantCulture),
            UjErtek = newScore.ToString(CultureInfo.InvariantCulture),
            Indoklas = reason,
            LetrehozasIdeje = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(CreateScoreResponse(instructor.Id, instructor.Nev, academicYear, newScore));
    }

    private static async Task<int?> GetReadAccessAsync(
        int requestedUserId,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var callerId = await GetAuthenticatedUserIdAsync(principal, db, null, cancellationToken);
        if (callerId is null)
        {
            return null;
        }

        var role = await db.Felhasznalok
            .Where(account => account.Id == callerId.Value)
            .Select(account => account.Role)
            .SingleAsync(cancellationToken);

        return role == InstructorRole && callerId.Value != requestedUserId ||
               role != InstructorRole &&
               role != AdministratorRole &&
               role != OperationsDirectorRole
            ? null
            : callerId;
    }

    private static Task<int?> GetAuthenticatedUserIdAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        string? requiredRole,
        CancellationToken cancellationToken)
    {
        var subject = principal.FindFirstValue("sub");
        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            return Task.FromResult<int?>(null);
        }

        return db.Felhasznalok
            .Where(account => account.Id == userId &&
                              (requiredRole == null || account.Role == requiredRole))
            .Select(account => (int?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static Task<InstructorResponse?> FindInstructorAsync(
        int userId,
        AppDbContext db,
        CancellationToken cancellationToken) =>
        db.Felhasznalok
            .AsNoTracking()
            .Where(account => account.Id == userId && account.Role == InstructorRole)
            .Select(account => new InstructorResponse(account.Id, account.Nev))
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<int> CalculateScoreAsync(
        int userId,
        string academicYear,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var points = await db.MegbizhatosagiEsemenyek
            .AsNoTracking()
            .Where(item => item.FelhasznaloId == userId && item.Tanev == academicYear)
            .OrderBy(item => item.EsemenyIdopontja)
            .ThenBy(item => item.Id)
            .Select(item => item.Pont)
            .ToListAsync(cancellationToken);

        return points.Aggregate(
            0,
            (score, pointsForEvent) => Math.Clamp(score + pointsForEvent, 0, MaximumScore));
    }

    private static string GetAcademicYear(DateTime date)
    {
        var startYear = date.Month >= 9 ? date.Year : date.Year - 1;
        return $"{startYear}/{startYear + 1}";
    }

    private static ReliabilityScoreResponse CreateScoreResponse(
        int userId,
        string name,
        string academicYear,
        int score) =>
        new(userId, name, academicYear, score, GetConsequence(score));

    private static string GetConsequence(int score) => score switch
    {
        >= 30 => "Elbeszélgetés és kulcskezelési/biztonsági tréning",
        >= 20 => "Kötelező személyes elbeszélgetés",
        >= 10 => "Figyelmeztetés",
        _ => "Nincs külön intézkedés"
    };

    private sealed record InstructorResponse(int Id, string Nev);

    public sealed record ReliabilityScoreResponse(
        int UserId,
        string Nev,
        string Tanev,
        int Pontszam,
        string Kovetkezmeny);

    public sealed record ReliabilityEventResponse(
        int Id,
        int FelhasznaloId,
        int? KulcsId,
        int? FoglalasId,
        int? KiadasiKulcsmozgasId,
        string EsemenyTipus,
        int Pont,
        DateTime? EredetiHatarido,
        DateTime? TenylegesLeadasIdeje,
        DateTime EsemenyIdopontja,
        string Tanev,
        int? LetrehozoFelhasznaloId,
        string? Indoklas,
        DateTime CreatedAt);

    public sealed record ReliabilityCorrectionRequest(int UserId, int Pont, string Indoklas);
}
