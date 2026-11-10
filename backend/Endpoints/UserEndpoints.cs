using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Users;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints
{
    public static class UserEndpoints
    {
        private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Oktato",
            "Portas",
            "Adminisztrator",
            "UzemeltetesiIgazgato"
        };

        public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/users")
                .WithTags("Users")
                .RequireAuthorization("Adminisztrator");

            // U1 – GET /api/users
            group.MapGet("/", async (
                string? role,
                int? page,
                int? pageSize,
                AppDbContext db) =>
            {
                var normalizedPage = page.GetValueOrDefault(1);
                normalizedPage = normalizedPage <= 0 ? 1 : normalizedPage;
                var normalizedPageSize = pageSize.GetValueOrDefault(20);
                normalizedPageSize = normalizedPageSize <= 0 ? 20 : Math.Min(normalizedPageSize, 100);

                var query = db.Felhasznalok.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(role))
                {
                    query = query.Where(u => u.Role == role);
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(u => u.Id)
                    .Skip((normalizedPage - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .Select(u => new UserDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Nev = u.Nev,
                        Email = u.Email,
                        Role = u.Role,
                        SzuletesiDatum = u.SzuletesiDatum,
                        CreatedAt = u.CreatedAt,
                        UpdatedAt = u.UpdatedAt
                    })
                    .ToListAsync();

                return Results.Ok(new UserListDto
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = normalizedPage,
                    PageSize = normalizedPageSize
                });
            })
            .WithName("GetUsers")
            .WithSummary("Felhasználók listázása lapozással és szerepkör szerinti szűréssel")
            .Produces<UserListDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // U2 – GET /api/users/{id}
            group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            {
                var user = await db.Felhasznalok
                    .AsNoTracking()
                    .Where(u => u.Id == id)
                    .Select(u => new UserDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Nev = u.Nev,
                        Email = u.Email,
                        Role = u.Role,
                        SzuletesiDatum = u.SzuletesiDatum,
                        CreatedAt = u.CreatedAt,
                        UpdatedAt = u.UpdatedAt
                    })
                    .FirstOrDefaultAsync();

                if (user is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú felhasználó nem található." });
                }

                return Results.Ok(user);
            })
            .WithName("GetUserById")
            .WithSummary("Egy felhasználó részletes adatainak lekérdezése")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // U3 – POST /api/users
            group.MapPost("/", async (
                CreateUserRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                if (string.IsNullOrWhiteSpace(request.Username) ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Jelszo) ||
                    string.IsNullOrWhiteSpace(request.Nev) ||
                    string.IsNullOrWhiteSpace(request.Role))
                {
                    return Results.BadRequest(new { message = "Minden kötelező mezőt ki kell tölteni." });
                }

                if (request.Jelszo.Length < 8)
                {
                    return Results.BadRequest(new { message = "A jelszónak legalább 8 karakterből kell állnia." });
                }

                var canonicalRole = AllowedRoles.FirstOrDefault(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));
                if (canonicalRole == null)
                {
                    return Results.BadRequest(new { message = $"Érvénytelen szerepkör: '{request.Role}'. Megengedett értékek: Oktato, Portas, Adminisztrator, UzemeltetesiIgazgato." });
                }

                // Check username uniqueness
                bool usernameTaken = await db.Felhasznalok.AnyAsync(u => u.Username == request.Username);
                if (usernameTaken)
                {
                    return Results.Conflict(new { message = $"A megadott felhasználónév ('{request.Username}') már foglalt." });
                }

                // Check email uniqueness
                bool emailTaken = await db.Felhasznalok.AnyAsync(u => u.Email == request.Email);
                if (emailTaken)
                {
                    return Results.Conflict(new { message = $"A megadott email cím ('{request.Email}') már használatban van." });
                }

                var user = new Felhasznalo
                {
                    Username = request.Username.Trim(),
                    Email = request.Email.Trim().ToLowerInvariant(),
                    Nev = request.Nev.Trim(),
                    Role = canonicalRole,
                    SzuletesiDatum = request.SzuletesiDatum,
                    JelszoHash = BCrypt.Net.BCrypt.HashPassword(request.Jelszo, workFactor: 12),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.Felhasznalok.Add(user);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CREATE",
                    entitasTipus: "Felhasznalo",
                    entitasId: user.Id.ToString(),
                    ujErtek: $"Username: {user.Username}, Role: {user.Role}, Email: {user.Email}");

                var result = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Nev = user.Nev,
                    Email = user.Email,
                    Role = user.Role,
                    SzuletesiDatum = user.SzuletesiDatum,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt
                };

                return Results.Created($"/api/users/{user.Id}", result);
            })
            .WithName("CreateUser")
            .WithSummary("Új felhasználó létrehozása")
            .Produces<UserDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // U4 – PUT /api/users/{id}
            group.MapPut("/{id:int}", async (
                int id,
                UpdateUserRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var user = await db.Felhasznalok.FindAsync(id);
                if (user is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú felhasználó nem található." });
                }

                var changes = new List<string>();

                if (!string.IsNullOrWhiteSpace(request.Nev) && request.Nev.Trim() != user.Nev)
                {
                    changes.Add($"Nev: '{user.Nev}' -> '{request.Nev.Trim()}'");
                    user.Nev = request.Nev.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                    if (normalizedEmail != user.Email)
                    {
                        bool emailInUse = await db.Felhasznalok.AnyAsync(u => u.Id != id && u.Email == normalizedEmail);
                        if (emailInUse)
                        {
                            return Results.Conflict(new { message = $"A megadott email cím ('{normalizedEmail}') már használatban van." });
                        }
                        changes.Add($"Email: '{user.Email}' -> '{normalizedEmail}'");
                        user.Email = normalizedEmail;
                    }
                }

                if (!string.IsNullOrWhiteSpace(request.UjJelszo))
                {
                    if (request.UjJelszo.Length < 8)
                    {
                        return Results.BadRequest(new { message = "A jelszónak legalább 8 karakterből kell állnia." });
                    }
                    user.JelszoHash = BCrypt.Net.BCrypt.HashPassword(request.UjJelszo, workFactor: 12);
                    changes.Add("Jelszo modosult");
                }

                if (request.SzuletesiDatum.HasValue && request.SzuletesiDatum != user.SzuletesiDatum)
                {
                    changes.Add($"SzuletesiDatum: '{user.SzuletesiDatum}' -> '{request.SzuletesiDatum}'");
                    user.SzuletesiDatum = request.SzuletesiDatum;
                }

                if (changes.Count > 0)
                {
                    user.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    await auditService.LogAsync(
                        caller,
                        muveletTipus: "UPDATE",
                        entitasTipus: "Felhasznalo",
                        entitasId: id.ToString(),
                        ujErtek: string.Join("; ", changes));
                }

                var result = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Nev = user.Nev,
                    Email = user.Email,
                    Role = user.Role,
                    SzuletesiDatum = user.SzuletesiDatum,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt
                };

                return Results.Ok(result);
            })
            .WithName("UpdateUser")
            .WithSummary("Felhasználó adatainak módosítása")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // U5 – DELETE /api/users/{id}
            group.MapDelete("/{id:int}", async (
                int id,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var user = await db.Felhasznalok.FindAsync(id);
                if (user is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú felhasználó nem található." });
                }

                // Check for active dependencies
                bool hasActiveBookings = await db.Foglalasok.AnyAsync(f => f.FelhasznaloId == id && f.Status == "Aktiv");
                if (hasActiveBookings)
                {
                    return Results.Conflict(new { message = "A felhasználónak aktív foglalásai vannak, ezért nem törölhető." });
                }

                db.Felhasznalok.Remove(user);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "DELETE",
                    entitasTipus: "Felhasznalo",
                    entitasId: id.ToString(),
                    regiErtek: $"Username: {user.Username}, Role: {user.Role}");

                return Results.Ok(new { message = $"A(z) {id} azonosítójú felhasználó sikeresen törölve lett." });
            })
            .WithName("DeleteUser")
            .WithSummary("Felhasználó törlése")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            // U6 – PUT /api/users/{id}/role
            group.MapPut("/{id:int}/role", async (
                int id,
                ChangeRoleRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var user = await db.Felhasznalok.FindAsync(id);
                if (user is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú felhasználó nem található." });
                }

                var canonicalRole = AllowedRoles.FirstOrDefault(r => r.Equals(request.UjRole, StringComparison.OrdinalIgnoreCase));
                if (canonicalRole == null)
                {
                    return Results.BadRequest(new { message = $"Érvénytelen szerepkör: '{request.UjRole}'. Megengedett értékek: Oktato, Portas, Adminisztrator, UzemeltetesiIgazgato." });
                }

                var oldRole = user.Role;
                user.Role = canonicalRole;
                user.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "ROLE_CHANGE",
                    entitasTipus: "Felhasznalo",
                    entitasId: id.ToString(),
                    regiErtek: oldRole,
                    ujErtek: canonicalRole);

                return Results.Ok(new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Nev = user.Nev,
                    Email = user.Email,
                    Role = user.Role,
                    SzuletesiDatum = user.SzuletesiDatum,
                    CreatedAt = user.CreatedAt,
                    UpdatedAt = user.UpdatedAt
                });
            })
            .WithName("ChangeUserRole")
            .WithSummary("Felhasználó szerepkörének módosítása")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            group.MapPut("/{id:int}/pin", async (
                int id,
                SetPinRequest request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                if (string.IsNullOrWhiteSpace(request.Pin) ||
                    request.Pin.Length is < 4 or > 8 ||
                    request.Pin.Any(character => character is < '0' or > '9'))
                {
                    return Results.BadRequest(new { message = "A PIN-kód 4–8 számjegyből állhat." });
                }

                var user = await db.Felhasznalok.FindAsync(id);
                if (user is null)
                {
                    return Results.NotFound(new { message = "A felhasználó nem található." });
                }

                user.PinHash = BCrypt.Net.BCrypt.HashPassword(request.Pin);
                user.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await auditService.LogAsync(
                    caller,
                    "PIN_CHANGE",
                    "Felhasznalo",
                    id.ToString(),
                    ujErtek: "PIN beállítva");

                return Results.NoContent();
            })
            .WithName("SetUserPin")
            .WithSummary("Felhasználói PIN beállítása kulcsátvételhez")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            return group;
        }

        public sealed record SetPinRequest(string Pin);
    }
}
