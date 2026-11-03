using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ProjektLabor.Data;
using ProjektLabor.DTOs;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Endpoints;
using ProjektLabor.Security;
using ProjektLabor.Services;

var builder = WebApplication.CreateBuilder(args);

var jwtSettings = JwtSettings.FromConfiguration(builder.Configuration, builder.Environment);
var jwtSigningKey = jwtSettings.GetSymmetricSecurityKey();

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
            IssuerSigningKey = jwtSigningKey,
            RoleClaimType = "role",
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var subject = context.Principal?.FindFirst("sub")?.Value;
                var role = context.Principal?.FindFirst("role")?.Value;
                if (!int.TryParse(subject, out var userId) ||
                    userId <= 0 ||
                    !RoleNames.All.Contains(role, StringComparer.Ordinal))
                {
                    context.Fail("The token must contain a valid user ID and role.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", policy => policy.RequireRole(RoleNames.Adminisztrator));
    options.AddPolicy("Adminisztrator", policy => policy.RequireRole(RoleNames.Adminisztrator));
    options.AddPolicy("Portas", policy => policy.RequireRole(RoleNames.Portas));
    options.AddPolicy("Oktato", policy => policy.RequireRole(RoleNames.Oktato));
    options.AddPolicy("UzemeltetesiIgazgato", policy => policy.RequireRole(RoleNames.UzemeltetesiIgazgato));
    options.AddPolicy("AdminOrIgazgato", policy => policy.RequireRole(RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato));
    options.AddPolicy("AdminOrPortas", policy => policy.RequireRole(RoleNames.Adminisztrator, RoleNames.Portas));
    options.AddPolicy("AdminOrOktato", policy => policy.RequireRole(RoleNames.Adminisztrator, RoleNames.Oktato));
    options.AddPolicy("AdminOrPortasOrIgazgato", policy =>
        policy.RequireRole(RoleNames.Adminisztrator, RoleNames.Portas, RoleNames.UzemeltetesiIgazgato));
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddSingleton(jwtSettings);
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<NoShowProcessor>();
builder.Services.AddScoped<BookingExpiryNotificationProcessor>();
builder.Services.AddScoped<DailyKeyClosingProcessor>();
builder.Services.AddHostedService<NoShowBackgroundService>();
builder.Services.AddHostedService<BookingExpiryNotificationBackgroundService>();
builder.Services.AddHostedService<DailyKeyClosingBackgroundService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ProjektLabor API",
        Version = "v1",
        Description = "ProjektLabor terem- és kulcskezelő rendszer REST API dokumentációja."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Írd be a JWT tokent."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

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
    var startupLogger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("Startup");
    startupLogger.LogWarning("The 'DefaultConnection' connection string is not configured.");
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

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.Value?.EndsWith("/swagger.json", StringComparison.OrdinalIgnoreCase) != true)
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;
        await next();

        if (context.Response.StatusCode == StatusCodes.Status200OK)
        {
            memoryStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(memoryStream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync();
            content = content.Replace("\"openapi\": \"3.0.4\"", "\"openapi\": \"3.0.1\"");
            var bytes = Encoding.UTF8.GetBytes(content);
            context.Response.Body = originalBody;
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes);
            return;
        }

        memoryStream.Seek(0, SeekOrigin.Begin);
        await memoryStream.CopyToAsync(originalBody);
        context.Response.Body = originalBody;
    });

    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ProjektLabor API v1");
        options.RoutePrefix = "swagger";
    });
}

if (app.Configuration.GetValue<bool>("ApplyMigrations") ||
    string.Equals(Environment.GetEnvironmentVariable("APPLY_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    const int maxRetries = 10;
    const int delaySeconds = 3;

    for (var attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            logger.LogInformation(
                "Checking and applying pending database migrations (Attempt {Attempt}/{MaxRetries})...",
                attempt,
                maxRetries);
            dbContext.Database.Migrate();
            logger.LogInformation("Database migrations applied successfully.");
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to apply database migrations on attempt {Attempt}/{MaxRetries}. Waiting {Delay}s before retry...",
                attempt,
                maxRetries,
                delaySeconds);
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

    await DemoUserBootstrapper.InitializeAsync(dbContext, app.Configuration, logger);
    await AdminBootstrapper.InitializeAsync(dbContext, app.Configuration, logger);
}

app.MapPost("/api/auth/login", async (
    LoginRequestDto request,
    AuthService authService,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Jelszo))
    {
        return Results.BadRequest(new { message = "A felhasználónév és a jelszó megadása kötelező." });
    }

    var result = await authService.LoginAsync(request);
    if (result is null)
    {
        logger.LogInformation("Login rejected for username {Username}.", request.Username);
        return Results.Json(
            new { message = "Hibás felhasználónév vagy jelszó." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(result);
})
.WithName("Login")
.WithSummary("Felhasználói bejelentkezés – JWT token kiadás")
.WithTags("Auth")
.Produces<LoginResponseDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.WithOpenApi();

app.MapPost("/api/auth/logout", () => Results.Ok(new { message = "Sikeres kijelentkezés." }))
    .WithName("Logout")
    .WithSummary("Kijelentkezés; a kliens törli a JWT tokent.")
    .WithTags("Auth")
    .RequireAuthorization("Authenticated")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .WithOpenApi();

app.MapGet("/api/auth/me", async (HttpContext context, AuthService authService) =>
{
    var user = await authService.GetCurrentUserAsync(context.User);
    return user is null
        ? Results.Json(new { message = "A felhasználó nem található." }, statusCode: StatusCodes.Status401Unauthorized)
        : Results.Ok(user);
})
.WithName("GetCurrentUser")
.WithSummary("A bejelentkezett felhasználó adatai")
.WithTags("Auth")
.RequireAuthorization("Authenticated")
.Produces<CurrentUserDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.WithOpenApi();

app.MapGet("/api/health", async (AppDbContext dbContext, ILogger<Program> logger) =>
{
    try
    {
        if (await dbContext.Database.CanConnectAsync())
        {
            return Results.Ok(new { status = "ok", database = "connected" });
        }

        logger.LogWarning("Health check failed: database connection could not be established.");
        return Results.Json(
            new { status = "unhealthy", database = "disconnected" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check failed with an exception.");
        return Results.Json(
            new { status = "unhealthy", database = "disconnected" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("GetHealth")
.WithSummary("Egészségellenőrzés az adatbázis-kapcsolattal együtt")
.WithTags("Health")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status503ServiceUnavailable)
.WithOpenApi();

app.MapUserEndpoints();
app.MapRoomEndpoints();
app.MapKeyEndpoints();
app.MapKeyMovementEndpoints();
app.MapBookingEndpoints();
app.MapTicketEndpoints();
app.MapMaintenanceEndpoints();
app.MapMasterAccessEndpoints();
app.MapReliabilityEndpoints();
app.MapNotificationEndpoints();
app.MapSystemAdministrationEndpoints();
app.MapReportEndpoints();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]))
        .ToArray();

    return forecast;
})
.WithName("GetWeatherForecast")
.RequireAuthorization("Authenticated")
.WithOpenApi();

app.Run();

public partial class Program { }

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
