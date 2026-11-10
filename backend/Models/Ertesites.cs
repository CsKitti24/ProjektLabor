using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class Ertesites
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Foglalas")]
        public int? FoglalasId { get; set; }
        public Foglalas? Foglalas { get; set; }

        public int? CimzettFelhasznaloId { get; set; }
        public Felhasznalo? CimzettFelhasznalo { get; set; }

        public int? KulcsmozgasId { get; set; }
        public Kulcsmozgas? Kulcsmozgas { get; set; }

        public DateTime? Hatarido { get; set; }

        public bool Olvasva { get; set; }

        [Required]
        public string ErtesitesTipus { get; set; } = string.Empty; // BeforeEnd

        [Required]
        public DateTime IdoPont { get; set; }

        [Required]
        public string Allapot { get; set; } = string.Empty; // Kesz, KuldesAlatt, Hiba

        [MaxLength(100)]
        public string? AutomationKey { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
