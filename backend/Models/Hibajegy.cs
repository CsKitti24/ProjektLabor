using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class Hibajegy
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Felhasznalo")]
        public int FelhasznaloId { get; set; }
        public Felhasznalo Felhasznalo { get; set; }

        [ForeignKey("Terem")]
        public int TeremId { get; set; }
        public Terem Terem { get; set; }

        [Required]
        public string Kategoria { get; set; }

        [Required]
        public string Leiras { get; set; }

        [Required]
        public string Statusz { get; set; } // Nyitva, Folyamatban, Megoldva, Zarva

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
