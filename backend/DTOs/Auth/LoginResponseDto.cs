namespace ProjektLabor.DTOs.Auth
{
    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public CurrentUserDto Felhasznalo { get; set; } = null!;
    }
}
