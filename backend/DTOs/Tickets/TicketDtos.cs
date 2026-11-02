using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Tickets
{
    public class TicketDto
    {
        public int Id { get; set; }
        public int FelhasznaloId { get; set; }
        public string FelhasznaloNev { get; set; } = string.Empty;
        public int TeremId { get; set; }
        public string TeremNev { get; set; } = string.Empty;
        public string Kategoria { get; set; } = string.Empty;
        public string Leiras { get; set; } = string.Empty;
        public string Statusz { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class CreateTicketRequestDto
    {
        [Required(ErrorMessage = "A terem azonosítójának megadása kötelező.")]
        public int TeremId { get; set; }

        [Required(ErrorMessage = "A hiba kategóriájának megadása kötelező.")]
        [MaxLength(100, ErrorMessage = "A kategória legfeljebb 100 karakter lehet.")]
        public string Kategoria { get; set; } = string.Empty;

        [Required(ErrorMessage = "A leírás megadása kötelező.")]
        [MaxLength(1000, ErrorMessage = "A leírás legfeljebb 1000 karakter lehet.")]
        public string Leiras { get; set; } = string.Empty;
    }

    public class UpdateTicketStatusRequestDto
    {
        [Required(ErrorMessage = "Az új státusz megadása kötelező.")]
        public string Statusz { get; set; } = string.Empty;
    }

    public class TicketListDto
    {
        public List<TicketDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
