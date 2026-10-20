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
        public string Nev { get; set; }

        [Required]
        public int Kapacitas { get; set; }

        [MaxLength(500)]
        public string Leiras { get; set; }

        public bool Aktivalva { get; set; } = true;

        public ICollection<Foglalas> Foglalasok { get; set; }
        public ICollection<Kulcs> Kulcsok { get; set; }
        public ICollection<KarbantartasiIdosav> KarbantartasiIdosavok { get; set; }
        public ICollection<Hibajegy> Hibajegyek { get; set; }
    }
}
