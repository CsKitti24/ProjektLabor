using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace ProjektLabor.Security;

public static partial class DemoUserBootstrapper
{
    private static readonly (string Role, string ConfigurationKey)[] DemoRoles =
    [
        (RoleNames.Oktato, "Oktato"),
        (RoleNames.Portas, "Portas"),
        (RoleNames.Adminisztrator, "Adminisztrator"),
        (RoleNames.UzemeltetesiIgazgato, "UzemeltetesiIgazgato")
    ];

    public static async Task InitializeAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("BootstrapDemoUsers:Enabled"))
        {
            return;
        }

        var users = DemoRoles.Select(item =>
        {
            var username = configuration[$"BootstrapDemoUsers:{item.ConfigurationKey}:Username"]?.Trim();
            var password = configuration[$"BootstrapDemoUsers:{item.ConfigurationKey}:Password"];
            if (username is null ||
                username.Length is < 3 or > 50 ||
                !UsernamePattern().IsMatch(username) ||
                string.IsNullOrWhiteSpace(password) ||
                password.Length < 12 ||
                Encoding.UTF8.GetByteCount(password) > 72)
            {
                throw new InvalidOperationException(
                    $"BootstrapDemoUsers:{item.ConfigurationKey} requires a valid Username and a Password of 12-72 UTF-8 bytes.");
            }

            return new
            {
                item.Role,
                Username = username,
                Password = password,
                Email = $"{username}@demo.projektlabor.invalid"
            };
        }).ToArray();

        if (users
            .GroupBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Demo account usernames must be unique.");
        }

        foreach (var user in users)
        {
            var usernameExists = await db.Felhasznalok
                .AnyAsync(account => account.Username == user.Username, cancellationToken);
            if (usernameExists)
            {
                logger.LogInformation(
                    "Demo account bootstrap skipped for role {Role}; username {Username} already exists.",
                    user.Role,
                    user.Username);
                continue;
            }

            var emailExists = await db.Felhasznalok
                .AnyAsync(account => account.Email == user.Email, cancellationToken);
            if (emailExists)
            {
                logger.LogWarning(
                    "Demo account bootstrap skipped for role {Role}; demo email {Email} already exists.",
                    user.Role,
                    user.Email);
                continue;
            }

            db.Felhasznalok.Add(new Felhasznalo
            {
                Username = user.Username,
                Email = user.Email,
                JelszoHash = BCryptHasher.HashPassword(user.Password, workFactor: 12),
                Nev = user.Role,
                Role = user.Role
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Configured demo accounts have been bootstrapped where usernames were unused.");
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
