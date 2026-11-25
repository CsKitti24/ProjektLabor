using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ProjektLabor.Data;
using ProjektLabor.DTOs;
using ProjektLabor.DTOs.Auth;
using ProjektLabor.Endpoints;
using ProjektLabor.Security;
using ProjektLabor.Services;

var builder = WebApplication.CreateBuilder(args);

var configuredTimeZoneId = builder.Configuration["Automation:TimeZoneId"];
if (!AppTime.Configure(configuredTimeZoneId))
{
    throw new InvalidOperationException(
        $"Az Automation:TimeZoneId beállításban megadott időzóna ('{configuredTimeZoneId}') nem található.");
}

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
            // A böngésző a tokent HttpOnly sütiben küldi; ha nincs Authorization fejléc, onnan olvassuk.
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) &&
                    !context.Request.Headers.ContainsKey("Authorization") &&
                    context.Request.Cookies.TryGetValue(AuthCookie.Name, out var cookieToken) &&
                    !string.IsNullOrWhiteSpace(cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirst("sub")?.Value;
                var role = context.Principal?.FindFirst("role")?.Value;
                if (!int.TryParse(subject, out var userId) ||
                    userId <= 0 ||
                    !RoleNames.All.Contains(role, StringComparer.Ordinal))
                {
                    context.Fail("The token must contain a valid user ID and role.");
                    return;
                }

                // A token csak addig érvényes, amíg a felhasználó létezik, aktív, és a szerepköre
                // megegyezik a tokenben lévővel. Így a törlés/inaktiválás és a szerepkör-változás
                // azonnal hat, nem csak a token lejártakor.
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.Felhasznalok
                    .AsNoTracking()
                    .Where(item => item.Id == userId)
                    .Select(item => new { item.Role, item.Aktiv, item.JelszoValtozas })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (user is null || !user.Aktiv || !string.Equals(user.Role, role, StringComparison.Ordinal))
                {
                    context.Fail("The user no longer exists, is inactive, or has a different role.");
                    return;
                }

                // Jelszócsere után a korábban kiadott tokenek érvénytelenek: a token jelszóverziója
                // (az utolsó csere időbélyege) meg kell egyezzen az aktuálissal.
                if (user.JelszoValtozas is not null &&
                    context.Principal?.FindFirst(AuthService.PasswordVersionClaim)?.Value !=
                    AuthService.GetPasswordVersion(user.JelszoValtozas))
                {
                    context.Fail("The token was issued before the last password change.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", policy => policy.RequireRole(RoleNames.Adminisztrator));
    options.AddPolicy("Adminisztrator", policy => policy.RequireRole(RoleNames.Adminisztrator));
    options.AddPolicy("Portas", policy => policy.RequireRole(RoleNames.Portas));
    options.AddPolicy("Oktato", policy => policy.RequireRole(RoleNames.Oktato));
    options.AddPolicy("OktatoOrPortas", policy =>
        policy.RequireRole(RoleNames.Oktato, RoleNames.Portas));
    options.AddPolicy("UzemeltetesiIgazgato", policy => policy.RequireRole(RoleNames.UzemeltetesiIgazgato));
    options.AddPolicy("AdminOrIgazgato", policy => policy.RequireRole(RoleNames.Adminisztrator, RoleNames.UzemeltetesiIgazgato));
    options.AddPolicy("AdminOrPortas", policy => policy.RequireRole(RoleNames.Adminisztrator, RoleNames.Portas));
    options.AddPolicy("AdminOrOktato", policy => policy.RequireRole(RoleNames.Adminisztrator, RoleNames.Oktato));
    options.AddPolicy("AdminOrPortasOrIgazgato", policy =>
        policy.RequireRole(RoleNames.Adminisztrator, RoleNames.Portas, RoleNames.UzemeltetesiIgazgato));
    options.AddPolicy("TicketReaders", policy =>
        policy.RequireRole(
            RoleNames.Adminisztrator,
            RoleNames.Portas,
            RoleNames.UzemeltetesiIgazgato,
            RoleNames.Oktato));
    options.AddPolicy("TicketCreators", policy =>
        policy.RequireRole(RoleNames.Adminisztrator, RoleNames.Portas, RoleNames.Oktato));
    options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
});
// A szerepkör-szabályok elutasításakor is magyar JSON-üzenet (üres 401/403 helyett).
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();


