using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices;
using FinViet.Infrastructure.ExternalServices.Gemini;
using FinViet.Infrastructure.ExternalServices.Notification;
using FinViet.Infrastructure.ExternalServices.TransactionImport;
using FinViet.Infrastructure.Features.Auth.Commands.Login;
using FinViet.Infrastructure.Identity;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.Services;
using FinViet.Infrastructure.Services.Background;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IBudgetAlertNotifier, FirebaseBudgetAlertNotifier>();

        // Avatar storage
        services.AddScoped<IAvatarService, AvatarService>();

        // Repositories
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<ITransactionImportRepository, TransactionImportRepository>();

        // Transaction Import
        services.AddScoped<IBankStatementParser, BankStatementExcelParser>();
        services.AddScoped<ISmsTransactionParser, SmsTransactionParser>();

        // Wallet Service
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<IBudgetService, BudgetService>();

        // Category & Income Source Services
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IIncomeSourceService, IncomeSourceService>();
        services.AddScoped<ICategoryRequestService, CategoryRequestService>();

        // Saving Goals & Notifications
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ISavingGoalService, SavingGoalService>();

        // LoginCommandHandler exposed as scoped service for GoogleLoginCommandHandler to reuse
        services.AddScoped<LoginCommandHandler>();

        // ── AI feature suite ──────────────────────────────────────────────────────
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<AiLimitsOptions>(configuration.GetSection(AiLimitsOptions.SectionName));

        services.AddHttpClient<IGeminiClient, GeminiClient>((sp, http) =>
        {
            var cfg = configuration.GetSection(GeminiOptions.SectionName);
            var timeout = int.TryParse(cfg["TimeoutSeconds"], out var t) ? t : 30;
            http.Timeout = TimeSpan.FromSeconds(timeout);
        });

        services.AddScoped<IAiRateLimiter, AiRateLimiter>();
        services.AddScoped<IAiClassificationQueue, AiClassificationQueue>();
        services.AddScoped<IAiCategorizationService, AiCategorizationService>();
        services.AddScoped<IBeneficiaryRuleService, BeneficiaryRuleService>();
        services.AddScoped<ISpendingScoreService, SpendingScoreService>();
        services.AddScoped<IWeeklyReportService, WeeklyReportService>();
        services.AddScoped<IAiChatService, AiChatService>();
        services.AddScoped<IAiReportNotifier, FirebaseAiReportNotifier>();

        // Shared in-process signal for the classification queue (singleton bridge to the processor).
        services.AddSingleton<ClassificationQueueSignal>();
        services.AddHostedService<ClassificationQueueProcessor>();
        services.AddHostedService<WeeklyReportScheduler>();

        return services;
    }
}
