using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class MesterkulcsJogosultsag
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Felhasznalo")]
        public int FelhasznaloId { get; set; }
        public Felhasznalo Felhasznalo { get; set; }

        [ForeignKey("Kulcs")]
        public int KulcsId { get; set; }
        public Kulcs Kulcs { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
