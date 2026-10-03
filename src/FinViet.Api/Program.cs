using FinViet.Api.Middlewares;
using FinViet.Application;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure;
using FinViet.Infrastructure.Persistence;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;
using System.Text.Json;


AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// Enable Sentry only when a DSN is configured.
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry();
}


// ── Services ─────────────────────────────────────────────────────────────────
// Application layer (FluentValidation + ValidationBehavior)
builder.Services.AddApplicationServices();

// Infrastructure layer (DbContext, JWT, Email, Firebase, Avatar, Wallets)
// NOTE: DbContext is registered here via an Npgsql data source that maps Postgres
// enums (email_token_type, gender, ...). Do NOT add a separate AddDbContext above —
// AddDbContext uses TryAdd, so an earlier plain registration would win and enums
// would be sent as integers ("operator does not exist: email_token_type = integer").
builder.Services.AddInfrastructureServices(builder.Configuration);

// Register MediatR handlers from Infrastructure (where handlers live in pragmatic arch)
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(FinViet.Infrastructure.DependencyInjection).Assembly));

builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

// HttpContext.Items flag set by OnTokenValidated when the customer is locked/deleted.
const string AccountDeactivatedItemKey = "FinViet.AccountDeactivated";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey        = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer          = true,
        ValidIssuer             = builder.Configuration["Jwt:Issuer"],
        ValidateAudience        = true,
        ValidAudience           = builder.Configuration["Jwt:Audience"],
        ClockSkew               = TimeSpan.Zero
    };

    // Access tokens are stateless JWTs (45 min). Without this check a customer locked by
    // an admin keeps full API access until the token expires, because deactivation only
    // revokes refresh tokens. Re-check the account on every authenticated customer request.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            if (principal is null || !principal.IsInRole("Customer")) return; // Admin tokens: unaffected

            var idValue = principal.FindFirst("customerId")?.Value
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(idValue, out var customerId))
            {
                context.Fail("Token is missing a valid customer id.");
                return;
            }

            var statusService = context.HttpContext.RequestServices
                .GetRequiredService<ICustomerAccountStatusService>();
            if (!await statusService.IsActiveAsync(customerId, context.HttpContext.RequestAborted))
            {
                context.HttpContext.Items[AccountDeactivatedItemKey] = true;
                context.Fail("Account is deactivated.");
            }
        },

        // A failed OnTokenValidated normally becomes a bare 401, which the mobile client
        // would treat as "access token expired" and try to refresh. Answer with 403 + a
        // stable code instead so the client force-logs-out and tells the user why.
        OnChallenge = async context =>
        {
            if (!context.HttpContext.Items.ContainsKey(AccountDeactivatedItemKey)) return;

            context.HandleResponse();
            context.Response.StatusCode  = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                success = false,
                message = "Your account has been deactivated. Please contact support.",
                code    = AccountStatusCodes.AccountDeactivated,
                errors  = (object?)null
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    };
});

builder.Services.AddAuthorization();

// Swagger with JWT support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "FinViet API",
        Version     = "v1",
        Description = "Personal Finance Management API – FinViet"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Paste only the accessToken value returned from /api/auth/login. Swagger will add the Bearer prefix automatically."
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

// CORS (allow frontend)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var frontendUrl = builder.Configuration["AppSettings:FrontendUrl"] ?? "http://localhost:3000";
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ── App Pipeline ──────────────────────────────────────────────────────────────
var app = builder.Build();

var reindexRequested = args.Contains("--reindex-rag", StringComparer.OrdinalIgnoreCase);
if (reindexRequested && !args.Contains("--confirm-reindex", StringComparer.OrdinalIgnoreCase))
{
    app.Logger.LogError(
        "RAG re-index was not started. Back up PostgreSQL, keep Gemini:RagEnabled=false, then run with --reindex-rag --confirm-reindex.");
    return;
}

var adoptBaselineRequested = args.Contains("--adopt-database-baseline", StringComparer.OrdinalIgnoreCase);
var adoptionConfirmed = args.Contains("--confirm-adopt-baseline", StringComparer.OrdinalIgnoreCase);
var backupConfirmed = args.Contains("--confirm-database-backup", StringComparer.OrdinalIgnoreCase);

// Run embedded DbUp migrations before EF opens its enum-mapped data source, then seed data.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FinVietDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

    try
    {
        await DbInitializer.InitializeAsync(
            connectionString,
            db,
            builder.Configuration,
            app.Environment,
            logger,
            adoptBaselineRequested,
            adoptionConfirmed,
            backupConfirmed);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database initialization failed: {Message}", ex.Message);
        throw;
    }
}

if (adoptBaselineRequested)
{
    app.Logger.LogInformation("Database baseline adoption completed successfully.");
    return;
}

if (reindexRequested)
{
    if (builder.Configuration.GetValue<bool>("Gemini:RagEnabled"))
    {
        app.Logger.LogError("Set Gemini:RagEnabled=false before re-indexing to prevent mixed-vector retrieval.");
        return;
    }

    using var reindexScope = app.Services.CreateScope();
    var reindexer = reindexScope.ServiceProvider.GetRequiredService<IRagEmbeddingReindexService>();
    var processed = await reindexer.ReindexAsync();
    app.Logger.LogInformation(
        "Re-indexed {Processed} RAG chunks. Validate retrieval before setting Gemini:RagEnabled=true.",
        processed);
    return;
}

// Global exception handling (must be first)
app.UseMiddleware<ExceptionHandlingMiddleware>();

var swaggerEnabled =
    app.Environment.IsDevelopment() ||
    builder.Configuration.GetValue<bool>("Swagger:Enabled");

if (swaggerEnabled)
{
    app.UseSwagger();

    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "FinViet API v1");

        c.RoutePrefix = "swagger";
    });
}
app.UseHttpsRedirection();

// Serve avatar images from wwwroot/avatars/
app.UseStaticFiles();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/", () => Results.Ok(new
{
    service = "FinViet API",
    status = "running"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy"
}));

app.MapGet("/sentry-test", object () => throw new Exception("Sentry test event"));

app.Run();
