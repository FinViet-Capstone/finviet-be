using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.ExternalServices;
using FinViet.Infrastructure.ExternalServices.Gemini;
using FinViet.Infrastructure.ExternalServices.Notification;
using FinViet.Infrastructure.ExternalServices.Sepay;
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
using Npgsql;

namespace FinViet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database — build an Npgsql data source so Postgres enums can be mapped to CLR enums.
        // Default snake_case name translator maps e.g. VerifyEmail -> verify_email.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(
            configuration.GetConnectionString("DefaultConnection"));
        dataSourceBuilder.MapEnum<EmailTokenType>("email_token_type");
        dataSourceBuilder.MapEnum<Gender>("gender");
        dataSourceBuilder.MapEnum<AppLanguage>("app_language");
        dataSourceBuilder.MapEnum<AppTheme>("app_theme");
        dataSourceBuilder.MapEnum<SepaySyncStatus>("sepay_sync_status");
        dataSourceBuilder.MapEnum<WalletType>("wallet_type");
        dataSourceBuilder.MapEnum<TransactionType>("transaction_type");
        dataSourceBuilder.MapEnum<EntryMethod>("entry_method");
        dataSourceBuilder.MapEnum<CategoryType>("category_type");
        dataSourceBuilder.MapEnum<CategorySource>("category_source");
        dataSourceBuilder.MapEnum<CategoryRequestStatus>("category_request_status");
        dataSourceBuilder.MapEnum<NotificationType>("notification_type");
        dataSourceBuilder.MapEnum<NotificationEntityType>("notification_entity_type");
        dataSourceBuilder.MapEnum<SubscriptionStatus>("subscription_status");
        // The entities expose these enum columns as strings; an EF value converter
        // (PgEnumStringConverter) binds them to the CLR enums mapped above so Npgsql sends the
        // enum OID, not text. The remaining DB enums (chat_role, score_view, score_color) are not
        // currently surfaced by any mapped EF column, so they need no mapping yet.
        dataSourceBuilder.EnableUnmappedTypes();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<FinVietDbContext>(options =>
            options.UseNpgsql(dataSource));

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

        // Transaction extract (SMS/CSV → candidate rows + AI suggestions; no persistence)
        services.AddScoped<ITransactionExtractService, TransactionExtractService>();

        // Transaction Import parsers (shared by the extract flow)
        services.AddScoped<IBankStatementParser, BankStatementExcelParser>();
        services.AddScoped<ISmsTransactionParser, SmsTransactionParser>();

        // Wallet Service
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<IBudgetService, BudgetService>();

        // Category & Category Request Services
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ICategoryRequestService, CategoryRequestService>();

        // Merchant auto-categorization rules
        services.AddScoped<IMerchantRuleService, MerchantRuleService>();

        // Linked wallets (SePay): static-token API client + connect/exchange/sync service.
        services.AddMemoryCache();
        services.Configure<SepayOptions>(configuration.GetSection(SepayOptions.SectionName));
        services.AddHttpClient<ISepayClient, SepayClient>();
        services.AddScoped<ILinkedWalletService, LinkedWalletService>();

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

        services.AddScoped<IAiCategorizationService, AiCategorizationService>();
        services.AddScoped<IBeneficiaryRuleService, BeneficiaryRuleService>();
        services.AddScoped<ISpendingScoreService, SpendingScoreService>();
        services.AddScoped<IWeeklyReportService, WeeklyReportService>();
        services.AddScoped<IAiChatService, AiChatService>();
        services.AddScoped<IAiReportNotifier, FirebaseAiReportNotifier>();

        services.AddHostedService<WeeklyReportScheduler>();

        return services;
    }
}
