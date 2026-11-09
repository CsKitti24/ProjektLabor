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
        public string Username { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        public string JelszoHash { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? PinHash { get; set; }

        [Required, MaxLength(100)]
        public string Nev { get; set; } = string.Empty;

        public DateTime? SzuletesiDatum { get; set; }

        [Required]
        public string Role { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Foglalas> Foglalasok { get; set; } = new List<Foglalas>();
        public ICollection<MesterkulcsJogosultsag> MesterkulcsJogosultsagok { get; set; } = new List<MesterkulcsJogosultsag>();
        public ICollection<Kulcsmozgas> Kulcsmozgasok { get; set; } = new List<Kulcsmozgas>();
        public ICollection<Hibajegy> Hibajegyek { get; set; } = new List<Hibajegy>();
        public ICollection<KarbantartasiIdosav> KarbantartasiIdosavok { get; set; } = new List<KarbantartasiIdosav>();
        public ICollection<MegbizhatosagiEsemeny> MegbizhatosagiEsemenyek { get; set; } = new List<MegbizhatosagiEsemeny>();
        public ICollection<AuditLog> AuditLogok { get; set; } = new List<AuditLog>();
    }
}
