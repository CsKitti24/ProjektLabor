global using Megbizhatosagi_esemeny = ProjektLabor.Models.MegbizhatosagiEsemeny;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class MegbizhatosagiEsemeny
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Felhasznalo")]
        [InverseProperty("MegbizhatosagiEsemenyek")]
        public int FelhasznaloId { get; set; }
        public Felhasznalo Felhasznalo { get; set; } = null!;

        [ForeignKey("Kulcs")]
        [InverseProperty("MegbizhatosagiEsemenyek")]
        public int? KulcsId { get; set; }
        public Kulcs? Kulcs { get; set; }

        [ForeignKey("Foglalas")]
        [InverseProperty("MegbizhatosagiEsemenyek")]
        public int? FoglalasId { get; set; }
        public Foglalas? Foglalas { get; set; }

        [ForeignKey("KiadasiKulcsmozgas")]
        public int? KiadasiKulcsmozgasId { get; set; }
        public Kulcsmozgas? KiadasiKulcsmozgas { get; set; }

        [Required, MaxLength(50)]
        public string EsemenyTipus { get; set; } = string.Empty; // Időben leadott, Késedelmesen leadott, Le nem adott, Elhagyott, Manuális korrekció

        [ForeignKey("LetrehozoFelhasznalo")]
        public int? LetrehozoFelhasznaloId { get; set; }
        public Felhasznalo? LetrehozoFelhasznalo { get; set; }

        [MaxLength(500)]
        public string? Indoklas { get; set; }

        public DateTime? EredetiHatarido { get; set; }

        public DateTime? TenylegesLeadasIdeje { get; set; }

        [Required]
        public int Pont { get; set; }

        public DateTime EsemenyIdopontja { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(20)]
        public string Tanev { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
