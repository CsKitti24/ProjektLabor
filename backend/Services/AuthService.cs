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

            if (user is null)
            {
                _logger.LogWarning("Bejelentkezési kísérlet nem létező felhasználóval: {Username}", request.Username);
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
            var credentials = new SigningCredentials(
                _jwtSettings.GetSymmetricSecurityKey(),
                SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("role", user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
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

        private double GetExpiresInHours()
        {
            return _config.GetValue<double>("Jwt:ExpiresInHours", 8);
        }
    }
}
