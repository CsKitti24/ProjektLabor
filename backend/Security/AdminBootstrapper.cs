using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Models;
using BCryptHasher = BCrypt.Net.BCrypt;

namespace ProjektLabor.Security;

public static partial class AdminBootstrapper
{
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
}
