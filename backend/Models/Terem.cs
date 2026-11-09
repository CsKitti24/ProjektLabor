// Models/Terem.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.Models
{
    public class Terem
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Nev { get; set; } = string.Empty;

        [Required]
        public int Kapacitas { get; set; }

        [MaxLength(500)]
        public string? Leiras { get; set; }

        [MaxLength(500)]
        public string? Felszereltseg { get; set; }

        public bool Aktivalva { get; set; } = true;

        public ICollection<Foglalas> Foglalasok { get; set; } = new List<Foglalas>();
        public ICollection<Kulcs> Kulcsok { get; set; } = new List<Kulcs>();
        public ICollection<KarbantartasiIdosav> KarbantartasiIdosavok { get; set; } = new List<KarbantartasiIdosav>();
        public ICollection<Hibajegy> Hibajegyek { get; set; } = new List<Hibajegy>();
        public ICollection<KulcsTerem> KulcsTeremKapcsok { get; set; } = new List<KulcsTerem>();
    }
}
