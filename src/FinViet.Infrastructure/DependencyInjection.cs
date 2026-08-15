using FinViet.Application.Interfaces;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.ExternalServices;
using FinViet.Infrastructure.ExternalServices.Documents;
using FinViet.Infrastructure.ExternalServices.Gemini;
using FinViet.Infrastructure.ExternalServices.Notification;
using FinViet.Infrastructure.ExternalServices.Ocr;
using FinViet.Infrastructure.ExternalServices.TransactionImport;
using FinViet.Infrastructure.ExternalServices.SePay;
using FinViet.Infrastructure.ExternalServices.VNPay;
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
using Google.GenAI;
using Google.GenAI.Types;
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
        dataSourceBuilder.MapEnum<PaymentStatus>("payment_status");
        dataSourceBuilder.MapEnum<ChatRole>("chat_role");
        dataSourceBuilder.MapEnum<ScoreView>("score_view");
        dataSourceBuilder.MapEnum<ScoreColor>("score_color");
        // String-backed enum properties use PgEnumStringConverter so Npgsql binds parameters
        // with the PostgreSQL enum OID rather than as text. DbInitializer runs embedded DbUp
        // migrations through a separate raw connection before this data source is first opened.
        dataSourceBuilder.EnableUnmappedTypes();
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        void ConfigureDatabase(DbContextOptionsBuilder options) =>
            options.UseNpgsql(dataSource, o => o.UseVector());

        services.AddDbContext<FinVietDbContext>(ConfigureDatabase);

        services.AddMemoryCache();
        services.AddDataProtection().SetApplicationName("FinViet");

        // SePay API (OAuth2 + static User API token). Credentials stay in server-side
        // configuration/user-secrets; tokens are encrypted before database persistence.
        services.Configure<SepayOptions>(configuration.GetSection(SepayOptions.SectionName));
        services.AddSingleton<ISepayTokenProtector, SepayTokenProtector>();
        services.AddSingleton<ISepayLinkStateProtector, SepayLinkStateProtector>();
        services.AddHttpClient<ISepayClient, SepayClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<SepayOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://my.sepay.vn"
                : options.BaseUrl;
            http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");
            http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
        });

        // VNPay (recurring/tokenized billing). TmnCode/HashSecret are validated at the point of
        // use (VNPayClient.EnsureConfigured), not at startup, so the API still boots in
        // environments without VNPay configured — same convention as SePay's WebhookApiKey.
        services.AddOptions<VNPayOptions>()
            .Bind(configuration.GetSection(VNPayOptions.SectionName))
            .Validate(options => options.TimeoutSeconds is >= 5 and <= 120,
                "VNPay:TimeoutSeconds must be between 5 and 120.")
            .ValidateOnStart();
        services.AddHttpClient<IVNPayClient, VNPayClient>();
        services.AddScoped<ISubscriptionPaymentResultService, SubscriptionPaymentResultService>();

        // JWT
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // Email (SendGrid)
        services.AddScoped<IEmailService, EmailService>();

        // Firebase Auth
        services.AddScoped<IFirebaseAuthService, FirebaseAuthService>();

        // Avatar storage
        services.AddScoped<IAvatarService, AvatarService>();

        // Category icon storage
        services.AddScoped<ICategoryIconService, CategoryIconService>();

        // Repositories
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();

        // Transaction extract (SMS/CSV → candidate rows + AI suggestions; no persistence)
        services.AddScoped<ITransactionExtractService, TransactionExtractService>();

        // Receipt OCR (photo extraction). No provider chosen yet — placeholder throws 503 until
        // Ocr:Provider/Ocr:ApiKey are configured and a real implementation is swapped in.
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        services.AddScoped<IReceiptOcrService, UnconfiguredReceiptOcrService>();

        // Transaction Import parsers (shared by the extract flow)
        services.AddScoped<IBankStatementParser, BankStatementExcelParser>();
        services.AddScoped<ISmsTransactionParser, SmsTransactionParser>();

        // Wallet Service
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<ISepayWalletService, SepayWalletService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IIncomeAllocationService, IncomeAllocationService>();

        // Category Services
        services.AddScoped<ICategoryService, CategoryService>();

        // Merchant auto-categorization rules
        services.AddScoped<IMerchantRuleService, MerchantRuleService>();

        // Saving Goals & Notifications
        services.AddHttpClient<INotificationPushSender, ExpoNotificationPushSender>(http =>
        {
            http.BaseAddress = new Uri("https://exp.host/--/api/v2/");
            http.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IBudgetAlertNotifier, NotificationBudgetAlertNotifier>();
        services.AddScoped<IAiReportNotifier, NotificationAiReportNotifier>();
        services.AddScoped<ISavingGoalService, SavingGoalService>();

        // LoginCommandHandler exposed as scoped service for GoogleLoginCommandHandler to reuse
        services.AddScoped<LoginCommandHandler>();

        // ── AI feature suite (Gemini Developer API) ──────────────────────────────
        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .PostConfigure(options =>
            {
                if (options.GenerationFallbackModels is not { Length: 0 })
                    return;

                options.GenerationFallbackModels =
                [
                    "gemini-3-flash-preview",
                    "gemini-3.6-flash",
                    "gemini-2.5-flash",
                    "gemini-2.5-flash-lite"
                ];
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Gemini:ApiKey is required. Supply it through user-secrets or Gemini__ApiKey.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.FlashModel),
                "Gemini:FlashModel is required.")
            .Validate(options => options.TryGetGenerationModels(out _),
                "Gemini:GenerationFallbackModels must contain at most four non-empty, unique models and must not repeat Gemini:FlashModel.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.EmbeddingModel),
                "Gemini:EmbeddingModel is required.")
            .Validate(options => options.EmbeddingDimensions == GeminiOptions.RequiredEmbeddingDimensions,
                $"Gemini:EmbeddingDimensions must be {GeminiOptions.RequiredEmbeddingDimensions} to match rag_chunk.embedding.")
            .Validate(options => options.TimeoutSeconds is >= 5 and <= 600,
                "Gemini:TimeoutSeconds must be between 5 and 600.")
            .Validate(options => options.RagMinimumSimilarity is >= 0 and <= 1,
                "Gemini:RagMinimumSimilarity must be between 0 and 1.")
            .ValidateOnStart();
        services.Configure<AiLimitsOptions>(configuration.GetSection(AiLimitsOptions.SectionName));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            return new Client(
                apiKey: options.ApiKey,
                httpOptions: new HttpOptions
                {
                    Timeout = options.TimeoutSeconds * 1000
                });
        });
        services.AddSingleton<IGeminiSdkClient, GeminiSdkClient>();
        services.AddScoped<IAiTelemetryRecorder, AiTelemetryRecorder>();
        services.AddSingleton<IDbContextFactory<FinVietDbContext>>(
            new FinVietDbContextFactory(dataSource));
        services.AddScoped<IAiModelClient>(sp => new GeminiAiModelClient(
            sp.GetRequiredService<IGeminiSdkClient>(),
            sp.GetRequiredService<IOptions<GeminiOptions>>(),
            sp.GetRequiredService<IAiTelemetryRecorder>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GeminiAiModelClient>>()));

        services.AddScoped<IAiCategorizationService, AiCategorizationService>();
        services.AddScoped<IBeneficiaryRuleService, BeneficiaryRuleService>();
        services.AddScoped<ISpendingScoreService, SpendingScoreService>();
        services.AddScoped<IFinancialContextService, FinancialContextService>();
        services.AddScoped<IWeeklyReportService, WeeklyReportService>();
        services.AddScoped<IAiChatService, AiChatService>();

        // ── RAG layer (pgvector + Gemini 768-dimensional embeddings) ──────────────
        services.AddScoped<IEmbeddingService>(sp => new GeminiEmbeddingService(
            sp.GetRequiredService<IGeminiSdkClient>(),
            sp.GetRequiredService<IOptions<GeminiOptions>>(),
            sp.GetRequiredService<IAiRateLimiter>(),
            sp.GetRequiredService<IAiTelemetryRecorder>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GeminiEmbeddingService>>()));
        services.AddScoped<IAiRateLimiter, PostgresAiRateLimiter>();
        services.AddScoped<IRagRetriever, PgVectorRagRetriever>();
        services.AddScoped<IRagEmbeddingReindexService, RagEmbeddingReindexService>();
        services.AddScoped<IDocumentIngestionService, PdfDocumentIngestionService>();
        services.AddScoped<IRagDocumentQueryService, RagDocumentQueryService>();

        services.AddHostedService<WeeklyReportScheduler>();
        services.AddHostedService<SubscriptionRenewalScheduler>();

        return services;
    }

}
