using System.Text;

namespace ProjektLabor.Security;

/// <summary>
/// Egységes jelszószabály: ugyanaz, mint az induló adminisztrátornál.
/// Legalább 12 karakter, és legfeljebb 72 UTF-8 bájt (a BCrypt ennél hosszabbat levágna).
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumUtf8Bytes = 72;

    public static string? Validate(string password)
    {
        if (password.Length < MinimumLength)
        {
            return $"A jelszónak legalább {MinimumLength} karakterből kell állnia.";
        }

        if (Encoding.UTF8.GetByteCount(password) > MaximumUtf8Bytes)
        {
            return "A jelszó túl hosszú (legfeljebb 72 bájt, ékezetes betűknél kevesebb karakter).";
        }

        return null;
    }
}
