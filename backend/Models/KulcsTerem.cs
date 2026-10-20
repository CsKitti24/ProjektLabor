using System.ComponentModel.DataAnnotations.Schema;

namespace ProjektLabor.Models
{
    public class KulcsTerem
    {
        [ForeignKey("Kulcs")]
        public int KulcsId { get; set; }
        public Kulcs Kulcs { get; set; } = null!;

        [ForeignKey("Terem")]
        public int TeremId { get; set; }
        public Terem Terem { get; set; } = null!;
    }
}
