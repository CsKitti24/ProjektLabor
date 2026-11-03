using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ProjektLabor.Security;

public sealed record JwtSettings(string Issuer, string Audience, string SigningKey)
{
    private const string DevelopmentOnlySigningKey =
        "development-only-signing-key-not-for-production-32bytes";
    private const string PreviouslyCheckedInSigningKey =
        "ProjektLaborDevSecretKey_ChangeInProduction_MinLength32Chars!";

    public static JwtSettings FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var issuer = configuration["Authentication:Jwt:Issuer"];
        var audience = configuration["Authentication:Jwt:Audience"];
        var signingKey = configuration["Authentication:Jwt:SigningKey"];

        if (string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience) ||
            string.IsNullOrWhiteSpace(signingKey) ||
            Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:Issuer, Authentication:Jwt:Audience, and an Authentication:Jwt:SigningKey of at least 32 bytes must be configured.");
        }

        if (!environment.IsDevelopment() &&
            !environment.IsEnvironment("Testing") &&
            (signingKey is DevelopmentOnlySigningKey or PreviouslyCheckedInSigningKey ||
             signingKey.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "A known development or placeholder JWT signing key cannot be used outside Development or Testing environments.");
        }

        return new JwtSettings(issuer, audience, signingKey);
    }

    public SymmetricSecurityKey GetSymmetricSecurityKey() =>
        new(Encoding.UTF8.GetBytes(SigningKey));
}
