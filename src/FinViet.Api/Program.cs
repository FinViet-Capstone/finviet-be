using FinViet.Api.Middlewares;
using FinViet.Application;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure;
using FinViet.Infrastructure.Persistence;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;


AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

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
        "RAG re-index was not started. Back up PostgreSQL, keep Ai:RagEnabled=false, then run with --reindex-rag --confirm-reindex.");
    return;
}

// Run migrations + seed data (idempotent)
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<FinVietDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
    try
    {
        await DbInitializer.InitializeAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed: {Message}", ex.Message);
    }
}

if (reindexRequested)
{
    if (builder.Configuration.GetValue<bool>("Ai:RagEnabled"))
    {
        app.Logger.LogError("Set Ai:RagEnabled=false before re-indexing to prevent mixed-vector retrieval.");
        return;
    }

    using var reindexScope = app.Services.CreateScope();
    var reindexer = reindexScope.ServiceProvider.GetRequiredService<IRagEmbeddingReindexService>();
    var processed = await reindexer.ReindexAsync();
    app.Logger.LogInformation(
        "Re-indexed {Processed} RAG chunks. Validate retrieval before setting Ai:RagEnabled=true.",
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
app.Run();