builder.Services.AddSingleton(jwtSettings);
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<NoShowProcessor>();
builder.Services.AddScoped<BookingExpiryNotificationProcessor>();
builder.Services.AddScoped<DailyKeyClosingProcessor>();
builder.Services.AddScoped<BookingCompletionProcessor>();
builder.Services.AddHostedService<NoShowBackgroundService>();
builder.Services.AddHostedService<BookingExpiryNotificationBackgroundService>();
builder.Services.AddHostedService<DailyKeyClosingBackgroundService>();
builder.Services.AddHostedService<BookingCompletionBackgroundService>();
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
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
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
        options.UseSqlServer(connectionString);
    }
});

// A frontend élesben ugyanarról a címről, proxyn keresztül éri el az API-t, így CORS-ra csak
// fejlesztéskor van szükség. Csak a konfigurációban felsorolt originök engedélyezettek.
// Megadható listaként (appsettings) vagy vesszővel elválasztott szövegként (környezeti változó).
var corsSection = builder.Configuration.GetSection("Cors:AllowedOrigins");
var allowedOrigins = (corsSection.Get<string[]>() ?? [])
    .Concat((corsSection.Value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            // A bejelentkezés HttpOnly sütiben utazik, ezért a felsorolt originöknek a sütit is
            // engedni kell (AllowAnyOrigin mellett ez nem lenne megengedett, itt csak listás origin van).
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// A frontend nginx-e proxyként továbbítja a kéréseket; a kliens valódi IP-címét (kéréskorláthoz,
// naplózáshoz) csak megbízható proxytól fogadjuk el (alapból a Docker belső, privát hálózatai).
// Ha a frontend elé HTTPS-proxy is kerül, két proxy van a kliens és a backend között: ekkor
// ForwardedHeaders:ForwardLimit = 2, különben a HTTPS-proxy címe számítana a kliens IP-jének, és
// a bejelentkezési korlát minden felhasználónál közös lenne.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = Math.Clamp(builder.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1), 1, 5);

    var knownNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
    foreach (var network in knownNetworks is { Length: > 0 } ? knownNetworks : ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network.Trim()));
    }

    // Egyedi proxycímek (pl. a gépen futó HTTPS-proxy), vesszővel elválasztva.
    var knownProxies = builder.Configuration.GetValue<string>("ForwardedHeaders:KnownProxies") ?? string.Empty;
    foreach (var proxy in knownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    }
});

// Jelszópróbálgatás elleni védelem: kliens-IP-nként percenként korlátozott számú bejelentkezés.
var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimitPerMinute", 10);
builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Túl sok bejelentkezési kísérlet. Kérjük, próbáld újra egy perc múlva." },
            cancellationToken);
    };
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

