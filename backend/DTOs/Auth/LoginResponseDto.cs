using System.Text.Json.Serialization;

namespace ProjektLabor.DTOs.Auth
{
    public class LoginResponseDto
    {
        // A token csak a HttpOnly sütiben utazik; a JSON-válaszba nem kerül, így JavaScriptből
        // (pl. egy XSS-hibán keresztül) sem olvasható ki.
        [JsonIgnore]
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public CurrentUserDto Felhasznalo { get; set; } = null!;
    }
}
