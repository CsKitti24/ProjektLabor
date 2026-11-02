namespace backend.Tests;

internal static class TestJwtConfiguration
{
    public const string Issuer = "projektlabor-tests";
    public const string Audience = "projektlabor-tests-api";
    public const string SigningKey = "test-only-signing-key-with-at-least-32-bytes";

    public static void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("Authentication__Jwt__Issuer", Issuer);
        Environment.SetEnvironmentVariable("Authentication__Jwt__Audience", Audience);
        Environment.SetEnvironmentVariable("Authentication__Jwt__SigningKey", SigningKey);
    }
}
