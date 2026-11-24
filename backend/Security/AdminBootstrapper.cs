using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace ProjektLabor.Security;

public static partial class AdminBootstrapper
{
    /// <summary>
    /// A SeedRoleUsersAndReliabilitySample migráció mintafiókjainak jelszó-hashei. A jelszavak a
    /// repóban dokumentáltak (pl. Adminjelszo1!), ezért ezek a fiókok csak fejlesztéshez/bemutatóhoz
    /// valók. Ha egy fiók hashe még ezek egyike, a jelszót senki nem változtatta meg.
    /// </summary>
    public static readonly string[] KnownSampleAccountPasswordHashes =
    [
        "$2a$12$23q0uRMoKUDlL4/dQS/adu.n.7obQn/GEpOL8A8g55Kk9hVjTf.Va",
        "$2a$12$ysGT0/JdNYd5OITODFM39O99A1U26i39gAYc78xkY.4qA3Z9dg.YW",
        "$2a$12$01raKmkR/7zM.ir.imSfJeqb9mFAI1eQiL0h69VranJfdzLrYQhza",
        "$2a$12$.g.1NVsA0WE6.pHDjL1vzOdTKgUG0LftPmNsWlCDUKk0J3kYpq2Ye",
        "$2a$12$/.w79qsFMyAn6Z0iizLEXOZIprTqrBi/DPJQz/9AyQdzsTA9cTs/2"
    ];

    public static async Task InitializeAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // A mintafiókok jelszava nyilvános. Csak kifejezett engedéllyel (SampleAccounts:Enabled=true,
        // pl. fejlesztéskor vagy bemutatón) maradhatnak aktívak; egyébként induláskor inaktiváljuk azokat,
        // amelyeknek a jelszavát még nem változtatták meg.
        if (!configuration.GetValue("SampleAccounts:Enabled", false))
        {
            var exposedSampleAccounts = await db.Felhasznalok
                .Where(user => user.Aktiv && KnownSampleAccountPasswordHashes.Contains(user.JelszoHash))
                .ToListAsync(cancellationToken);
            if (exposedSampleAccounts.Count > 0)
            {
                foreach (var user in exposedSampleAccounts)
                {
                    user.Aktiv = false;
                    user.UpdatedAt = DateTime.UtcNow;
                }

                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning(
                    "Deactivated {Count} sample account(s) that still use their publicly known default password: {Usernames}. " +
                    "Set SampleAccounts:Enabled=true only for development or demonstrations.",
                    exposedSampleAccounts.Count,
                    string.Join(", ", exposedSampleAccounts.Select(user => user.Username)));
            }
        }

        var hasActiveAdministrator = await db.Felhasznalok.AnyAsync(
            user => user.Aktiv && user.Role == RoleNames.Adminisztrator,
            cancellationToken);
        if (hasActiveAdministrator)
        {
            return;
        }

        var username = configuration["BootstrapAdmin:Username"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        ValidateCredentials(username, password, "BootstrapAdmin");

        var passwordHash = BCryptHasher.HashPassword(password!, workFactor: 12);
        var existing = await db.Felhasznalok.SingleOrDefaultAsync(user => user.Username == username, cancellationToken);
        if (existing is not null)
        {
            // Csak egy (letiltott) mintafiókot veszünk át; valódi, más által használt fiókot nem írunk felül.
            if (!KnownSampleAccountPasswordHashes.Contains(existing.JelszoHash))
            {
                throw new InvalidOperationException(
                    $"No active administrator exists and the BootstrapAdmin username '{username}' already belongs to another account. " +
                    "Choose a different BootstrapAdmin:Username.");
            }

            existing.JelszoHash = passwordHash;
            existing.Role = RoleNames.Adminisztrator;
            existing.Aktiv = true;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.Felhasznalok.Add(new Felhasznalo
            {
                Username = username!,
                Email = $"{username}@localhost",
                JelszoHash = passwordHash,
                Nev = username!,
                Role = RoleNames.Adminisztrator
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Initial administrator account '{Username}' is ready.", username);
    }

    private static void ValidateCredentials(string? username, string? password, string configurationKey)
    {
        if (username is null ||
            username.Length is < 3 or > 50 ||
            !UsernamePattern().IsMatch(username) ||
            string.IsNullOrWhiteSpace(password) ||
            password.Length < 12 ||
            Encoding.UTF8.GetByteCount(password) > 72 ||
            username.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) ||
            password.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The empty database requires {configurationKey} with a valid username and a password between 12 and 72 UTF-8 bytes " +
                "(no active administrator exists; placeholder REPLACE_WITH_ values are not accepted).");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
