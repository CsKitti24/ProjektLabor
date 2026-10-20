using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.Models
{
    public class Felhasznalo
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Username { get; set; }

        [Required, MaxLength(100)]
        public string Email { get; set; }

        [Required, MaxLength(256)]
        public string JelszoHash { get; set; }

        [Required, MaxLength(100)]
        public string Nev { get; set; }

        public DateTime? SzuletesiDatum { get; set; }

        [Required]
        public string Role { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Foglalas> Foglalasok { get; set; }
        public ICollection<MesterkulcsJogosultsag> MesterkulcsJogosultsagok { get; set; }
        public ICollection<Kulcsmozgas> Kulcsmozgasok { get; set; }
        public ICollection<Hibajegy> Hibajegyek { get; set; }
        public ICollection<KarbantartasiIdosav> KarbantartasiIdosavok { get; set; }
        public ICollection<MegbizhatosagiEsemeny> MegbizhatosagiEsemenyek { get; set; }
        public ICollection<AuditLog> AuditLogok { get; set; }
    }
}
