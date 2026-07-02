using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.ExternalServices;
using FinViet.Infrastructure.ExternalServices.Documents;
using FinViet.Infrastructure.ExternalServices.Gemini;
using FinViet.Infrastructure.ExternalServices.Notification;
using FinViet.Infrastructure.ExternalServices.TransactionImport;
using FinViet.Infrastructure.ExternalServices.Finverse;
using FinViet.Infrastructure.Features.Auth.Commands.Login;
using FinViet.Infrastructure.Identity;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.Services;
using FinViet.Infrastructure.Services.Background;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector.EntityFrameworkCore;

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
        dataSourceBuilder.MapEnum<WalletType>("wallet_type");
        dataSourceBuilder.MapEnum<TransactionType>("transaction_type");
        dataSourceBuilder.MapEnum<EntryMethod>("entry_method");
        dataSourceBuilder.MapEnum<CategoryType>("category_type");
        dataSourceBuilder.MapEnum<CategorySource>("category_source");
        dataSourceBuilder.MapEnum<CategoryRequestStatus>("category_request_status");
        dataSourceBuilder.MapEnum<NotificationType>("notification_type");
        dataSourceBuilder.MapEnum<NotificationEntityType>("notification_entity_type");
        dataSourceBuilder.MapEnum<SubscriptionStatus>("subscription_status");
        dataSourceBuilder.MapEnum<ScoreView>("score_view");
        dataSourceBuilder.MapEnum<ScoreColor>("score_color");
        dataSourceBuilder.MapEnum<ChatRole>("chat_role");
        // The entities expose these enum columns as strings; an EF value converter
        // (PgEnumStringConverter) binds them to the CLR enums mapped above so Npgsql sends the
        // enum OID, not text. The remaining DB enums (chat_role, score_view, score_color) are not
        // currently surfaced by any mapped EF column, so they need no mapping yet.
        dataSourceBuilder.EnableUnmappedTypes();
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<FinVietDbContext>(options =>
            options.UseNpgsql(dataSource, o => o.UseVector()));

        // Finverse Data API. Credentials stay in server-side configuration/user-secrets;
        // Login Identity tokens are encrypted before database persistence.
        services.Configure<FinverseOptions>(configuration.GetSection(FinverseOptions.SectionName));
        services.AddMemoryCache();
        services.AddDataProtection().SetApplicationName("FinViet");
        services.AddSingleton<IFinverseTokenProtector, FinverseTokenProtector>();
        services.AddSingleton<IFinverseLinkStateProtector, FinverseLinkStateProtector>();
        services.AddHttpClient<IFinverseClient, FinverseClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<FinverseOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://api.prod.finverse.net/"
                : options.BaseUrl;
            http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");
            http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
        });

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
        services.AddScoped<IFinverseWalletService, FinverseWalletService>();
        services.AddScoped<IBudgetService, BudgetService>();

        // Category & Category Request Services
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ICategoryRequestService, CategoryRequestService>();

        // Merchant auto-categorization rules
        services.AddScoped<IMerchantRuleService, MerchantRuleService>();

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

        // ── RAG layer (pgvector + Gemini embeddings) ──────────────────────────────
        services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>((sp, http) =>
        {
            var cfg = configuration.GetSection(GeminiOptions.SectionName);
            var timeout = int.TryParse(cfg["TimeoutSeconds"], out var t) ? t : 30;
            http.Timeout = TimeSpan.FromSeconds(timeout);
        });
        services.AddSingleton<IAiRateLimiter, InMemoryAiRateLimiter>();
        services.AddScoped<IRagRetriever, PgVectorRagRetriever>();
        services.AddScoped<IDocumentIngestionService, PdfDocumentIngestionService>();

        services.AddHostedService<WeeklyReportScheduler>();

        return services;
    }
}