// Közös hibakezelő: váratlan hiba esetén egységes, magyar nyelvű JSON választ adunk (üres 500 helyett),
// a részletek csak a szervernaplóba kerülnek.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");

    // Átfedő foglalás (utolsó védvonal a mentéskor): magyar üzenettel 409.
    if (exception is BookingConflictException bookingConflict)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { message = bookingConflict.Message, traceId = context.TraceIdentifier });
        return;
    }

    // Párhuzamos kérések ütközése: nem szerverhiba, a felhasználó frissítés után újrapróbálhatja.
    if (exception is not null && SqlServerLocking.IsUniqueConstraintViolation(exception))
    {
        logger.LogWarning(exception, "Unique constraint violation for {Method} {Path}.", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Ez az adat időközben már rögzítésre került (például egy párhuzamos kérésből). Frissítsd az oldalt, és ellenőrizd az adatokat.",
            traceId = context.TraceIdentifier
        });
        return;
    }

    if (exception is not null && SqlServerLocking.IsTransientConcurrencyFailure(exception))
    {
        logger.LogWarning(exception, "Database contention for {Method} {Path}.", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "2";
        await context.Response.WriteAsJsonAsync(new
        {
            message = "A rendszer éppen egy párhuzamos műveletet dolgoz fel. Kérjük, néhány másodperc múlva próbáld újra.",
            traceId = context.TraceIdentifier
        });
        return;
    }

    logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new
    {
        message = "Váratlan szerverhiba történt. Kérjük, próbáld újra, és ha a hiba ismétlődik, jelezd az üzemeltetésnek.",
        traceId = context.TraceIdentifier
    });
}));

app.UseForwardedHeaders();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseMiddleware<AccessAuditMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

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
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
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

    await AdminBootstrapper.InitializeAsync(dbContext, app.Configuration, logger);
}

// Minden API-végpont egy közös csoportban van, hogy a DTO-k ellenőrző attribútumai
// ([Required], [MaxLength], [EmailAddress] ...) mindenhol lefussanak.
var api = app.MapGroup(string.Empty)
    .AddEndpointFilterFactory(DataAnnotationsValidationFilter.Create);

api.MapPost("/api/auth/login", async (
    LoginRequestDto request,
    HttpContext httpContext,
    AuthService authService,
    AuditService auditService,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Jelszo))
    {
        await auditService.LogForUserAsync(
            null,
            "LOGIN_INVALID",
            "Authentication",
            entitasId: request.Username);
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

    httpContext.Response.Cookies.Append(
        AuthCookie.Name,
        result.Token,
        AuthCookie.CreateOptions(httpContext.Request, result.ExpiresAt));
    return Results.Ok(result);
})
.RequireRateLimiting("login")
.WithName("Login")
.WithSummary("Felhasználói bejelentkezés – JWT token kiadás")
.WithTags("Auth")
.Produces<LoginResponseDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

api.MapPost("/api/auth/logout", async (HttpContext context, AuditService auditService) =>
{
    await auditService.LogAsync(context.User, "LOGOUT", "Authentication");
    context.Response.Cookies.Delete(AuthCookie.Name, AuthCookie.CreateOptions(context.Request, null));
    return Results.Ok(new { message = "Sikeres kijelentkezés." });
})
    .WithName("Logout")
    .WithSummary("Kijelentkezés; a kliens törli a JWT tokent.")
    .WithTags("Auth")
    .RequireAuthorization("Authenticated")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

