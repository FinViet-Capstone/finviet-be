using System;
using System.Collections.Generic;
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

    public virtual DbSet<Bucket> Buckets { get; set; }

    public virtual DbSet<Budget> Budgets { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; }

    public virtual DbSet<ChatMessage> ChatMessages { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerCategory> CustomerCategories { get; set; }

    public virtual DbSet<CustomerSubscription> CustomerSubscriptions { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<SavingGoal> SavingGoals { get; set; }

    public virtual DbSet<SavingGoalContribution> SavingGoalContributions { get; set; }

    public virtual DbSet<ScoringCriterion> ScoringCriteria { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<SystemAnalytic> SystemAnalytics { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<Wallet> Wallets { get; set; }

    public virtual DbSet<WalletLink> WalletLinks { get; set; }

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
            entity.Property(e => e.ExpenseClass)
                .HasMaxLength(20)
                .HasColumnName("default_bucket");
            entity.Ignore(e => e.IsMandatory);
            entity.Property(e => e.Type)
                .HasColumnName("type");
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

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("chat_message_pkey");

            entity.ToTable("chat_message");

            entity.Property(e => e.MessageId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("message_id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.SenderType)
                .HasMaxLength(50)
                .HasColumnName("sender_type");
            entity.Property(e => e.Timestamps)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("timestamps");

            entity.HasOne(d => d.Customer).WithMany(p => p.ChatMessages)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("chat_message_customer_id_fkey");
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
                .HasColumnType("subscription_status")
                .HasColumnName("status");
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
                .HasColumnType("notification_type")
                .HasColumnName("type");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Message).HasColumnName("body");
            entity.Property(e => e.EntityType)
                .HasColumnType("notification_entity_type")
                .HasColumnName("entity_type");
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
                .HasColumnName("type");
            entity.Property(e => e.EntryMethod)
                .IsRequired()
                .HasColumnName("entry_method");
            entity.Property(e => e.WalletId)
                .IsRequired()
                .HasColumnName("wallet_id");
            entity.Property(e => e.TransferPairId).HasColumnName("transfer_pair_id");
            entity.Property(e => e.ExternalId).HasMaxLength(120).HasColumnName("external_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("updated_at");

            entity.Ignore(e => e.Note);
            entity.Ignore(e => e.BeneficiaryName);
            entity.Ignore(e => e.SourceChannel);
            entity.Ignore(e => e.IsAiClassified);
            entity.Ignore(e => e.AiConfidence);
            entity.Ignore(e => e.AiCategoryGuess);

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
            entity.Property(e => e.WalletType)
                .IsRequired()
                .HasColumnType("wallet_type")
                .HasColumnName("type");
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

            entity.HasOne(d => d.Link).WithOne(p => p.Wallet)
                .HasForeignKey<WalletLink>(l => l.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("wallet_links_wallet_id_fkey");
        });

        modelBuilder.Entity<AiSpendingScore>(entity =>
        {
            entity.HasKey(e => e.ScoreId).HasName("ai_spending_scores_pkey");

            entity.ToTable("ai_spending_scores");

            entity.HasIndex(e => new { e.CustomerId, e.PeriodType, e.PeriodStart },
                "ux_ai_spending_scores_customer_period").IsUnique();

            entity.Property(e => e.ScoreId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("score_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.PeriodType)
                .HasMaxLength(10)
                .HasColumnName("period_type");
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.FinalScore)
                .HasPrecision(5, 2)
                .HasColumnName("final_score");
            entity.Property(e => e.SpikeScore)
                .HasPrecision(5, 2)
                .HasColumnName("spike_score");
            entity.Property(e => e.BudgetScore)
                .HasPrecision(5, 2)
                .HasColumnName("budget_score");
            entity.Property(e => e.SavingsScore)
                .HasPrecision(5, 2)
                .HasColumnName("savings_score");
            entity.Property(e => e.WeightsJson)
                .HasColumnType("jsonb")
                .HasColumnName("weights_json");
            entity.Property(e => e.ColorBadge)
                .HasMaxLength(20)
                .HasColumnName("color_badge");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_spending_scores_customer_id_fkey");
        });

        modelBuilder.Entity<AiWeeklyReport>(entity =>
        {
            entity.HasKey(e => e.ReportId).HasName("ai_weekly_reports_pkey");

            entity.ToTable("ai_weekly_reports");

            entity.HasIndex(e => new { e.CustomerId, e.PeriodStart },
                "ux_ai_weekly_reports_customer_period").IsUnique();

            entity.Property(e => e.ReportId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("report_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.ScoreId).HasColumnName("score_id");
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.Narrative).HasColumnName("narrative");
            entity.Property(e => e.GeneratedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("generated_at");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_weekly_reports_customer_id_fkey");

            entity.HasOne(d => d.Score).WithMany()
                .HasForeignKey(d => d.ScoreId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("ai_weekly_reports_score_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);}
