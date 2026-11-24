using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Security;
using ProjektLabor.Models;

namespace ProjektLabor.Services
{
    public class AuthService
    {
        private readonly AppDbContext _db;
        private readonly JwtSettings _jwtSettings;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;
        private readonly AuditService _auditService;

        // Nem létező felhasználónál is lefuttatunk egy ugyanilyen költségű BCrypt-ellenőrzést, hogy a
        // válaszidőből ne derüljön ki, létezik-e a felhasználónév.
        private static readonly Lazy<string> DummyPasswordHash = new(
            () => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), workFactor: 12));

        public AuthService(
            AppDbContext db,
            IConfiguration config,
            JwtSettings jwtSettings,
            ILogger<AuthService> logger,
            AuditService auditService)
        {
            _db = db;
            _config = config;
            _jwtSettings = jwtSettings;
            _logger = logger;
            _auditService = auditService;
        }

        /// <summary>
        /// Felhasználó hitelesítése username + jelszó alapján.
        /// Visszatér null-lal, ha a hitelesítés sikertelen.
        /// </summary>
        public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
        {
            var user = await _db.Felhasznalok
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user is null || !user.Aktiv)
            {
                BCrypt.Net.BCrypt.Verify(request.Jelszo, DummyPasswordHash.Value);
                _logger.LogWarning("Bejelentkezési kísérlet nem létező vagy inaktív felhasználóval: {Username}", request.Username);
                await _auditService.LogForUserAsync(
                    null,
                    "LOGIN_FAILED",
                    "Authentication",
                    entitasId: request.Username);
                return null;
            }

            bool passwordValid = BCrypt.Net.BCrypt.Verify(request.Jelszo, user.JelszoHash);
            if (!passwordValid)
            {
                _logger.LogWarning("Sikertelen bejelentkezés hibás jelszóval: {Username}", request.Username);
                await _auditService.LogForUserAsync(
                    null,
                    "LOGIN_FAILED",
                    "Authentication",
                    entitasId: request.Username);
                return null;
            }

            // A mintafiókok jelszava nyilvános: ha nincsenek kifejezetten engedélyezve, az eredeti jelszóval
            // akkor sem lehet belépni, ha valaki időközben újraaktiválta a fiókot.
            if (!_config.GetValue("SampleAccounts:Enabled", false) &&
                AdminBootstrapper.KnownSampleAccountPasswordHashes.Contains(user.JelszoHash))
            {
                _logger.LogWarning("Bejelentkezés elutasítva: {Username} még a nyilvános mintajelszót használja.", request.Username);
                await _auditService.LogForUserAsync(
                    null,
                    "LOGIN_FAILED",
                    "Authentication",
                    entitasId: request.Username,
                    indoklas: "A fiók még a nyilvános mintajelszót használja.");
                return null;
            }

            var token = GenerateJwtToken(user);
            var expiresAt = DateTime.UtcNow.AddHours(GetExpiresInHours());

            _logger.LogInformation("Sikeres bejelentkezés: {Username} (Role: {Role})", user.Username, user.Role);
            await _auditService.LogForUserAsync(
                user.Id,
                "LOGIN_SUCCESS",
                "Authentication",
                entitasId: user.Id.ToString());

            return new LoginResponseDto
            {
                Token = token,
                ExpiresAt = expiresAt,
                Felhasznalo = new CurrentUserDto
                {
                    Id = user.Id,
                    Nev = user.Nev,
                    Email = user.Email,
                    Username = user.Username,
                    Role = user.Role,
                    HasPin = user.PinHash != null
                }
            };
        }

        /// <summary>
        /// JWT Bearer token generálása a felhasználó adatai alapján.
        /// Claims: sub (userId), unique_name (username), email, role.
        /// </summary>
        public string GenerateJwtToken(Felhasznalo user)
        {
            var credentials = new SigningCredentials(
                _jwtSettings.GetSymmetricSecurityKey(),
                SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("role", user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                // A kiadás ideje: jelszócsere után az ennél korábban kiadott tokeneket elutasítjuk.
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
                // Jelszóverzió: az utolsó jelszócsere időbélyege. Csere után a régi értékű tokeneket elutasítjuk.
                new Claim(PasswordVersionClaim, GetPasswordVersion(user))
            };

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(GetExpiresInHours()),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// A bejelentkezett felhasználó adatait adja vissza a JWT token sub claim-je alapján.
        /// </summary>
        public async Task<CurrentUserDto?> GetCurrentUserAsync(ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)
                           ?? principal.FindFirst(ClaimTypes.NameIdentifier);

            if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out int userId))
                return null;

            var user = await _db.Felhasznalok
                .AsNoTracking()
                .Where(u => u.Id == userId && u.Aktiv)
                .Select(u => new CurrentUserDto
                {
                    Id = u.Id,
                    Nev = u.Nev,
                    Email = u.Email,
                    Username = u.Username,
                    Role = u.Role,
                    HasPin = u.PinHash != null
                })
                .FirstOrDefaultAsync();

            return user;
        }

        /// <summary>
        /// A bejelentkezett felhasználó saját jelszavának cseréje a jelenlegi jelszó ellenőrzésével.
        /// Visszatér: (siker, hibaüzenet, új token); a hibaüzenet null, ha a csere sikerült. A régi tokenek
        /// a cserével érvényüket vesztik, ezért a hívó az új tokennel folytathatja a munkát.
        /// </summary>
        public async Task<(bool Success, string? Error, string? NewToken)> ChangePasswordAsync(
            ClaimsPrincipal principal,
            ChangePasswordRequestDto request)
        {
            var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)
                           ?? principal.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return (false, null, null);
            }

            var user = await _db.Felhasznalok.FirstOrDefaultAsync(u => u.Id == userId && u.Aktiv);
            if (user is null)
            {
                return (false, null, null);
            }

            if (!BCrypt.Net.BCrypt.Verify(request.JelenlegiJelszo, user.JelszoHash))
            {
                await _auditService.LogForUserAsync(
                    user.Id,
                    "PASSWORD_CHANGE_FAILED",
                    "Felhasznalo",
                    entitasId: user.Id.ToString());
                return (false, "A jelenlegi jelszó hibás.", null);
            }

            var policyError = PasswordPolicy.Validate(request.UjJelszo);
            if (policyError is not null)
            {
                return (false, policyError, null);
            }

            if (string.Equals(request.JelenlegiJelszo, request.UjJelszo, StringComparison.Ordinal))
            {
                return (false, "Az új jelszó nem egyezhet meg a jelenlegivel.", null);
            }

            user.JelszoHash = BCrypt.Net.BCrypt.HashPassword(request.UjJelszo, workFactor: 12);
            user.UpdatedAt = DateTime.UtcNow;
            // A korábban kiadott (pl. egy másik gépen nyitva hagyott) bejelentkezések érvényüket vesztik.
            user.JelszoValtozas = user.UpdatedAt;
            await _db.SaveChangesAsync();

            await _auditService.LogForUserAsync(
                user.Id,
                "PASSWORD_CHANGE",
                "Felhasznalo",
                entitasId: user.Id.ToString());
            return (true, null, GenerateJwtToken(user));
        }

        /// <summary>
        /// Az oktató saját PIN-kódjának beállítása a jelenlegi jelszó ellenőrzésével.
        /// Visszatér: (siker, hibaüzenet); a hibaüzenet null, ha a felhasználó nem található.
        /// </summary>
        public async Task<(bool Success, string? Error)> SetOwnPinAsync(
            ClaimsPrincipal principal,
            SetOwnPinRequestDto request)
        {
            var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)
                           ?? principal.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim is null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return (false, null);
            }

            var user = await _db.Felhasznalok.FirstOrDefaultAsync(u => u.Id == userId && u.Aktiv);
            if (user is null)
            {
                return (false, null);
            }

            if (!BCrypt.Net.BCrypt.Verify(request.JelenlegiJelszo, user.JelszoHash))
            {
                await _auditService.LogForUserAsync(
                    user.Id,
                    "PIN_CHANGE_FAILED",
                    "Felhasznalo",
                    entitasId: user.Id.ToString());
                return (false, "A jelenlegi jelszó hibás.");
            }

            user.PinHash = BCrypt.Net.BCrypt.HashPassword(request.Pin);
            // Új PIN beállításakor a korábbi hibás próbálkozások és a zárolás megszűnik.
            user.PinHibasProbalkozasok = 0;
            user.PinZarolvaEddig = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _auditService.LogForUserAsync(
                user.Id,
                "PIN_CHANGE",
                "Felhasznalo",
                entitasId: user.Id.ToString(),
                ujErtek: "Saját PIN beállítva");
            return (true, null);
        }

        public const string PasswordVersionClaim = "pwv";

        public static string GetPasswordVersion(Felhasznalo user) =>
            GetPasswordVersion(user.JelszoValtozas);

        public static string GetPasswordVersion(DateTime? passwordChangedAt) =>
            (passwordChangedAt?.Ticks ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);

        public DateTime GetTokenExpiry() => DateTime.UtcNow.AddHours(GetExpiresInHours());

        private double GetExpiresInHours()
        {
            return _config.GetValue<double>("Jwt:ExpiresInHours", 8);
        }
    }
}
