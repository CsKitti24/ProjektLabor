using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Models;

namespace ProjektLabor.Services
{
    public class AuthService
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;

        public AuthService(AppDbContext db, IConfiguration config, ILogger<AuthService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
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

            if (user is null)
            {
                _logger.LogWarning("Bejelentkezési kísérlet nem létező felhasználóval: {Username}", request.Username);
                return null;
            }

            bool passwordValid = BCrypt.Net.BCrypt.Verify(request.Jelszo, user.JelszoHash);
            if (!passwordValid)
            {
                _logger.LogWarning("Sikertelen bejelentkezés hibás jelszóval: {Username}", request.Username);
                return null;
            }

            var token = GenerateJwtToken(user);
            var expiresAt = DateTime.UtcNow.AddHours(GetExpiresInHours());

            _logger.LogInformation("Sikeres bejelentkezés: {Username} (Role: {Role})", user.Username, user.Role);

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
                    Role = user.Role
                }
            };
        }

        /// <summary>
        /// JWT Bearer token generálása a felhasználó adatai alapján.
        /// Claims: sub (userId), unique_name (username), email, role.
        /// </summary>
        public string GenerateJwtToken(Felhasznalo user)
        {
            var key = GetSigningKey();
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
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
                .Where(u => u.Id == userId)
                .Select(u => new CurrentUserDto
                {
                    Id = u.Id,
                    Nev = u.Nev,
                    Email = u.Email,
                    Username = u.Username,
                    Role = u.Role
                })
                .FirstOrDefaultAsync();

            return user;
        }

        public SymmetricSecurityKey GetSigningKey()
        {
            var keyString = _config["Jwt:Key"]
                ?? throw new InvalidOperationException("JWT kulcs nincs konfigurálva (Jwt:Key).");

            if (keyString.Length < 32)
                throw new InvalidOperationException("A JWT kulcsnak legalább 32 karakter hosszúnak kell lennie.");

            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
        }

        private double GetExpiresInHours()
        {
            return _config.GetValue<double>("Jwt:ExpiresInHours", 8);
        }
    }
}
