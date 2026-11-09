using System.ComponentModel.DataAnnotations;
using ProjektLabor.DTOs.Keys;

namespace ProjektLabor.DTOs.KeyMovements
{
    public class IssueKeyRequestDto
    {
        [Required(ErrorMessage = "A kulcs azonosítójának megadása kötelező.")]
        public int KulcsId { get; set; }

        public int? FoglalasId { get; set; }

        public int? AtvevoFelhasznaloId { get; set; }

        [Required]
        [RegularExpression("^[0-9]{4,8}$", ErrorMessage = "A PIN-kód 4–8 számjegyből állhat.")]
        public string Pin { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string TeremAllapot { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? SerulesLeiras { get; set; }

        [MaxLength(500, ErrorMessage = "A megjegyzés legfeljebb 500 karakter lehet.")]
        public string? Megjegyzes { get; set; }
    }

    public class ReturnKeyRequestDto
    {
        [Required(ErrorMessage = "A kulcs azonosítójának megadása kötelező.")]
        public int KulcsId { get; set; }

        [Required]
        [RegularExpression("^[0-9]{4,8}$", ErrorMessage = "A PIN-kód 4–8 számjegyből állhat.")]
        public string Pin { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string TeremAllapot { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? SerulesLeiras { get; set; }

        [MaxLength(500, ErrorMessage = "A megjegyzés legfeljebb 500 karakter lehet.")]
        public string? Megjegyzes { get; set; }
    }

    public class KeyMovementListDto
    {
        public List<KeyMovementItemDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class OverdueKeyDto
    {
        public int KulcsId { get; set; }
        public string KulcsSzam { get; set; } = string.Empty;
        public int TeremId { get; set; }
        public string TeremNev { get; set; } = string.Empty;
        public DateTime KiadasIdeje { get; set; }
        public int? FoglalasId { get; set; }
        public DateTime? FoglalasVége { get; set; }
        public string? BirtokosNev { get; set; }
        public string? BirtokosEmail { get; set; }
        public double KésésPercekben { get; set; }
    }
}
