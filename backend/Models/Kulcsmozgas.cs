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
        public Kulcs Kulcs { get; set; }

        // Optional link to a booking
        public int? FoglalasId { get; set; }
        public Foglalas Foglalas { get; set; }

        [Required]
        public string MozgasTipus { get; set; } // Kiadás, Visszavétel, etc.

        [Required]
        public DateTime Idopont { get; set; }

        [ForeignKey("Operátor")]
        public int OperátorId { get; set; }
        public Felhasznalo Operátor { get; set; }

        public string Megjegyzes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
