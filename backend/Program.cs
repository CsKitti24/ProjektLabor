using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using ProjektLabor.Data;
using ProjektLabor.DTOs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ProjektLabor API",
        Version = "v1",
        Description = "ProjektLabor terem- és kulcskezelő rendszer REST API dokumentációja."
    });
});

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
    // Normalization middleware: Swashbuckle 6.6.2 bundles a Swagger UI version whose validator
    // regex recognizes OpenAPI 3.0.0 through 3.0.3 (or 2.0 / 3.1.0). The Microsoft.OpenApi 1.6.30 dependency
    // serializes OpenApi3_0 as "3.0.4", causing Swagger UI to report:
    // "Unable to render this definition. The provided definition does not specify a valid version field."
    // Normalizing "3.0.4" to "3.0.1" allows Swagger UI to seamlessly parse and render the document.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.Value?.EndsWith("/swagger.json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var originalBody = context.Response.Body;
            using var memStream = new MemoryStream();
            context.Response.Body = memStream;

            await next();

            if (context.Response.StatusCode == StatusCodes.Status200OK)
            {
                memStream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(memStream, System.Text.Encoding.UTF8);
                var content = await reader.ReadToEndAsync();

                if (content.Contains("\"openapi\": \"3.0.4\""))
                {
                    content = content.Replace("\"openapi\": \"3.0.4\"", "\"openapi\": \"3.0.1\"");
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                context.Response.Body = originalBody;
                context.Response.ContentLength = bytes.Length;
                await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
                return;
            }

            memStream.Seek(0, SeekOrigin.Begin);
            await memStream.CopyToAsync(originalBody);
            context.Response.Body = originalBody;
            return;
        }

        await next();
    });

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ProjektLabor API v1");
        options.RoutePrefix = "swagger";
    });
}

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
            dbContext.Database.Migrate();
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
            }
        }
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
.WithDescription("Ellenőrzi a backend alkalmazás és az adatbázis-kapcsolat elérhetőségét.")
.WithTags("Health")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status503ServiceUnavailable)
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
.WithDescription("Lekérdezi a rendszerben rögzített összes terem listáját.")
.WithTags("Rooms")
.Produces<List<RoomDto>>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status500InternalServerError)
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
.WithDescription("Egy adott terem részletes adatait adja vissza az azonosítója (ID) alapján.")
.WithTags("Rooms")
.Produces<RoomDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.WithOpenApi();

app.Run();

public partial class Program { }
