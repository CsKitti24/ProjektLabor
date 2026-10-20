using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class Foglalas
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
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        [Required]
        public string Status { get; set; } // Expected values: Aktiv, NoShow, Megszuntetve, Kesz

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation collections
        public ICollection<Kulcsmozgas> Kulcsmozgasok { get; set; }
        public ICollection<Ertesites> Ertesitesek { get; set; }
        public ICollection<Megbizhatosagi_esemeny> MegbizhatosagiEsemenyek { get; set; }
    }
}
