using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs.Tickets;
using ProjektLabor.Models;
using ProjektLabor.Services;

namespace ProjektLabor.Endpoints
{
    public static class TicketEndpoints
    {
        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Nyitva",
            "Folyamatban",
            "Megoldva",
            "Zarva"
        };

        public static RouteGroupBuilder MapTicketEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/tickets")
                .WithTags("Tickets")
                .RequireAuthorization("Authenticated");

            // T1 – GET /api/tickets
            group.MapGet("/", async (
                int? teremId,
                string? statusz,
                string? kategoria,
                int page,
                int pageSize,
                AppDbContext db) =>
            {
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);

                var query = db.Hibajegyek
                    .AsNoTracking()
                    .Include(h => h.Terem)
                    .Include(h => h.Felhasznalo)
                    .AsQueryable();

                if (teremId.HasValue)
                {
                    query = query.Where(h => h.TeremId == teremId.Value);
                }

                if (!string.IsNullOrWhiteSpace(statusz))
                {
                    query = query.Where(h => h.Statusz == statusz);
                }

                if (!string.IsNullOrWhiteSpace(kategoria))
                {
                    query = query.Where(h => h.Kategoria == kategoria);
                }

                int totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(h => h.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(h => new TicketDto
                    {
                        Id = h.Id,
                        FelhasznaloId = h.FelhasznaloId,
                        FelhasznaloNev = h.Felhasznalo != null ? h.Felhasznalo.Nev : string.Empty,
                        TeremId = h.TeremId,
                        TeremNev = h.Terem != null ? h.Terem.Nev : string.Empty,
                        Kategoria = h.Kategoria,
                        Leiras = h.Leiras,
                        Statusz = h.Statusz,
                        CreatedAt = h.CreatedAt,
                        UpdatedAt = h.UpdatedAt
                    })
                    .ToListAsync();

                return Results.Ok(new TicketListDto
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                });
            })
            .WithName("GetTickets")
            .WithSummary("Hibajegyek listázása szűrőkkel és lapozással")
            .Produces<TicketListDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // T2 – GET /api/tickets/{id}
            group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            {
                var h = await db.Hibajegyek
                    .AsNoTracking()
                    .Include(h => h.Terem)
                    .Include(h => h.Felhasznalo)
                    .FirstOrDefaultAsync(h => h.Id == id);

                if (h is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú hibajegy nem található." });
                }

                var dto = new TicketDto
                {
                    Id = h.Id,
                    FelhasznaloId = h.FelhasznaloId,
                    FelhasznaloNev = h.Felhasznalo != null ? h.Felhasznalo.Nev : string.Empty,
                    TeremId = h.TeremId,
                    TeremNev = h.Terem != null ? h.Terem.Nev : string.Empty,
                    Kategoria = h.Kategoria,
                    Leiras = h.Leiras,
                    Statusz = h.Statusz,
                    CreatedAt = h.CreatedAt,
                    UpdatedAt = h.UpdatedAt
                };

                return Results.Ok(dto);
            })
            .WithName("GetTicketById")
            .WithSummary("Egy hibajegy adatainak lekérdezése")
            .Produces<TicketDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // T3 – POST /api/tickets
            group.MapPost("/", async (
                CreateTicketRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                if (string.IsNullOrWhiteSpace(request.Kategoria) || string.IsNullOrWhiteSpace(request.Leiras))
                {
                    return Results.BadRequest(new { message = "A kategória és a leírás megadása kötelező." });
                }

                var room = await db.Termek.FindAsync(request.TeremId);
                if (room is null)
                {
                    return Results.NotFound(new { message = $"A(z) {request.TeremId} azonosítójú terem nem található." });
                }

                int userId = 1;
                var subClaim = caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value;
                if (int.TryParse(subClaim, out int parsedId))
                {
                    userId = parsedId;
                }

                var ticket = new Hibajegy
                {
                    FelhasznaloId = userId,
                    TeremId = request.TeremId,
                    Kategoria = request.Kategoria.Trim(),
                    Leiras = request.Leiras.Trim(),
                    Statusz = "Nyitva",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.Hibajegyek.Add(ticket);
                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "CREATE",
                    entitasTipus: "Hibajegy",
                    entitasId: ticket.Id.ToString(),
                    ujErtek: $"TeremId: {ticket.TeremId}, Kategoria: {ticket.Kategoria}");

                var user = await db.Felhasznalok.FindAsync(userId);

                var result = new TicketDto
                {
                    Id = ticket.Id,
                    FelhasznaloId = ticket.FelhasznaloId,
                    FelhasznaloNev = user?.Nev ?? string.Empty,
                    TeremId = ticket.TeremId,
                    TeremNev = room.Nev,
                    Kategoria = ticket.Kategoria,
                    Leiras = ticket.Leiras,
                    Statusz = ticket.Statusz,
                    CreatedAt = ticket.CreatedAt,
                    UpdatedAt = ticket.UpdatedAt
                };

                return Results.Created($"/api/tickets/{ticket.Id}", result);
            })
            .WithName("CreateTicket")
            .WithSummary("Új hibajegy rögzítése")
            .Produces<TicketDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi();

            // T4 – PUT /api/tickets/{id}/status
            group.MapPut("/{id:int}/status", async (
                int id,
                UpdateTicketStatusRequestDto request,
                AppDbContext db,
                AuditService auditService,
                ClaimsPrincipal caller) =>
            {
                var ticket = await db.Hibajegyek
                    .Include(h => h.Terem)
                    .Include(h => h.Felhasznalo)
                    .FirstOrDefaultAsync(h => h.Id == id);

                if (ticket is null)
                {
                    return Results.NotFound(new { message = $"A(z) {id} azonosítójú hibajegy nem található." });
                }

                var canonicalStatus = AllowedStatuses.FirstOrDefault(s => s.Equals(request.Statusz, StringComparison.OrdinalIgnoreCase));
                if (canonicalStatus == null)
                {
                    return Results.BadRequest(new { message = $"Érvénytelen hibajegy státusz: '{request.Statusz}'. Megengedett: Nyitva, Folyamatban, Megoldva, Zarva." });
                }

                var oldStatus = ticket.Statusz;
                ticket.Statusz = canonicalStatus;
                ticket.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();

                await auditService.LogAsync(
                    caller,
                    muveletTipus: "UPDATE_STATUS",
                    entitasTipus: "Hibajegy",
                    entitasId: id.ToString(),
                    regiErtek: oldStatus,
                    ujErtek: canonicalStatus);

                var result = new TicketDto
                {
                    Id = ticket.Id,
                    FelhasznaloId = ticket.FelhasznaloId,
                    FelhasznaloNev = ticket.Felhasznalo?.Nev ?? string.Empty,
                    TeremId = ticket.TeremId,
                    TeremNev = ticket.Terem?.Nev ?? string.Empty,
                    Kategoria = ticket.Kategoria,
                    Leiras = ticket.Leiras,
                    Statusz = ticket.Statusz,
                    CreatedAt = ticket.CreatedAt,
                    UpdatedAt = ticket.UpdatedAt
                };

                return Results.Ok(result);
            })
            .WithName("UpdateTicketStatus")
            .WithSummary("Hibajegy státuszának módosítása")
            .RequireAuthorization("AdminOrIgazgato")
            .Produces<TicketDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi();

            return group;
        }
    }
}
