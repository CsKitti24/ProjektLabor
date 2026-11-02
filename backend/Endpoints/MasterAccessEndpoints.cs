using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using ProjektLabor.Security;

namespace ProjektLabor.Endpoints;

public static class MasterAccessEndpoints
{
    private const string MasterKeyType = "Mesterkulcs";

    public static IEndpointRouteBuilder MapMasterAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var masterAccess = endpoints.MapGroup("/api/keys/{id:int}/master-access")
            .RequireAuthorization(RolePolicies.Administrator);

        masterAccess.MapGet("/", GetAsync)
            .WithName("GetMasterKeyAccess");
        masterAccess.MapPost("/", GrantAsync)
            .WithName("GrantMasterKeyAccess");
        masterAccess.MapDelete("/{userId:int}", RevokeAsync)
            .WithName("RevokeMasterKeyAccess");

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        int id,
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await GetAdministratorIdAsync(principal, db, cancellationToken) is null)
        {
            return Results.Forbid();
        }

        if (!await IsMasterKeyAsync(id, db, cancellationToken))
        {
            return Results.NotFound(new { message = "A megadott mesterkulcs nem található." });
        }

        var instructors = await db.MesterkulcsJogosultsagok
            .AsNoTracking()
            .Where(access => access.KulcsId == id)
            .OrderBy(access => access.Felhasznalo.Nev)
            .ThenBy(access => access.FelhasznaloId)
            .Select(access => new MasterAccessResponse(
                access.FelhasznaloId,
                access.Felhasznalo.Nev,
                access.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(instructors);
    }

    private static async Task<IResult> GrantAsync(
        int id,
        MasterAccessRequest request,
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await GetAdministratorIdAsync(principal, db, cancellationToken) is null)
        {
            return Results.Forbid();
        }

        if (request.UserId <= 0)
        {
            return Results.BadRequest(new { message = "Érvényes oktatóazonosító szükséges." });
        }

        if (!await IsMasterKeyAsync(id, db, cancellationToken))
        {
            return Results.NotFound(new { message = "A megadott mesterkulcs nem található." });
        }

        var instructor = await db.Felhasznalok
            .AsNoTracking()
            .Where(account => account.Id == request.UserId && account.Role == RoleNames.Oktato)
            .Select(account => new InstructorResponse(account.Id, account.Nev))
            .SingleOrDefaultAsync(cancellationToken);

        if (instructor is null)
        {
            return Results.NotFound(new { message = "A megadott oktató nem található." });
        }

        if (await db.MesterkulcsJogosultsagok.AnyAsync(
                access => access.KulcsId == id && access.FelhasznaloId == request.UserId,
                cancellationToken))
        {
            return Results.Conflict(new { message = "Az oktató már jogosult erre a mesterkulcsra." });
        }

        var access = new MesterkulcsJogosultsag
        {
            KulcsId = id,
            FelhasznaloId = instructor.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.MesterkulcsJogosultsagok.Add(access);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/keys/{id}/master-access/{instructor.Id}",
            new MasterAccessResponse(instructor.Id, instructor.Nev, access.CreatedAt));
    }

    private static async Task<IResult> RevokeAsync(
        int id,
        int userId,
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (await GetAdministratorIdAsync(principal, db, cancellationToken) is null)
        {
            return Results.Forbid();
        }

        if (!await IsMasterKeyAsync(id, db, cancellationToken))
        {
            return Results.NotFound(new { message = "A megadott mesterkulcs nem található." });
        }

        var access = await db.MesterkulcsJogosultsagok.SingleOrDefaultAsync(
            item => item.KulcsId == id && item.FelhasznaloId == userId,
            cancellationToken);

        if (access is null)
        {
            return Results.NotFound(new { message = "A jogosultság nem található." });
        }

        db.MesterkulcsJogosultsagok.Remove(access);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static async Task<int?> GetAdministratorIdAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!principal.IsInRole(RoleNames.Adminisztrator) ||
            !int.TryParse(
                principal.FindFirstValue("sub"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId))
        {
            return null;
        }

        return await db.Felhasznalok
            .Where(account => account.Id == userId && account.Role == RoleNames.Adminisztrator)
            .Select(account => (int?)account.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static Task<bool> IsMasterKeyAsync(
        int keyId,
        AppDbContext db,
        CancellationToken cancellationToken) =>
        db.Kulcsok.AnyAsync(
            key => key.Id == keyId && key.Tipus == MasterKeyType,
            cancellationToken);

    private sealed record InstructorResponse(int Id, string Nev);

    private sealed record MasterAccessResponse(int UserId, string Nev, DateTime CreatedAt);

    public sealed record MasterAccessRequest(int UserId);
}
