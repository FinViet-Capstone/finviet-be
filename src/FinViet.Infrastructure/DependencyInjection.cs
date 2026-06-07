using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices;
using FinViet.Infrastructure.Features.Auth.Commands.Login;
using FinViet.Infrastructure.Identity;
using FinViet.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.ExternalServices.TransactionImport;

namespace FinViet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        services.AddDbContext<FinVietDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // JWT
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // Email (SendGrid)
        services.AddScoped<IEmailService, EmailService>();

        // Firebase Auth
        services.AddScoped<IFirebaseAuthService, FirebaseAuthService>();

        // Avatar storage
        services.AddScoped<IAvatarService, AvatarService>();

         // Repositories
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<ITransactionImportRepository, TransactionImportRepository>();

        // Transaction Import
        services.AddScoped<IBankStatementParser, BankStatementExcelParser>();
        services.AddScoped<ISmsTransactionParser, SmsTransactionParser>();

        // LoginCommandHandler exposed as scoped service for GoogleLoginCommandHandler to reuse
        services.AddScoped<LoginCommandHandler>();

        return services;
    }
}
