using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required(ErrorMessage = "A felhasználónév megadása kötelező.")]
        [MaxLength(50, ErrorMessage = "A felhasználónév legfeljebb 50 karakter lehet.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "A jelszó megadása kötelező.")]
        // Bejelentkezéskor nem írunk elő minimális hosszt (a jelszószabályt a beállításkor ellenőrizzük);
        // a felső korlát a túl nagy kérések ellen véd.
        [MaxLength(200, ErrorMessage = "A jelszó túl hosszú.")]
        public string Jelszo { get; set; } = string.Empty;
    }
}
