global using Audit_log = ProjektLabor.Models.AuditLog;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Felhasznalo")]
        [InverseProperty("AuditLogok")]
        public int? FelhasznaloId { get; set; }
        public Felhasznalo? Felhasznalo { get; set; }

        [Required, MaxLength(50)]
        public string MuveletTipus { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string EntitasTipus { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? EntitasId { get; set; }

        public string? RegiErtek { get; set; }

        public string? UjErtek { get; set; }

        [MaxLength(500)]
        public string? Indoklas { get; set; }

        public DateTime LetrehozasIdeje { get; set; } = DateTime.UtcNow;
    }
}
