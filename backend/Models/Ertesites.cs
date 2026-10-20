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
        public int FoglalasId { get; set; }
        public Foglalas Foglalas { get; set; }

        [Required]
        public string ErtesitesTipus { get; set; } // BeforeEnd

        [Required]
        public DateTime IdoPont { get; set; }

        [Required]
        public string Allapot { get; set; } // Kesz, KuldesAlatt, Hiba

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
