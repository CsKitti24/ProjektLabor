using System;
using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.Models
{
    public class Rendszerparameter
    {
        [Key, MaxLength(100)]
        public string Kulcs { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string Ertek { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Leiras { get; set; }

        public DateTime ModositasIdeje { get; set; } = DateTime.UtcNow;
    }
}
