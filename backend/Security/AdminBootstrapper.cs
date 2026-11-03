using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace ProjektLabor.Security;

public static partial class AdminBootstrapper
{
    private static readonly DemoUserDefinition[] DemoUsers =
    [
        new("demo-oktato", "Oktato", "Demo Oktato"),
        new("demo-portas", "Portas", "Demo Portas"),
        new("demo-admin", "Adminisztrator", "Demo Adminisztrator"),
        new("demo-igazgato", "UzemeltetesiIgazgato", "Demo Uzemeltetesi Igazgato")
    ];

    public static async Task InitializeAsync(
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Felhasznalok.AnyAsync(cancellationToken))
        {
            var username = configuration["BootstrapAdmin:Username"]?.Trim();
            var password = configuration["BootstrapAdmin:Password"];
            ValidateCredentials(username, password, "BootstrapAdmin");

            db.Felhasznalok.Add(new Felhasznalo
            {
                Username = username!,
                Email = $"{username}@localhost",
                JelszoHash = BCryptHasher.HashPassword(password!, workFactor: 12),
                Nev = username!,
                Role = RoleNames.Adminisztrator
            });

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Initial administrator account created.");
        }

        foreach (var demoUser in DemoUsers)
        {
            var password = configuration[$"DemoUsers:{demoUser.ConfigurationKey}:Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning(
                    "Demo user {Username} was not seeded because its password is not configured.",
                    demoUser.Username);
                continue;
            }

            ValidateCredentials(demoUser.Username, password, $"DemoUsers:{demoUser.ConfigurationKey}");
            if (await db.Felhasznalok.AnyAsync(user => user.Username == demoUser.Username, cancellationToken))
            {
                continue;
            }

            db.Felhasznalok.Add(new Felhasznalo
            {
                Username = demoUser.Username,
                Email = $"{demoUser.Username}@localhost",
                JelszoHash = BCryptHasher.HashPassword(password, workFactor: 12),
                Nev = demoUser.DisplayName,
                Role = demoUser.Role
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Configured demo users have been seeded idempotently.");
    }

    private static void ValidateCredentials(string? username, string? password, string configurationKey)
    {
        if (username is null ||
            username.Length is < 3 or > 50 ||
            !UsernamePattern().IsMatch(username) ||
            string.IsNullOrWhiteSpace(password) ||
            password.Length < 12 ||
            Encoding.UTF8.GetByteCount(password) > 72)
        {
            throw new InvalidOperationException(
                $"The empty database requires {configurationKey} with a valid username and a password between 12 and 72 UTF-8 bytes.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    private sealed record DemoUserDefinition(string Username, string Role, string DisplayName)
    {
        public string ConfigurationKey => Role switch
        {
            "Oktato" => "Oktato",
            "Portas" => "Portas",
            "Adminisztrator" => "Adminisztrator",
            "UzemeltetesiIgazgato" => "UzemeltetesiIgazgato",
            _ => throw new InvalidOperationException($"Unsupported demo role: {Role}")
        };
    }
}
