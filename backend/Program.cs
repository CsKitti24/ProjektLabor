using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.DTOs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register DbContext with connection string from configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
    startupLogger.LogWarning("Warning: 'DefaultConnection' connection string is not configured.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseSqlServer(connectionString);
    }
});

// Configure CORS for frontend access
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors("AllowFrontend");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Automatic migrations on startup if configured (e.g. Docker environment)
if (app.Configuration.GetValue<bool>("ApplyMigrations") ||
    string.Equals(Environment.GetEnvironmentVariable("APPLY_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Checking and applying pending database migrations...");
        dbContext.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations on startup.");
    }
}

// -----------------------------------------------------------------------------------------
// 5.1 Health endpoint
// -----------------------------------------------------------------------------------------
app.MapGet("/api/health", async (AppDbContext dbContext, ILogger<Program> logger) =>
{
    try
    {
        bool canConnect = await dbContext.Database.CanConnectAsync();
        if (canConnect)
        {
            return Results.Ok(new
            {
                status = "ok",
                database = "connected"
            });
        }

        logger.LogWarning("Health check failed: database connection could not be established.");
        return Results.Json(new
        {
            status = "unhealthy",
            database = "disconnected"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check failed with an exception.");
        return Results.Json(new
        {
            status = "unhealthy",
            database = "disconnected"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("GetHealth")
.WithSummary("Egészségellenőrző végpont (alkalmazás és adatbázis kapcsolat)")
.WithOpenApi();

// -----------------------------------------------------------------------------------------
// 5.2 Adatlekérdező végpontok (Rooms / Termek)
// -----------------------------------------------------------------------------------------
app.MapGet("/api/rooms", async (AppDbContext dbContext, ILogger<Program> logger) =>
{
    try
    {
        var rooms = await dbContext.Termek
            .AsNoTracking()
            .Select(t => new RoomDto
            {
                Id = t.Id,
                Nev = t.Nev,
                Kapacitas = t.Kapacitas,
                Leiras = t.Leiras,
                Aktivalva = t.Aktivalva
            })
            .ToListAsync();

        return Results.Ok(rooms);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error occurred while retrieving rooms from database.");
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            detail: "Hiba történt a termek lekérdezése során."
        );
    }
})
.WithName("GetRooms")
.WithSummary("Termek listájának lekérdezése")
.WithOpenApi();

app.MapGet("/api/rooms/{id:int}", async (int id, AppDbContext dbContext, ILogger<Program> logger) =>
{
    try
    {
        var room = await dbContext.Termek
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new RoomDto
            {
                Id = t.Id,
                Nev = t.Nev,
                Kapacitas = t.Kapacitas,
                Leiras = t.Leiras,
                Aktivalva = t.Aktivalva
            })
            .FirstOrDefaultAsync();

        if (room is null)
        {
            return Results.NotFound(new { message = $"A(z) {id} azonosítójú terem nem található." });
        }

        return Results.Ok(room);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error occurred while retrieving room {RoomId} from database.", id);
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            detail: "Hiba történt a terem lekérdezése során."
        );
    }
})
.WithName("GetRoomById")
.WithSummary("Egy adott terem adatainak lekérdezése azonosító alapján")
.WithOpenApi();

app.Run();

public partial class Program { }
