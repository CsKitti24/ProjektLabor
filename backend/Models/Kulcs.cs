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
        public string Szam { get; set; }

        [Required, MaxLength(20)]
        public string Tipus { get; set; } // Mesterkulcs, Osztalykulcs

        public bool Aktivalva { get; set; } = true;

        // Foreign key to Terem
        public int TeremId { get; set; }
        public Terem Terem { get; set; }

        public ICollection<Kulcsmozgas> Kulcsmozgasok { get; set; }
        public ICollection<MesterkulcsJogosultsag> MesterkulcsJogosultsagok { get; set; }
        public ICollection<MegbizhatosagiEsemeny> MegbizhatosagiEsemenyek { get; set; }
        public ICollection<KulcsTerem> KulcsTeremKapcsok { get; set; }
    }
}
