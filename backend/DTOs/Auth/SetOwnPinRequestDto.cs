using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Auth
{
    /// <summary>
    /// Az oktató saját kulcsátvételi PIN-kódjának beállítása; a jelszó újbóli megadása véd az ellen,
    /// hogy egy nyitva hagyott munkamenetből más állítsa át.
    /// </summary>
    public class SetOwnPinRequestDto
    {
        [Required(ErrorMessage = "A jelenlegi jelszó megadása kötelező.")]
        [MaxLength(200, ErrorMessage = "A jelszó túl hosszú.")]
        public string JelenlegiJelszo { get; set; } = string.Empty;

        [Required(ErrorMessage = "A PIN-kód megadása kötelező.")]
        [RegularExpression("^[0-9]{4,8}$", ErrorMessage = "A PIN-kód 4–8 számjegyből állhat.")]
        public string Pin { get; set; } = string.Empty;
    }
}
