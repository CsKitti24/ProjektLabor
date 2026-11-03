using Microsoft.AspNetCore.Authorization;

namespace ProjektLabor.Security;

public static class RoleNames
{
    public const string Oktato = "Oktato";
    public const string Portas = "Portas";
    public const string Adminisztrator = "Adminisztrator";
    public const string UzemeltetesiIgazgato = "UzemeltetesiIgazgato";

    public static readonly string[] All =
    [
        Oktato,
        Portas,
        Adminisztrator,
        UzemeltetesiIgazgato
    ];
}

public static class RolePolicies
{
    public const string Administrator = "Administrator";

    public static void AddAuthorizationPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(Administrator, policy => policy.RequireRole(RoleNames.Adminisztrator));
    }
}
