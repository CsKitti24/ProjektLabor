using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Users
{
    public class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Nev { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool Aktiv { get; set; }
        // Van-e beállított PIN-kód (kulcsátvételhez szükséges); maga a PIN sosem kerül ki.
        public bool HasPin { get; set; }
        // Foglalási jog felfüggesztése (null = nincs felfüggesztve).
        public DateTime? FoglalasFelfuggesztveEddig { get; set; }
        public string? FelfuggesztesOka { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class UserListDto
    {
        public List<UserDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class CreateUserRequestDto
    {
        [Required(ErrorMessage = "A felhasználónév megadása kötelező.")]
        [MinLength(3, ErrorMessage = "A felhasználónév legalább 3 karakter legyen.")]
        [MaxLength(50, ErrorMessage = "A felhasználónév legfeljebb 50 karakter lehet.")]
        [RegularExpression(@"^\s*[A-Za-z0-9._-]+\s*$",
            ErrorMessage = "A felhasználónév csak angol betűket, számjegyeket, pontot, kötőjelet és aláhúzást tartalmazhat.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Az email cím megadása kötelező.")]
        [EmailAddress(ErrorMessage = "Érvénytelen email cím formátum.")]
        [MaxLength(100, ErrorMessage = "Az email cím legfeljebb 100 karakter lehet.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "A jelszó megadása kötelező.")]
        [MinLength(12, ErrorMessage = "A jelszónak legalább 12 karakter hosszúnak kell lennie.")]
        public string Jelszo { get; set; } = string.Empty;

        [Required(ErrorMessage = "A név megadása kötelező.")]
        [MaxLength(100, ErrorMessage = "A név legfeljebb 100 karakter lehet.")]
        public string Nev { get; set; } = string.Empty;

        [Required(ErrorMessage = "A szerepkör megadása kötelező.")]
        public string Role { get; set; } = string.Empty;

    }

    public class UpdateUserRequestDto
    {
        [MaxLength(100, ErrorMessage = "A név legfeljebb 100 karakter lehet.")]
        public string? Nev { get; set; }

        [EmailAddress(ErrorMessage = "Érvénytelen email cím formátum.")]
        [MaxLength(100, ErrorMessage = "Az email cím legfeljebb 100 karakter lehet.")]
        public string? Email { get; set; }

        [MinLength(12, ErrorMessage = "A jelszónak legalább 12 karakter hosszúnak kell lennie.")]
        public string? UjJelszo { get; set; }

    }

    public class BookingSuspensionRequestDto
    {
        // A felfüggesztés vége (UTC vagy helyi idő); null a felfüggesztés feloldása.
        public DateTime? Eddig { get; set; }

        [MaxLength(500, ErrorMessage = "Az indoklás legfeljebb 500 karakter lehet.")]
        public string? Indoklas { get; set; }
    }

    public class ChangeRoleRequestDto
    {
        [Required(ErrorMessage = "Az új szerepkör megadása kötelező.")]
        public string UjRole { get; set; } = string.Empty;
    }
}
