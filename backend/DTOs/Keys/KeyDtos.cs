using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Keys
{
    public class KeyDto
    {
        public int Id { get; set; }
        public string Szam { get; set; } = string.Empty;
        public string Tipus { get; set; } = string.Empty;
        public bool Aktivalva { get; set; }
        public int TeremId { get; set; }
        public string TeremNev { get; set; } = string.Empty;
        public string JelenlegiAllapot { get; set; } = string.Empty; // Szabad | Kiadva | Deaktivalt
    }

    public class CreateKeyRequestDto
    {
        [Required(ErrorMessage = "A kulcsszám megadása kötelező.")]
        [MaxLength(20, ErrorMessage = "A kulcsszám legfeljebb 20 karakter lehet.")]
        public string Szam { get; set; } = string.Empty;

        [Required(ErrorMessage = "A kulcstípus megadása kötelező.")]
        [RegularExpression("^(Mesterkulcs|Osztalykulcs)$", ErrorMessage = "A kulcstípus csak 'Mesterkulcs' vagy 'Osztalykulcs' lehet.")]
        public string Tipus { get; set; } = string.Empty;

        [Required(ErrorMessage = "A terem megadása kötelező.")]
        public int TeremId { get; set; }

        public bool Aktivalva { get; set; } = true;
    }

    public class UpdateKeyRequestDto
    {
        [MaxLength(20, ErrorMessage = "A kulcsszám legfeljebb 20 karakter lehet.")]
        public string? Szam { get; set; }

        [RegularExpression("^(Mesterkulcs|Osztalykulcs)$", ErrorMessage = "A kulcstípus csak 'Mesterkulcs' vagy 'Osztalykulcs' lehet.")]
        public string? Tipus { get; set; }

        public int? TeremId { get; set; }

        public bool? Aktivalva { get; set; }
    }

    public class KeyStatusDto
    {
        public int KulcsId { get; set; }
        public string Szam { get; set; } = string.Empty;
        public string Allapot { get; set; } = string.Empty; // Szabad | Kiadva | Deaktivalt
        public DateTime? KiadasIdopontja { get; set; }
        public string? KiadtaOperatorNev { get; set; }
        public string? AktualisBirtokosNev { get; set; }
        public int? KapcsolodoFoglalasId { get; set; }
    }

    public class KeyMovementItemDto
    {
        public int Id { get; set; }
        public int KulcsId { get; set; }
        public string KulcsSzam { get; set; } = string.Empty;
        public string MozgasTipus { get; set; } = string.Empty;
        public DateTime Idopont { get; set; }
        public int? FoglalasId { get; set; }
        public string? FoglalasFelhasznaloNev { get; set; }
        public int OperatorId { get; set; }
        public string OperatorNev { get; set; } = string.Empty;
        public string Megjegyzes { get; set; } = string.Empty;
    }
}
