using System.Text;
using Microsoft.Extensions.Hosting;

namespace ProjektLabor.Security;

public sealed record JwtSettings(string Issuer, string Audience, string SigningKey)
{
    private const string DevelopmentOnlySigningKey =
        "development-only-signing-key-not-for-production-32bytes";

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
            signingKey == DevelopmentOnlySigningKey)
        {
            throw new InvalidOperationException(
                "The development-only JWT signing key cannot be used outside Development or Testing environments.");
        }

        return new JwtSettings(issuer, audience, signingKey);
    }
}
