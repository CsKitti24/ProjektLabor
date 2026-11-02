using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Endpoints;

public static class SystemAdministrationEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const string ClosingTimeKey = "napi_kulcsleadasi_zarasi_idopont";
    private const string NoShowLimitKey = "no_show_idokorlat_perc";

    public static IEndpointRouteBuilder MapSystemAdministrationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var parameters = endpoints.MapGroup("/api/parameters")
            .WithTags("Parameters")
            .RequireAuthorization("AdminOrIgazgato");

        parameters.MapGet("/", GetParametersAsync)
            .WithName("GetParameters");
        parameters.MapGet("/{key}", GetParameterAsync)
            .WithName("GetParameter");
        parameters.MapPut("/{key}", UpdateParameterAsync)
            .RequireAuthorization("Adminisztrator")
            .WithName("UpdateParameter");

        var audit = endpoints.MapGroup("/api/audit")
            .WithTags("Audit")
            .RequireAuthorization("AdminOrIgazgato");

        audit.MapGet("/", GetAuditLogsAsync)
            .WithName("GetAuditLogs");
        audit.MapGet("/{id:int}", GetAuditLogAsync)
            .WithName("GetAuditLog");

        return endpoints;
    }

    private static async Task<IResult> GetParametersAsync(
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var parameters = await db.Rendszerparameterek
            .AsNoTracking()
            .OrderBy(parameter => parameter.Kulcs)
            .Select(parameter => new ParameterResponse(
                parameter.Kulcs,
                parameter.Ertek,
                parameter.Leiras,
                parameter.ModositasIdeje))
            .ToListAsync(cancellationToken);

        return Results.Ok(parameters);
    }

    private static async Task<IResult> GetParameterAsync(
        string key,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var parameter = await FindParameterAsync(key, db, cancellationToken);
        return parameter is null
            ? Results.NotFound(new { message = "A rendszerparaméter nem található." })
            : Results.Ok(parameter);
    }

    private static async Task<IResult> UpdateParameterAsync(
        string key,
        UpdateParameterRequest request,
        ClaimsPrincipal principal,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Ertek) || request.Ertek.Length > 500)
        {
            return Results.BadRequest(new { message = "Az érték megadása kötelező, legfeljebb 500 karakter lehet." });
        }

        var value = request.Ertek.Trim();
        var validationError = ValidateParameterValue(key, value);
        if (validationError is not null)
        {
            return Results.BadRequest(new { message = validationError });
        }

        if (request.Indoklas?.Length > 500)
        {
            return Results.BadRequest(new { message = "Az indoklás legfeljebb 500 karakter lehet." });
        }

        var parameter = await db.Rendszerparameterek
            .SingleOrDefaultAsync(item => item.Kulcs == key, cancellationToken);
        if (parameter is null)
        {
            return Results.NotFound(new { message = "A rendszerparaméter nem található." });
        }

        if (!int.TryParse(
                principal.FindFirstValue("sub"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId) ||
            !await db.Felhasznalok.AnyAsync(
                user => user.Id == userId && user.Role == "Adminisztrator",
                cancellationToken))
        {
            return Results.Forbid();
        }

        var oldValue = parameter.Ertek;
        var now = DateTime.UtcNow;
        parameter.Ertek = value;
        parameter.ModositasIdeje = now;
        db.AuditLogok.Add(new AuditLog
        {
            FelhasznaloId = userId,
            MuveletTipus = "Rendszerparaméter módosítása",
            EntitasTipus = "Rendszerparaméter",
            EntitasId = parameter.Kulcs,
            RegiErtek = oldValue,
            UjErtek = value,
            Indoklas = string.IsNullOrWhiteSpace(request.Indoklas) ? null : request.Indoklas.Trim(),
            LetrehozasIdeje = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new ParameterResponse(
            parameter.Kulcs,
            parameter.Ertek,
            parameter.Leiras,
            parameter.ModositasIdeje));
    }

    private static async Task<IResult> GetAuditLogsAsync(
        [FromServices] AppDbContext db,
        [FromQuery] int? felhasznaloId,
        [FromQuery] string? entitasTipus,
        [FromQuery] string? muveletTipus,
        [FromQuery] DateTime? kezdet,
        [FromQuery] DateTime? veg,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (felhasznaloId is <= 0 ||
            page <= 0 ||
            pageSize <= 0 ||
            pageSize > MaximumPageSize ||
            page > int.MaxValue / pageSize ||
            (kezdet.HasValue && veg.HasValue && kezdet > veg))
        {
            return Results.BadRequest(new { message = "Érvénytelen audit szűrő vagy lapozási paraméter." });
        }

        var query = db.AuditLogok.AsNoTracking().AsQueryable();
        if (felhasznaloId.HasValue)
        {
            query = query.Where(item => item.FelhasznaloId == felhasznaloId.Value);
        }

        if (!string.IsNullOrWhiteSpace(entitasTipus))
        {
            query = query.Where(item => item.EntitasTipus == entitasTipus);
        }

        if (!string.IsNullOrWhiteSpace(muveletTipus))
        {
            query = query.Where(item => item.MuveletTipus == muveletTipus);
        }

        if (kezdet.HasValue)
        {
            query = query.Where(item => item.LetrehozasIdeje >= kezdet.Value);
        }

        if (veg.HasValue)
        {
            query = query.Where(item => item.LetrehozasIdeje <= veg.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.LetrehozasIdeje)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new AuditLogResponse(
                item.Id,
                item.FelhasznaloId,
                item.Felhasznalo == null ? null : item.Felhasznalo.Nev,
                item.MuveletTipus,
                item.EntitasTipus,
                item.EntitasId,
                item.RegiErtek,
                item.UjErtek,
                item.Indoklas,
                item.LetrehozasIdeje))
            .ToListAsync(cancellationToken);

        return Results.Ok(new AuditLogListResponse(items, totalCount));
    }

    private static async Task<IResult> GetAuditLogAsync(
        int id,
        [FromServices] AppDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await db.AuditLogok
            .AsNoTracking()
            .Where(log => log.Id == id)
            .Select(log => new AuditLogResponse(
                log.Id,
                log.FelhasznaloId,
                log.Felhasznalo == null ? null : log.Felhasznalo.Nev,
                log.MuveletTipus,
                log.EntitasTipus,
                log.EntitasId,
                log.RegiErtek,
                log.UjErtek,
                log.Indoklas,
                log.LetrehozasIdeje))
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? Results.NotFound(new { message = "Az auditbejegyzés nem található." })
            : Results.Ok(item);
    }

    private static async Task<ParameterResponse?> FindParameterAsync(
        string key,
        AppDbContext db,
        CancellationToken cancellationToken) =>
        await db.Rendszerparameterek
            .AsNoTracking()
            .Where(parameter => parameter.Kulcs == key)
            .Select(parameter => new ParameterResponse(
                parameter.Kulcs,
                parameter.Ertek,
                parameter.Leiras,
                parameter.ModositasIdeje))
            .SingleOrDefaultAsync(cancellationToken);

    private static string? ValidateParameterValue(string key, string value)
    {
        if (key == ClosingTimeKey &&
            !TimeOnly.TryParseExact(
                value,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            return "A napi zárási időpont HH:mm formátumú legyen (például 22:00).";
        }

        if (key == NoShowLimitKey &&
            (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
             minutes <= 0))
        {
            return "A no-show időkorlát pozitív egész szám legyen percben.";
        }

        return null;
    }

    public sealed record UpdateParameterRequest(string? Ertek, string? Indoklas);

    public sealed record ParameterResponse(
        string Kulcs,
        string Ertek,
        string? Leiras,
        DateTime ModositasIdeje);

    public sealed record AuditLogResponse(
        int Id,
        int? FelhasznaloId,
        string? FelhasznaloNev,
        string MuveletTipus,
        string EntitasTipus,
        string? EntitasId,
        string? RegiErtek,
        string? UjErtek,
        string? Indoklas,
        DateTime LetrehozasIdeje);

    public sealed record AuditLogListResponse(
        IReadOnlyList<AuditLogResponse> Items,
        int TotalCount);
}
