using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Auth
{
    public class ChangePasswordRequestDto
    {
        [Required(ErrorMessage = "A jelenlegi jelszó megadása kötelező.")]
        [MaxLength(200, ErrorMessage = "A jelszó túl hosszú.")]
        public string JelenlegiJelszo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Az új jelszó megadása kötelező.")]
        [MaxLength(200, ErrorMessage = "A jelszó túl hosszú.")]
        public string UjJelszo { get; set; } = string.Empty;
    }
}
