using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Bookings
{
    public class BookingDto
    {
        public int Id { get; set; }
        public int FelhasznaloId { get; set; }
        public string FelhasznaloNev { get; set; } = string.Empty;
        public string FelhasznaloEmail { get; set; } = string.Empty;
        public int TeremId { get; set; }
        public string TeremNev { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? SpecialisIgenyek { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class CreateBookingRequestDto
    {
        [Required(ErrorMessage = "A terem azonosítójának megadása kötelező.")]
        public int TeremId { get; set; }

        public int? FelhasznaloId { get; set; }

        [Required(ErrorMessage = "A kezdési időpont megadása kötelező.")]
        public DateTime StartTime { get; set; }

        [Required(ErrorMessage = "A befejezési időpont megadása kötelező.")]
        public DateTime EndTime { get; set; }

        [MaxLength(500, ErrorMessage = "A speciális igények legfeljebb 500 karakter lehet.")]
        public string? SpecialisIgenyek { get; set; }
    }

    public class UpdateBookingRequestDto
    {
        public int? TeremId { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string? Status { get; set; } // Aktiv | NoShow | Megszuntetve | Kesz

        [MaxLength(500, ErrorMessage = "A speciális igények legfeljebb 500 karakter lehet.")]
        public string? SpecialisIgenyek { get; set; }
    }

    public class BookingListDto
    {
        public List<BookingDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
