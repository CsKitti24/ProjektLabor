namespace ProjektLabor.DTOs
{
    public class RoomDto
    {
        public int Id { get; set; }
        public string Nev { get; set; } = string.Empty;
        public int Kapacitas { get; set; }
        public string? Leiras { get; set; }
        public bool Aktivalva { get; set; }
    }
}
