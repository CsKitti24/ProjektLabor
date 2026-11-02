using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ProjektLabor.Data;
using ProjektLabor.DTOs;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Endpoints;
using ProjektLabor.Services;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------------------
// JWT Authentication
// -----------------------------------------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT kulcs nincs konfigurálva (Jwt:Key).");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew                = TimeSpan.FromSeconds(30)
        };
    });

// -----------------------------------------------------------------------------------------
// RBAC Authorization policies
// -----------------------------------------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Adminisztrator",        p => p.RequireRole("Adminisztrator"));
    options.AddPolicy("Portas",                p => p.RequireRole("Portas"));
    options.AddPolicy("Oktato",                p => p.RequireRole("Oktato"));
    options.AddPolicy("UzemeltetesiIgazgato",  p => p.RequireRole("UzemeltetesiIgazgato"));
    options.AddPolicy("AdminOrIgazgato",       p => p.RequireRole("Adminisztrator", "UzemeltetesiIgazgato"));
    options.AddPolicy("AdminOrPortas",         p => p.RequireRole("Adminisztrator", "Portas"));
    // Bármely bejelentkezett felhasználó
    options.AddPolicy("Authenticated",         p => p.RequireAuthenticatedUser());
});

// -----------------------------------------------------------------------------------------
// Application Services
// -----------------------------------------------------------------------------------------
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditService>();

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

    // JWT Bearer auth Swagger UI-ban
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Írd be a JWT tokent. Példa: eyJhbGci..."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
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

// Authentication & Authorization middleware – sorrendük kritikus!
app.UseAuthentication();
app.UseAuthorization();

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
// Auth endpoints (A1, A2, A3)
// -----------------------------------------------------------------------------------------

// A1 – POST /api/auth/login
app.MapPost("/api/auth/login", async (LoginRequestDto request, AuthService authService, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Jelszo))
    {
        return Results.BadRequest(new { message = "A felhasználónév és a jelszó megadása kötelező." });
    }

    var result = await authService.LoginAsync(request);

    if (result is null)
    {
        return Results.Json(
            new { message = "Hibás felhasználónév vagy jelszó." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(result);
})
.WithName("Login")
.WithSummary("Felhasználói bejelentkezés – JWT token kiadás")
.WithDescription("Hitelesíti a felhasználót username és jelszó alapján. Sikeres hitelesítés esetén JWT Bearer tokent ad vissza.")
.WithTags("Auth")
.Produces<LoginResponseDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.WithOpenApi();

// A2 – POST /api/auth/logout
app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    // JWT stateless: a szerver oldalon nincs mit invalidálni.
    // A kliens oldali token törlése a frontend feladata.
    // Jövőbeli fejlesztés: token blacklist implementálható itt.
    var username = context.User.Identity?.Name ?? "ismeretlen";
    return Results.Ok(new { message = "Sikeres kijelentkezés." });
})
.WithName("Logout")
.WithSummary("Kijelentkezés")
.WithDescription("JWT stateless kijelentkezés. A kliens feladata a token törlése. Szerver oldali token invalidálás nem implementált.")
.WithTags("Auth")
.RequireAuthorization("Authenticated")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.WithOpenApi();

// A3 – GET /api/auth/me
app.MapGet("/api/auth/me", async (HttpContext context, AuthService authService) =>
{
    var user = await authService.GetCurrentUserAsync(context.User);

    if (user is null)
    {
        return Results.Json(
            new { message = "A felhasználó nem található." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(user);
})
.WithName("GetCurrentUser")
.WithSummary("Bejelentkezett felhasználó adatai")
.WithDescription("A JWT tokenből azonosítja a bejelentkezett felhasználót és visszaadja az adatait.")
.WithTags("Auth")
.RequireAuthorization("Authenticated")
.Produces<CurrentUserDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.WithOpenApi();

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
// Module Endpoint Groups
// -----------------------------------------------------------------------------------------
app.MapUserEndpoints();
app.MapRoomEndpoints();
app.MapKeyEndpoints();
app.MapKeyMovementEndpoints();
app.MapBookingEndpoints();
app.MapTicketEndpoints();

app.Run();

public partial class Program { }
