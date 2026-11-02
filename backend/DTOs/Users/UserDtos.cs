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
        public DateTime? SzuletesiDatum { get; set; }
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
        [MaxLength(50, ErrorMessage = "A felhasználónév legfeljebb 50 karakter lehet.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Az email cím megadása kötelező.")]
        [EmailAddress(ErrorMessage = "Érvénytelen email cím formátum.")]
        [MaxLength(100, ErrorMessage = "Az email cím legfeljebb 100 karakter lehet.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "A jelszó megadása kötelező.")]
        [MinLength(8, ErrorMessage = "A jelszónak legalább 8 karakter hosszúnak kell lennie.")]
        public string Jelszo { get; set; } = string.Empty;

        [Required(ErrorMessage = "A név megadása kötelező.")]
        [MaxLength(100, ErrorMessage = "A név legfeljebb 100 karakter lehet.")]
        public string Nev { get; set; } = string.Empty;

        [Required(ErrorMessage = "A szerepkör megadása kötelező.")]
        public string Role { get; set; } = string.Empty;

        public DateTime? SzuletesiDatum { get; set; }
    }

    public class UpdateUserRequestDto
    {
        [MaxLength(100, ErrorMessage = "A név legfeljebb 100 karakter lehet.")]
        public string? Nev { get; set; }

        [EmailAddress(ErrorMessage = "Érvénytelen email cím formátum.")]
        [MaxLength(100, ErrorMessage = "Az email cím legfeljebb 100 karakter lehet.")]
        public string? Email { get; set; }

        [MinLength(8, ErrorMessage = "A jelszónak legalább 8 karakter hosszúnak kell lennie.")]
        public string? UjJelszo { get; set; }

        public DateTime? SzuletesiDatum { get; set; }
    }

    public class ChangeRoleRequestDto
    {
        [Required(ErrorMessage = "Az új szerepkör megadása kötelező.")]
        public string UjRole { get; set; } = string.Empty;
    }
}
