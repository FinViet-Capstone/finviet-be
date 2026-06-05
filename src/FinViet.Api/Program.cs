using System.Security.Claims;
using System.Text;
using FinViet.Api.Middlewares;
using FinViet.Application;
using FinViet.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtIssuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Missing Jwt:Issuer configuration.");
var jwtAudience = jwtSection["Audience"] ?? throw new InvalidOperationException("Missing Jwt:Audience configuration.");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration.");

if (jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 characters long.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = ClaimTypes.NameIdentifier
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FinViet API",
        Version = "v1"
    });
    var bearerScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Bearer token for authenticated requests."
    };
    options.AddSecurityDefinition("Bearer", bearerScheme);
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous();
app.MapControllers();
app.Run();
