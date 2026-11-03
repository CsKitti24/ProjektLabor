using System.Security.Claims;
using ProjektLabor.Data;
using ProjektLabor.Models;

namespace ProjektLabor.Services
{
    public class AuditService
    {
        private readonly AppDbContext _db;
        public AuditService(AppDbContext db, ILogger<AuditService> logger)
        {
            _db = db;
        }

        public async Task LogAsync(
            ClaimsPrincipal? user,
            string muveletTipus,
            string entitasTipus,
            string? entitasId = null,
            string? regiErtek = null,
            string? ujErtek = null,
            string? indoklas = null)
        {
            int? userId = null;
                var subClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? user?.FindFirst("sub")?.Value;

                if (int.TryParse(subClaim, out int parsedId))
                {
                    userId = parsedId;
                }

            var log = new AuditLog
            {
                    FelhasznaloId = userId,
                    MuveletTipus = muveletTipus,
                    EntitasTipus = entitasTipus,
                    EntitasId = entitasId,
                    RegiErtek = regiErtek,
                    UjErtek = ujErtek,
                    Indoklas = indoklas,
                    LetrehozasIdeje = DateTime.UtcNow
                };

            _db.AuditLogok.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}
