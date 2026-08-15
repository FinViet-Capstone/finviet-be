using System;
using System.Collections.Generic;
using FinViet.Domain.Enums;
using FinViet.Infrastructure.Persistence.Conventions;
using FinViet.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinViet.Infrastructure.Persistence.Context;

public partial class FinVietDbContext : DbContext
{
    public FinVietDbContext()
    {
    }

    public FinVietDbContext(DbContextOptions<FinVietDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Admin> Admins { get; set; }

    public virtual DbSet<AiSpendingScore> AiSpendingScores { get; set; }

    public virtual DbSet<AiWeeklyReport> AiWeeklyReports { get; set; }

    public virtual DbSet<AiChatSession> AiChatSessions { get; set; }

    public virtual DbSet<AiCustomerPreference> AiCustomerPreferences { get; set; }

    public virtual DbSet<AiRateLimitWindow> AiRateLimitWindows { get; set; }

    public virtual DbSet<AiUsageEvent> AiUsageEvents { get; set; }

    public virtual DbSet<AiAuditEvent> AiAuditEvents { get; set; }

    public virtual DbSet<Bucket> Buckets { get; set; }

    public virtual DbSet<Budget> Budgets { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; }

    public virtual DbSet<MerchantRule> MerchantRules { get; set; }

    public virtual DbSet<ChatMessage> ChatMessages { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerCategory> CustomerCategories { get; set; }

    public virtual DbSet<CustomerSubscription> CustomerSubscriptions { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NotificationDevice> NotificationDevices { get; set; }

    public virtual DbSet<SavingGoal> SavingGoals { get; set; }

    public virtual DbSet<SavingGoalContribution> SavingGoalContributions { get; set; }

    public virtual DbSet<ScoringCriterion> ScoringCriteria { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<SystemAnalytic> SystemAnalytics { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<Wallet> Wallets { get; set; }

    public virtual DbSet<RagDocument> RagDocuments { get; set; }

    public virtual DbSet<RagChunk> RagChunks { get; set; }

    public virtual DbSet<SepayLink> SepayLinks { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Connection string is configured via DI in Infrastructure.DependencyInjection.
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.AdminId).HasName("admins_pkey");

            entity.ToTable("admins");

            entity.HasIndex(e => e.Email, "admins_email_key").IsUnique();

            entity.HasIndex(e => e.Username, "admins_username_key").IsUnique();

            entity.Property(e => e.AdminId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("categories_pkey");

            entity.ToTable("categories");

            entity.Property(e => e.CategoryId)
                .HasMaxLength(40)
                .HasColumnName("id");
            entity.Property(e => e.CategoryName)
                .HasMaxLength(80)
                .HasColumnName("name_vi");
            entity.Ignore(e => e.NameVi);
            entity.Property(e => e.NameEn)
                .HasMaxLength(80)
                .HasColumnName("name_en");
            entity.Property(e => e.DefaultBucket)
                .HasMaxLength(20)
                .HasColumnName("default_bucket");
            entity.Ignore(e => e.IsMandatory);
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasConversion(PgEnumStringConverter.Create<CategoryType>());
            entity.Property(e => e.Icon)
                .HasMaxLength(60)
                .HasColumnName("icon");
            entity.Property(e => e.Color)
                .HasMaxLength(7)
                .HasColumnName("color");
            entity.Property(e => e.SortOrder)
                .HasColumnName("sort_order");
        });

        modelBuilder.Entity<CategoryCorrectionLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("category_correction_log_pkey");

            entity.ToTable("category_correction_log");

            entity.Property(e => e.LogId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("log_id");
            entity.Property(e => e.AdminId).HasColumnName("admin_id");
            entity.Property(e => e.CorrectedCategoryId)
                .HasMaxLength(40)
                .HasColumnName("corrected_category_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.OriginalAiGuess)
                .HasMaxLength(40)
                .HasColumnName("original_ai_guess");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");

            entity.HasOne(d => d.Admin).WithMany(p => p.CategoryCorrectionLogs)
                .HasForeignKey(d => d.AdminId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("category_correction_log_admin_id_fkey");

            entity.HasOne(d => d.CorrectedCategory).WithMany(p => p.CategoryCorrectionLogs)
                .HasForeignKey(d => d.CorrectedCategoryId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("category_correction_log_corrected_category_id_fkey");

            entity.HasOne(d => d.Customer).WithMany(p => p.CategoryCorrectionLogs)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_correction_log_customer_id_fkey");

            entity.HasOne(d => d.Transaction).WithMany(p => p.CategoryCorrectionLogs)
                .HasForeignKey(d => d.TransactionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_correction_log_transaction_id_fkey");
        });

        modelBuilder.Entity<MerchantRule>(entity =>
        {
            entity.HasKey(e => e.RuleId).HasName("merchant_rules_pkey");

            entity.ToTable("merchant_rules");

            entity.Property(e => e.RuleId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("rule_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.MerchantKeyword)
                .HasMaxLength(255)
                .HasColumnName("merchant_keyword");
            entity.Property(e => e.CategoryId)
                .HasMaxLength(40)
                .HasColumnName("category_id");
            entity.Property(e => e.AppliedCount)
                .HasDefaultValue(0)
                .HasColumnName("applied_count");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("ai_chat_messages_pkey");

            entity.ToTable("ai_chat_messages");

            entity.Property(e => e.MessageId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Role)
                .HasColumnType("chat_role")
                .HasColumnName("role")
                .HasConversion(PgEnumStringConverter.Create<ChatRole>());
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.ChatMessages)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_chat_messages_customer_id_fkey");

            entity.HasOne(d => d.Session).WithMany(p => p.Messages)
                .HasForeignKey(d => new { d.SessionId, d.CustomerId })
                .HasPrincipalKey(p => new { p.SessionId, p.CustomerId })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_chat_messages_session_customer_fkey");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("customers_pkey");

            entity.ToTable("customers", tb =>
                tb.HasCheckConstraint("chk_buckets_sum", "(needs_pct + wants_pct + savings_pct) = 100"));

            entity.HasIndex(e => e.Email, "customers_email_key").IsUnique();
            entity.HasIndex(e => e.GoogleId, "customers_google_id_key").IsUnique();

            entity.Property(e => e.CustomerId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(120)
                .HasColumnName("display_name");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.GoogleId)
                .HasMaxLength(255)
                .HasColumnName("google_id");
            entity.Property(e => e.AvatarUrl)
                .HasMaxLength(512)
                .HasColumnName("avatar_url");
            entity.Property(e => e.Gender)
                .HasColumnName("gender");
            entity.Property(e => e.DateOfBirth)
                .HasColumnName("date_of_birth");
            entity.Property(e => e.MonthlyIncomeExpected)
                .HasPrecision(15, 2)
                .HasColumnName("monthly_income");
            entity.Property(e => e.NeedsPct).HasDefaultValue(50).HasColumnName("needs_pct");
            entity.Property(e => e.WantsPct).HasDefaultValue(30).HasColumnName("wants_pct");
            entity.Property(e => e.SavingsPct).HasDefaultValue(20).HasColumnName("savings_pct");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsEmailVerified)
                .HasDefaultValue(false)
                .HasColumnName("email_verified");
            entity.Property(e => e.EmailVerifiedAt)
                .HasColumnName("email_verified_at");
            entity.Property(e => e.OnboardingDone)
                .HasDefaultValue(false)
                .HasColumnName("onboarding_done");
            entity.Property(e => e.DeletedAt)
                .HasColumnName("deleted_at");

            entity.HasOne(d => d.Setting).WithOne(p => p.Customer)
                .HasForeignKey<CustomerSetting>(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("customer_settings_customer_id_fkey");
        });

        modelBuilder.Entity<CustomerSetting>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("customer_settings_pkey");

            entity.ToTable("customer_settings");

            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DefaultCurrency)
                .HasMaxLength(3)
                .HasDefaultValue("VND")
                .HasColumnName("default_currency");
            entity.Property(e => e.Language)
                .HasColumnName("language");
            entity.Property(e => e.Theme)
                .HasColumnName("theme");
            entity.Property(e => e.NotifBudget).HasDefaultValue(true).HasColumnName("notif_budget");
            entity.Property(e => e.NotifReport).HasDefaultValue(true).HasColumnName("notif_report");
            entity.Property(e => e.NotifGoals).HasDefaultValue(true).HasColumnName("notif_goals");
            entity.Property(e => e.NotifBudgetThresholds)
                .HasColumnName("notif_budget_thresholds");
            entity.Property(e => e.FcmToken)
                .HasMaxLength(255)
                .HasColumnName("fcm_token");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<CustomerSubscription>(entity =>
        {
            entity.HasKey(e => e.SubscriptionId).HasName("customer_subscriptions_pkey");

            entity.ToTable("customer_subscriptions");

            entity.Property(e => e.SubscriptionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion(PgEnumStringConverter.Create<SubscriptionStatus>());
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerSubscriptions)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("customer_subscriptions_customer_id_fkey");

            entity.HasOne(d => d.Plan).WithMany(p => p.CustomerSubscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("customer_subscriptions_plan_id_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("notifications_pkey");

            entity.ToTable("notifications");

            entity.Property(e => e.NotificationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasConversion(PgEnumStringConverter.Create<NotificationType>());
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Message).HasColumnName("body");
            entity.Property(e => e.EntityType)
                .HasColumnName("entity_type")
                .HasConversion(PgEnumStringConverter.CreateNullable<NotificationEntityType>());
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("sent_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("notifications_customer_id_fkey");
        });

        modelBuilder.Entity<NotificationDevice>(entity =>
        {
            entity.HasKey(e => e.DeviceId).HasName("notification_devices_pkey");

            entity.ToTable("notification_devices", tb =>
                tb.HasCheckConstraint(
                    "chk_notification_devices_platform",
                    "platform IN ('ios', 'android')"));

            entity.HasIndex(e => new { e.CustomerId, e.InstallationId },
                "uq_notification_devices_customer_installation").IsUnique();
            entity.HasIndex(e => e.Token, "uq_notification_devices_token").IsUnique();
            entity.HasIndex(e => e.CustomerId, "idx_notification_devices_customer");

            entity.Property(e => e.DeviceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Token).HasMaxLength(255).HasColumnName("token");
            entity.Property(e => e.Platform).HasMaxLength(10).HasColumnName("platform");
            entity.Property(e => e.InstallationId).HasMaxLength(100).HasColumnName("installation_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").HasColumnName("updated_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.NotificationDevices)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("notification_devices_customer_id_fkey");
        });

        modelBuilder.Entity<SavingGoal>(entity =>
        {
            entity.HasKey(e => e.GoalId).HasName("savings_goals_pkey");

            entity.ToTable("savings_goals");

            entity.Property(e => e.GoalId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.GoalName)
                .HasMaxLength(120)
                .HasColumnName("name");
            entity.Property(e => e.IconEmoji)
                .HasMaxLength(16)
                .HasColumnName("icon_emoji");
            entity.Property(e => e.TargetAmount)
                .HasPrecision(15, 2)
                .HasColumnName("target_amount");
            entity.Property(e => e.CurrentAmount)
                .HasPrecision(15, 2)
                .HasDefaultValue(0m)
                .HasColumnName("current_amount");
            entity.Property(e => e.Deadline).HasColumnName("deadline");
            entity.Property(e => e.FundingWalletId).HasColumnName("funding_wallet_id");
            entity.Property(e => e.IsCompleted).HasDefaultValue(false).HasColumnName("is_completed");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false).HasColumnName("is_deleted");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()").HasColumnName("updated_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.SavingGoals)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("savings_goals_customer_id_fkey");
        });

        modelBuilder.Entity<SavingGoalContribution>(entity =>
        {
            entity.HasKey(e => e.ContributionId).HasName("savings_goal_contributions_pkey");

            entity.ToTable("savings_goal_contributions");

            entity.Property(e => e.ContributionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.GoalId).HasColumnName("goal_id");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.ContributionDate)
                .HasDefaultValueSql("now()")
                .HasColumnName("contributed_at");
            entity.Property(e => e.Note)
                .HasMaxLength(255)
                .HasColumnName("note");
            entity.Property(e => e.Type)
                .HasMaxLength(20)
                .HasDefaultValue("contribution")
                .HasColumnName("type");

            entity.HasOne(d => d.Goal).WithMany(p => p.SavingGoalContributions)
                .HasForeignKey(d => d.GoalId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("savings_goal_contributions_goal_id_fkey");
        });

        modelBuilder.Entity<ScoringCriterion>(entity =>
        {
            entity.HasKey(e => e.CriterionId).HasName("scoring_criteria_pkey");

            entity.ToTable("scoring_criteria");

            entity.Property(e => e.CriterionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(30)
                .HasColumnName("code");
            entity.Property(e => e.CriterionName)
                .HasMaxLength(100)
                .HasColumnName("name_vi");
            entity.Property(e => e.WeightWeekly)
                .HasPrecision(5, 2)
                .HasColumnName("weight_weekly");
            entity.Property(e => e.WeightMonthly)
                .HasPrecision(5, 2)
                .HasColumnName("weight_monthly");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId).HasName("subscription_plans_pkey");

            entity.ToTable("subscription_plans");

            entity.Property(e => e.PlanId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(20)
                .HasColumnName("code");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");
            entity.Property(e => e.FeaturesJson)
                .HasColumnType("jsonb")
                .HasColumnName("features_json");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
        });

        modelBuilder.Entity<SystemAnalytic>(entity =>
        {
            entity.HasKey(e => e.AnalyticsId).HasName("system_analytics_pkey");

            entity.ToTable("system_analytics");

            entity.Property(e => e.AnalyticsId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AdminId).HasColumnName("admin_id");
            entity.Property(e => e.MetricName)
                .HasMaxLength(100)
                .HasColumnName("metric_name");
            entity.Property(e => e.MetricValue)
                .HasPrecision(15, 2)
                .HasColumnName("metric_value");
            entity.Property(e => e.RecordedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("recorded_at");

            entity.HasOne(d => d.Admin).WithMany(p => p.SystemAnalytics)
                .HasForeignKey(d => d.AdminId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("system_analytics_admin_id_fkey");
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasName("transactions_pkey");

            entity.ToTable("transactions");

            entity.HasIndex(e => new { e.CustomerId, e.TransactionDate, e.TransactionId }, "idx_tx_customer_date");
            entity.HasIndex(e => e.WalletId, "idx_tx_wallet");
            entity.HasIndex(e => e.CategoryId, "idx_tx_category");
            entity.HasIndex(e => e.TransferPairId, "idx_tx_pair").HasFilter("transfer_pair_id IS NOT NULL");
            entity.HasIndex(e => e.ExternalId, "uq_tx_external").IsUnique().HasFilter("external_id IS NOT NULL");

            entity.Property(e => e.TransactionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CategoryId)
                .HasMaxLength(40)
                .HasColumnName("category_id");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.Merchant)
                .HasMaxLength(255)
                .HasColumnName("merchant");
            entity.Property(e => e.TransactionDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("transaction_date");
            entity.Property(e => e.TransactionType)
                .HasColumnName("type")
                .HasConversion(PgEnumStringConverter.Create<TransactionType>());
            entity.Property(e => e.EntryMethod)
                .IsRequired()
                .HasColumnName("entry_method")
                .HasConversion(PgEnumStringConverter.Create<EntryMethod>());
            entity.Property(e => e.WalletId)
                .IsRequired()
                .HasColumnName("wallet_id");
            entity.Property(e => e.TransferPairId).HasColumnName("transfer_pair_id");
            entity.Property(e => e.ExternalId).HasMaxLength(120).HasColumnName("external_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("updated_at");

            entity.Ignore(e => e.SourceChannel);
            entity.Ignore(e => e.Note);
            entity.Ignore(e => e.BeneficiaryName);
            entity.Property(e => e.IsAiClassified)
                .HasDefaultValue(false)
                .HasColumnName("is_ai_classified");
            entity.Property(e => e.AiConfidence)
                .HasPrecision(5, 4)
                .HasColumnName("ai_confidence");
            entity.Property(e => e.AiCategoryGuess)
                .HasMaxLength(40)
                .HasColumnName("ai_category_guess");
            entity.Property(e => e.AiClassificationSource)
                .HasMaxLength(30)
                .HasColumnName("ai_classification_source");
            entity.Property(e => e.AiClassifiedAt).HasColumnName("ai_classified_at");

            entity.HasOne(d => d.Category).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("transactions_category_id_fkey");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("transactions_customer_id_fkey");

            entity.HasOne(d => d.Wallet).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("transactions_wallet_id_fkey");
        });

        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.HasKey(e => e.WalletId).HasName("wallets_pkey");

            entity.ToTable("wallets");

            entity.Property(e => e.WalletId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Balance)
                .HasPrecision(15, 2)
                .HasDefaultValue(0m)
                .HasColumnName("balance");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.WalletName)
                .HasMaxLength(120)
                .IsRequired()
                .HasColumnName("name");
            // The `type` column is the Postgres enum `wallet_type` (mapped via MapEnum<WalletType>).
            // The entity keeps WalletType as a normalized string ("basic"/"sepay_linked"); this
            // converter sends/receives it as the mapped CLR enum so Npgsql binds the parameter as
            // the enum type rather than text (text would fail: "column type is of type wallet_type
            // but expression is of type text").
            entity.Property(e => e.WalletType)
                .IsRequired()
                .HasColumnName("type")
                .HasConversion(PgEnumStringConverter.Create<WalletType>());
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.Wallets)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("wallets_customer_id_fkey");

            entity.HasOne(d => d.SepayLink).WithOne(p => p.Wallet)
                .HasForeignKey<SepayLink>(l => l.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("sepay_links_wallet_id_fkey");
        });

        modelBuilder.Entity<AiSpendingScore>(entity =>
        {
            entity.HasKey(e => e.ScoreId).HasName("ai_spending_scores_pkey");

            entity.ToTable("ai_spending_scores");

            entity.HasIndex(e => new { e.CustomerId, e.View, e.PeriodStart },
                "ux_ai_spending_scores_customer_period").IsUnique();

            entity.Property(e => e.ScoreId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.View)
                .HasColumnType("score_view")
                .HasColumnName("view")
                .HasConversion(PgEnumStringConverter.Create<ScoreView>());
            entity.Property(e => e.Score).HasColumnName("score");
            entity.Property(e => e.SpikeScore).HasColumnName("spike_score");
            entity.Property(e => e.BudgetScore).HasColumnName("budget_score");
            entity.Property(e => e.SavingsScore).HasColumnName("savings_score");
            entity.Property(e => e.Color)
                .HasColumnType("score_color")
                .HasColumnName("color")
                .HasConversion(PgEnumStringConverter.Create<ScoreColor>());
            entity.Property(e => e.VerdictVi)
                .HasMaxLength(120)
                .HasColumnName("verdict_vi");
            entity.Property(e => e.ReasonVi)
                .HasMaxLength(255)
                .HasColumnName("reason_vi");
            entity.Property(e => e.CommentaryVi).HasColumnName("commentary_vi");
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.GeneratedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("generated_at");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_spending_scores_customer_id_fkey");
        });

        modelBuilder.Entity<AiWeeklyReport>(entity =>
        {
            entity.HasKey(e => e.ReportId).HasName("ai_weekly_reports_pkey");

            entity.ToTable("ai_weekly_reports");

            entity.HasIndex(e => new { e.CustomerId, e.WeekStart },
                "ux_ai_weekly_reports_customer_period").IsUnique();

            entity.Property(e => e.ReportId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Narrative).HasColumnName("report_text_vi");
            entity.Property(e => e.WeekStart).HasColumnName("week_start");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.GeneratedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("generated_at");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_weekly_reports_customer_id_fkey");
        });

        modelBuilder.Entity<RagDocument>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("rag_document_pkey");
            entity.ToTable("rag_document");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.SourceType)
                .HasMaxLength(20)
                .HasColumnName("source_type");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Uri)
                .HasMaxLength(512)
                .HasColumnName("uri");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");

            entity.HasOne<Customer>().WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("rag_document_customer_id_fkey");
        });

        modelBuilder.Entity<RagChunk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("rag_chunk_pkey");
            entity.ToTable("rag_chunk");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.Embedding)
                .HasColumnType("vector(768)")
                .HasColumnName("embedding");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");

            entity.HasIndex(e => e.CustomerId, "ix_rag_chunk_customer");

            entity.HasOne(d => d.Document).WithMany(p => p.Chunks)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("rag_chunk_document_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);}
