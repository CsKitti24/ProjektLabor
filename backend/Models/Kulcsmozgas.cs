using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class Kulcsmozgas
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Kulcs")]
        public int KulcsId { get; set; }
        public Kulcs Kulcs { get; set; } = null!;

        // Optional link to a booking
        public int? FoglalasId { get; set; }
        public Foglalas? Foglalas { get; set; }

        // A terem, amelyre a kulcsot kiadták (mesterkulcsnál ez eltérhet a kulcs saját termétől).
        public int? TeremId { get; set; }

        public int? AtvevoFelhasznaloId { get; set; }
        public Felhasznalo? AtvevoFelhasznalo { get; set; }

        [Required, MaxLength(50)]
        public string MozgasTipus { get; set; } = string.Empty; // Kiadás, Visszavétel, etc.

        [Required]
        public DateTime Idopont { get; set; }

        [ForeignKey("Operátor")]
        public int OperátorId { get; set; }
        public Felhasznalo Operátor { get; set; } = null!;

        public string? Megjegyzes { get; set; }

        [MaxLength(500)]
        public string? TeremAllapot { get; set; }

        [MaxLength(1000)]
        public string? SerulesLeiras { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
