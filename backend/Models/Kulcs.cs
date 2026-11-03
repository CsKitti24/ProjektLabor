// Models/Kulcs.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class Kulcs
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(20)]
        public string Szam { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Tipus { get; set; } = string.Empty; // Mesterkulcs, Osztalykulcs

        public bool Aktivalva { get; set; } = true;

        // Foreign key to Terem
        public int TeremId { get; set; }
        public Terem Terem { get; set; } = null!;

        public ICollection<Kulcsmozgas> Kulcsmozgasok { get; set; } = new List<Kulcsmozgas>();
        public ICollection<MesterkulcsJogosultsag> MesterkulcsJogosultsagok { get; set; } = new List<MesterkulcsJogosultsag>();
        public ICollection<MegbizhatosagiEsemeny> MegbizhatosagiEsemenyek { get; set; } = new List<MegbizhatosagiEsemeny>();
        public ICollection<KulcsTerem> KulcsTeremKapcsok { get; set; } = new List<KulcsTerem>();
    }
}
