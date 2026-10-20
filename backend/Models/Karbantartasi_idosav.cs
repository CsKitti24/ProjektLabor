global using Karbantartasi_idosav = ProjektLabor.Models.KarbantartasiIdosav;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class KarbantartasiIdosav
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Felhasznalo")]
        public int FelhasznaloId { get; set; }
        public Felhasznalo Felhasznalo { get; set; } = null!;

        [ForeignKey("Terem")]
        public int TeremId { get; set; }
        public Terem Terem { get; set; } = null!;

        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        public string? Leiras { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