// Munkamenet megújítása (hosszú műszak): a felület aktivitás közben időnként meghívja, így a portás
// nem jelentkezik ki műszak közben. A megújítás a bejelentkezéstől számított Jwt:MaxSessionHours óráig
// lehetséges; utána 401, és újra be kell lépni.
api.MapPost("/api/auth/refresh", async (HttpContext context, AuthService authService) =>
{
    var result = await authService.RefreshSessionAsync(context.User);
    if (result is null)
    {
        return Results.Json(
            new { message = "A munkamenet lejárt, kérjük, jelentkezz be újra." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    context.Response.Cookies.Append(
        AuthCookie.Name,
        result.Token,
        AuthCookie.CreateOptions(context.Request, result.ExpiresAt));
    return Results.Ok(new { expiresAt = result.ExpiresAt, sessionEndsAt = result.SessionEndsAt });
})
.WithName("RefreshSession")
.WithSummary("A bejelentkezési munkamenet megújítása (csúszó lejárat, felső határral)")
.WithTags("Auth")
.RequireAuthorization("Authenticated")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

api.MapGet("/api/auth/me", async (HttpContext context, AuthService authService) =>
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
.Produces(StatusCodes.Status401Unauthorized);

api.MapPost("/api/auth/change-password", async (
    ChangePasswordRequestDto request,
    HttpContext context,
    AuthService authService) =>
{
    var (success, error, newToken) = await authService.ChangePasswordAsync(context.User, request);
    if (success)
    {
        // A régi tokenek érvénytelenné váltak; ez a munkamenet az új tokennel folytatódik.
        context.Response.Cookies.Append(
            AuthCookie.Name,
            newToken!,
            AuthCookie.CreateOptions(context.Request, authService.GetTokenExpiry()));
        return Results.Ok(new { message = "A jelszó sikeresen megváltozott." });
    }

    return error is null
        ? Results.Json(new { message = "A felhasználó nem található." }, statusCode: StatusCodes.Status401Unauthorized)
        : Results.BadRequest(new { message = error });
})
// A jelenlegi jelszó próbálgatása ellen ugyanaz a kéréskorlát véd, mint a bejelentkezésnél.
.RequireRateLimiting("login")
.WithName("ChangeOwnPassword")
.WithSummary("A bejelentkezett felhasználó saját jelszavának cseréje")
.WithTags("Auth")
.RequireAuthorization("Authenticated")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status429TooManyRequests);

api.MapPut("/api/auth/pin", async (
    SetOwnPinRequestDto request,
    HttpContext context,
    AuthService authService) =>
{
    var (success, error) = await authService.SetOwnPinAsync(context.User, request);
    if (success)
    {
        return Results.Ok(new { message = "A PIN-kód beállítva." });
    }

    return error is null
        ? Results.Json(new { message = "A felhasználó nem található." }, statusCode: StatusCodes.Status401Unauthorized)
        : Results.BadRequest(new { message = error });
})
// A jelszó próbálgatása ellen ugyanaz a kéréskorlát véd, mint a bejelentkezésnél.
.RequireRateLimiting("login")
.WithName("SetOwnPin")
.WithSummary("Az oktató saját kulcsátvételi PIN-kódjának beállítása")
.WithTags("Auth")
.RequireAuthorization("Oktato")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status429TooManyRequests);

// Az adatbázis-ellenőrzés legfeljebb ennyi ideig tarthat, hogy elérhetetlen adatbázisnál a
// health-check gyorsan 503-at adjon, ne lógjon percekig.
var healthDatabaseTimeout = TimeSpan.FromSeconds(
    Math.Max(1, builder.Configuration.GetValue("HealthCheck:DatabaseTimeoutSeconds", 3)));

api.MapGet("/api/health", async (AppDbContext dbContext, ILogger<Program> logger, CancellationToken requestAborted) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
    timeout.CancelAfter(healthDatabaseTimeout);
    try
    {
        if (await dbContext.Database.CanConnectAsync(timeout.Token))
        {
            return Results.Ok(new { status = "ok", database = "connected" });
        }

        logger.LogWarning("Health check failed: database connection could not be established.");
        return Results.Json(
            new { status = "unhealthy", database = "disconnected" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
    {
        logger.LogWarning(
            "Health check failed: the database did not respond within {Timeout} seconds.",
            healthDatabaseTimeout.TotalSeconds);
        return Results.Json(
            new { status = "unhealthy", database = "timeout" },
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
.Produces(StatusCodes.Status503ServiceUnavailable);

api.MapUserEndpoints();
api.MapRoomEndpoints();
api.MapKeyEndpoints();
api.MapKeyMovementEndpoints();
api.MapBookingEndpoints();
api.MapTicketEndpoints();
api.MapMaintenanceEndpoints();
api.MapMasterAccessEndpoints();
api.MapReliabilityEndpoints();
api.MapNotificationEndpoints();
api.MapSystemAdministrationEndpoints();
api.MapReportEndpoints();

app.Run();

public partial class Program { }
