namespace ProjektLabor.Services;

/// <summary>
/// A kulcsmozgás-típusok egy helyen. A régi, ékezet nélküli értékeket is felismerjük.
/// </summary>
public static class KeyMovementTypes
{
    public const string Issue = "Kiadás";
    public const string LegacyIssue = "Kiadas";
    public const string Return = "Visszavétel";
    public const string LegacyReturn = "Visszavetel";

    public static bool IsIssue(string? movementType) =>
        movementType is Issue or LegacyIssue;

    public static bool IsReturn(string? movementType) =>
        movementType is Return or LegacyReturn;
}
