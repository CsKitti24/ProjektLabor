using System.ComponentModel.DataAnnotations;

namespace ProjektLabor.DTOs.Rooms
{
    public class CreateRoomRequestDto
    {
        [Required(ErrorMessage = "A terem nevének megadása kötelező.")]
        [MaxLength(100, ErrorMessage = "A terem neve legfeljebb 100 karakter lehet.")]
        public string Nev { get; set; } = string.Empty;

        [Required(ErrorMessage = "A kapacitás megadása kötelező.")]
        [Range(1, 10000, ErrorMessage = "A terem kapacitásának legalább 1-nek kell lennie.")]
        public int Kapacitas { get; set; }

        [MaxLength(500, ErrorMessage = "A leírás legfeljebb 500 karakter lehet.")]
        public string? Leiras { get; set; }

        public bool Aktivalva { get; set; } = true;
    }

    public class UpdateRoomRequestDto
    {
        [MaxLength(100, ErrorMessage = "A terem neve legfeljebb 100 karakter lehet.")]
        public string? Nev { get; set; }

        [Range(1, 10000, ErrorMessage = "A terem kapacitásának legalább 1-nek kell lennie.")]
        public int? Kapacitas { get; set; }

        [MaxLength(500, ErrorMessage = "A leírás legfeljebb 500 karakter lehet.")]
        public string? Leiras { get; set; }

        public bool? Aktivalva { get; set; }
    }

    public class RoomAvailabilityDto
    {
        public int TeremId { get; set; }
        public DateTime Datum { get; set; }
        public List<TimeSlotDto> Szabad { get; set; } = new();
        public List<BookingSlotDto> Foglalt { get; set; } = new();
        public List<MaintenanceSlotDto> Karbantartas { get; set; } = new();
    }

    public class TimeSlotDto
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    public class BookingSlotDto
    {
        public int FoglalasId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Statusz { get; set; } = string.Empty;
        public string? FelhasznaloNev { get; set; }
    }

    public class MaintenanceSlotDto
    {
        public int Id { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? Leiras { get; set; }
    }

    public class RoomKeyDto
    {
        public int Id { get; set; }
        public string Szam { get; set; } = string.Empty;
        public string Tipus { get; set; } = string.Empty;
        public bool Aktivalva { get; set; }
        public bool IsPrimaryRoomKey { get; set; }
    }
}
