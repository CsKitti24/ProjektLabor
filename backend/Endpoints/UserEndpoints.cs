using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Users;
using ProjektLabor.Models;
using ProjektLabor.Security;
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
                string? kereses,
                bool? aktiv,
                int? page,
                int? pageSize,
                AppDbContext db) =>
            {
                var normalizedPage = Pagination.NormalizePage(page);
                var normalizedPageSize = pageSize.GetValueOrDefault(20);
                normalizedPageSize = normalizedPageSize <= 0 ? 20 : Math.Min(normalizedPageSize, 100);

                var query = db.Felhasznalok.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(role))
                {
                    query = query.Where(u => u.Role == role);
                }

                if (aktiv.HasValue)
                {
                    query = query.Where(u => u.Aktiv == aktiv.Value);
                }

                // Szabad szöveges keresés névben, felhasználónévben és e-mail-címben (kis- és nagybetűtől függetlenül).
                if (!string.IsNullOrWhiteSpace(kereses))
                {
                    var term = kereses.Trim().ToLower();
                    query = query.Where(u =>
                        u.Nev.ToLower().Contains(term) ||
                        u.Username.ToLower().Contains(term) ||
                        u.Email.ToLower().Contains(term));
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderBy(u => u.Nev)
                    .ThenBy(u => u.Id)
                    .Skip((normalizedPage - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .Select(u => new UserDto
                    {
                        Id = u.Id,
                        Username = u.Username,
                        Nev = u.Nev,
                        Email = u.Email,
                        Role = u.Role,
                        Aktiv = u.Aktiv,
                        HasPin = u.PinHash != null,
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
            .WithSummary("Felhasználók listázása lapozással, szerepkör/aktív állapot szerinti szűréssel és kereséssel")
            .Produces<UserListDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

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
                        Aktiv = u.Aktiv,
                        HasPin = u.PinHash != null,
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
            .Produces(StatusCodes.Status403Forbidden);

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

                var passwordError = PasswordPolicy.Validate(request.Jelszo);
                if (passwordError is not null)
                {
                    return Results.BadRequest(new { message = passwordError });
                }

                // Az ellenőrzés ugyanazzal a (levágott, kisbetűs) értékkel fut, amelyet elmentünk,
                // így a szóközzel kezdődő név sem okozhat egyediségi hibát (500).
                var username = request.Username.Trim();
                var email = request.Email.Trim().ToLowerInvariant();

                var canonicalRole = AllowedRoles.FirstOrDefault(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));
                if (canonicalRole == null)
                {
                    return Results.BadRequest(new { message = $"Érvénytelen szerepkör: '{request.Role}'. Megengedett értékek: Oktato, Portas, Adminisztrator, UzemeltetesiIgazgato." });
                }

                // Check username uniqueness
                bool usernameTaken = await db.Felhasznalok.AnyAsync(u => u.Username == username);
                if (usernameTaken)
                {
                    return Results.Conflict(new { message = $"A megadott felhasználónév ('{username}') már foglalt." });
                }

                // Check email uniqueness
                bool emailTaken = await db.Felhasznalok.AnyAsync(u => u.Email == email);
                if (emailTaken)
                {
                    return Results.Conflict(new { message = $"A megadott email cím ('{email}') már használatban van." });
                }

                var user = new Felhasznalo
                {
                    Username = username,
                    Email = email,
                    Nev = request.Nev.Trim(),
                    Role = canonicalRole,
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
                    Aktiv = user.Aktiv,
                    HasPin = user.PinHash != null,
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
            .Produces(StatusCodes.Status403Forbidden);

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
                    var newPasswordError = PasswordPolicy.Validate(request.UjJelszo);
                    if (newPasswordError is not null)
                    {
                        return Results.BadRequest(new { message = newPasswordError });
                    }
                    user.JelszoHash = BCrypt.Net.BCrypt.HashPassword(request.UjJelszo, workFactor: 12);
                    // A felhasználó meglévő bejelentkezései érvényüket vesztik.
                    user.JelszoValtozas = DateTime.UtcNow;
                    changes.Add("Jelszo modosult");
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
                    Aktiv = user.Aktiv,
                    HasPin = user.PinHash != null,
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
            .Produces(StatusCodes.Status403Forbidden);

            // U5 – DELETE /api/users/{id}
            // Fizikai törlés helyett inaktiválás: a foglalások, kulcsmozgások és hibajegyek
            // előzményei megmaradnak, az inaktív felhasználó pedig nem tud bejelentkezni.
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

                if (!user.Aktiv)
                {
                    return Results.Conflict(new { message = "A felhasználó már inaktív." });
                }

                if (IsCaller(caller, id))
                {
                    return Results.BadRequest(new { message = "Saját magadat nem törölheted." });
                }

                if (await IsLastActiveAdministratorAsync(db, user))
                {
                    return Results.Conflict(new { message = "Az utolsó aktív adminisztrátor nem törölhető." });
                }

                bool hasActiveBookings = await db.Foglalasok.AnyAsync(f => f.FelhasznaloId == id && f.Status == BookingRules.ActiveStatus);
                if (hasActiveBookings)
                {
                    return Results.Conflict(new { message = "A felhasználónak aktív foglalásai vannak, ezért nem törölhető." });
                }

                if (await HoldsKeyAsync(db, id))
                {
                    return Results.Conflict(new { message = "A felhasználónál jelenleg kulcs van, ezért nem törölhető." });
                }

                user.Aktiv = false;
                user.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "DEACTIVATE",
                    entitasTipus: "Felhasznalo",
                    entitasId: id.ToString(),
                    regiErtek: $"Username: {user.Username}, Role: {user.Role}, Aktiv: true",
                    ujErtek: "Aktiv: false");

                return Results.Ok(new { message = $"A(z) {id} azonosítójú felhasználó inaktiválva lett." });
            })
            .WithName("DeleteUser")
            .WithSummary("Felhasználó törlése (inaktiválás)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

            // U5b – POST /api/users/{id}/reactivate
            group.MapPost("/{id:int}/reactivate", async (
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

                if (user.Aktiv)
                {
                    return Results.Conflict(new { message = "A felhasználó már aktív." });
                }

                user.Aktiv = true;
                user.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "REACTIVATE",
                    entitasTipus: "Felhasznalo",
                    entitasId: id.ToString(),
                    regiErtek: "Aktiv: false",
                    ujErtek: "Aktiv: true");

                return Results.Ok(new { message = $"A(z) {id} azonosítójú felhasználó újra aktív." });
            })
            .WithName("ReactivateUser")
            .WithSummary("Inaktivált felhasználó újraaktiválása")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

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

                if (user.Role == RoleNames.Adminisztrator && canonicalRole != RoleNames.Adminisztrator)
                {
                    if (IsCaller(caller, id))
                    {
                        return Results.BadRequest(new { message = "Saját adminisztrátori szerepkörödet nem vonhatod vissza." });
                    }

                    if (await IsLastActiveAdministratorAsync(db, user))
                    {
                        return Results.Conflict(new { message = "Az utolsó aktív adminisztrátor szerepköre nem módosítható." });
                    }
                }

                var removedGrants = new List<MesterkulcsJogosultsag>();
                if (user.Role == RoleNames.Oktato && canonicalRole != RoleNames.Oktato)
                {
                    // Oktatói foglalás és kulcs csak oktatóhoz tartozhat: előbb ezeket kell rendezni.
                    var now = DateTime.UtcNow;
                    if (await db.Foglalasok.AnyAsync(booking =>
                            booking.FelhasznaloId == id &&
                            booking.Status == BookingRules.ActiveStatus &&
                            booking.EndTime > now))
                    {
                        return Results.Conflict(new { message = "Az oktatónak aktív foglalásai vannak; a szerepkör ezek lemondása után módosítható." });
                    }

                    if (await HoldsKeyAsync(db, id))
                    {
                        return Results.Conflict(new { message = "Az oktatónál jelenleg kulcs van; a szerepkör a visszavétel után módosítható." });
                    }

                    // A mesterkulcs-jogosultság csak oktatónak szól, ezért megszűnik.
                    removedGrants = await db.MesterkulcsJogosultsagok.Where(access => access.FelhasznaloId == id).ToListAsync();
                    db.MesterkulcsJogosultsagok.RemoveRange(removedGrants);
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
                    ujErtek: canonicalRole,
                    indoklas: removedGrants.Count > 0
                        ? $"{removedGrants.Count} mesterkulcs-jogosultság megszűnt (KulcsId: {string.Join(",", removedGrants.Select(access => access.KulcsId))})."
                        : null);

                return Results.Ok(new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Nev = user.Nev,
                    Email = user.Email,
                    Role = user.Role,
                    Aktiv = user.Aktiv,
                    HasPin = user.PinHash != null,
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
            .Produces(StatusCodes.Status403Forbidden);

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
                // Új PIN beállításakor a korábbi hibás próbálkozások és a zárolás megszűnik.
                user.PinHibasProbalkozasok = 0;
                user.PinZarolvaEddig = null;
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
            .Produces(StatusCodes.Status403Forbidden);

            return group;
        }

        public sealed record SetPinRequest(string Pin);

        // A felhasználónál van-e most kulcs (valamelyik kulcs legutolsó mozgása neki szóló kiadás).
        private static Task<bool> HoldsKeyAsync(AppDbContext db, int userId) =>
            db.Kulcsmozgasok
                .Where(movement =>
                    movement.AtvevoFelhasznaloId == userId &&
                    !db.Kulcsmozgasok.Any(newer =>
                        newer.KulcsId == movement.KulcsId &&
                        (newer.Idopont > movement.Idopont ||
                         (newer.Idopont == movement.Idopont && newer.Id > movement.Id))))
                .AnyAsync(movement =>
                    movement.MozgasTipus == KeyMovementTypes.Issue ||
                    movement.MozgasTipus == KeyMovementTypes.LegacyIssue);

        private static bool IsCaller(ClaimsPrincipal caller, int userId)
        {
            var subject = caller.FindFirst("sub")?.Value ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(subject, out var callerId) && callerId == userId;
        }

        private static async Task<bool> IsLastActiveAdministratorAsync(AppDbContext db, Felhasznalo user) =>
            user.Role == RoleNames.Adminisztrator &&
            user.Aktiv &&
            !await db.Felhasznalok.AnyAsync(other =>
                other.Id != user.Id &&
                other.Aktiv &&
                other.Role == RoleNames.Adminisztrator);
    }
}
