using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.ExternalServices;
using FinViet.Infrastructure.ExternalServices.Documents;
using FinViet.Infrastructure.ExternalServices.OpenAiCompatible;
using FinViet.Infrastructure.ExternalServices.Notification;
using FinViet.Infrastructure.ExternalServices.TransactionImport;
using FinViet.Infrastructure.ExternalServices.Finverse;
using FinViet.Infrastructure.ExternalServices.SePay;
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

        // SePay OAuth2 API. Credentials stay in server-side configuration/user-secrets;
        // OAuth tokens are encrypted before database persistence.
        services.Configure<SepayOptions>(configuration.GetSection(SepayOptions.SectionName));
        services.AddSingleton<ISepayTokenProtector, SepayTokenProtector>();
        services.AddHttpClient<ISepayClient, SepayClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<SepayOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://my.sepay.vn"
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
        services.AddScoped<ISepayWalletService, SepayWalletService>();
        services.AddScoped<IBudgetService, BudgetService>();

        // Category Services
        services.AddScoped<ICategoryService, CategoryService>();

        // Merchant auto-categorization rules
        services.AddScoped<IMerchantRuleService, MerchantRuleService>();

        // Saving Goals & Notifications
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ISavingGoalService, SavingGoalService>();

        // LoginCommandHandler exposed as scoped service for GoogleLoginCommandHandler to reuse
        services.AddScoped<LoginCommandHandler>();

        // ── AI feature suite ──────────────────────────────────────────────────────
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "Ai:BaseUrl must be an absolute URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ClassificationModel),
                "Ai:ClassificationModel is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.GenerationModel),
                "Ai:GenerationModel is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.EmbeddingModel),
                "Ai:EmbeddingModel is required.")
            .Validate(options => options.EmbeddingDimensions == AiOptions.RequiredEmbeddingDimensions,
                $"Ai:EmbeddingDimensions must be {AiOptions.RequiredEmbeddingDimensions} to match rag_chunk.embedding.")
            .Validate(options => options.TimeoutSeconds is >= 5 and <= 600,
                "Ai:TimeoutSeconds must be between 5 and 600.")
            .ValidateOnStart();
        services.Configure<AiLimitsOptions>(configuration.GetSection(AiLimitsOptions.SectionName));

        services.AddHttpClient<IAiModelClient, OpenAiCompatibleAiModelClient>((sp, http) =>
        {
            ConfigureAiHttpClient(http, sp.GetRequiredService<IOptions<AiOptions>>().Value);
        });

        services.AddScoped<IAiCategorizationService, AiCategorizationService>();
        services.AddScoped<IBeneficiaryRuleService, BeneficiaryRuleService>();
        services.AddScoped<ISpendingScoreService, SpendingScoreService>();
        services.AddScoped<IWeeklyReportService, WeeklyReportService>();
        services.AddScoped<IAiChatService, AiChatService>();
        services.AddScoped<IAiReportNotifier, FirebaseAiReportNotifier>();

        // ── RAG layer (pgvector + configured 768-dimensional embeddings) ──────────
        services.AddHttpClient<IEmbeddingService, OpenAiCompatibleEmbeddingService>((sp, http) =>
        {
            ConfigureAiHttpClient(http, sp.GetRequiredService<IOptions<AiOptions>>().Value);
        });
        services.AddSingleton<IAiRateLimiter, InMemoryAiRateLimiter>();
        services.AddScoped<IRagRetriever, PgVectorRagRetriever>();
        services.AddScoped<IRagEmbeddingReindexService, RagEmbeddingReindexService>();
        services.AddScoped<IDocumentIngestionService, PdfDocumentIngestionService>();

        services.AddHostedService<WeeklyReportScheduler>();

        return services;
    }

    private static void ConfigureAiHttpClient(HttpClient http, AiOptions options)
    {
        http.BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/");
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    }
}
