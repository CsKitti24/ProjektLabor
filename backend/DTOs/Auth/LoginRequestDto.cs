using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "A felhasználónév megadása kötelező.")]
        [MaxLength(50, ErrorMessage = "A felhasználónév legfeljebb 50 karakter lehet.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "A jelszó megadása kötelező.")]
        [MinLength(6, ErrorMessage = "A jelszó legalább 6 karakter kell legyen.")]
        public string Jelszo { get; set; } = string.Empty;
    }
}
