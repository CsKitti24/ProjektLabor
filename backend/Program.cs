using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using ProjektLabor.Data;
using ProjektLabor.Endpoints;
using ProjektLabor.DTOs;
using ProjektLabor.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSettings = JwtSettings.FromConfiguration(builder.Configuration, builder.Environment);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            RoleClaimType = "role",
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization(RolePolicies.AddAuthorizationPolicies);

// Register DbContext with connection string from configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var databasePassword = builder.Configuration["Database:Password"];
if (!string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(databasePassword))
{
    var sqlConnectionString = new SqlConnectionStringBuilder(connectionString)
    {
        Password = databasePassword
    };
    connectionString = sqlConnectionString.ConnectionString;
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
    startupLogger.LogWarning("Warning: 'DefaultConnection' connection string is not configured.");
}
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
        });
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

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapMaintenanceEndpoints();
app.MapMasterAccessEndpoints();
app.MapReliabilityEndpoints();

// Automatic migrations on startup if configured (e.g. Docker environment)
if (app.Configuration.GetValue<bool>("ApplyMigrations") ||
    string.Equals(Environment.GetEnvironmentVariable("APPLY_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    const int maxRetries = 10;
    const int delaySeconds = 3;

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            logger.LogInformation("Checking and applying pending database migrations (Attempt {Attempt}/{MaxRetries})...", attempt, maxRetries);
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply database migrations on attempt {Attempt}/{MaxRetries}. Waiting {Delay}s before retry...", attempt, maxRetries, delaySeconds);
            if (attempt < maxRetries)
            {
                Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
            }
            else
            {
                logger.LogError(ex, "Database migrations could not be applied after {MaxRetries} attempts.", maxRetries);
                throw new InvalidOperationException(
                    "Database migrations could not be applied; the backend cannot start securely.",
                    ex);
            }
        }
    }

    await AdminBootstrapper.InitializeAsync(
        dbContext,
        app.Configuration,
        logger);
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
