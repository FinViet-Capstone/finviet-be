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

    public virtual DbSet<AiReport> AiReports { get; set; }

    public virtual DbSet<AiReportDetail> AiReportDetails { get; set; }

    public virtual DbSet<AiSpendingScore> AiSpendingScores { get; set; }

    public virtual DbSet<AiWeeklyReport> AiWeeklyReports { get; set; }

    public virtual DbSet<AiClassificationQueueItem> AiClassificationQueueItems { get; set; }

    public virtual DbSet<AiUsageLog> AiUsageLogs { get; set; }

    public virtual DbSet<BeneficiaryRule> BeneficiaryRules { get; set; }

    public virtual DbSet<UserCategoryBucket> UserCategoryBuckets { get; set; }

    public virtual DbSet<BudgetPlan> BudgetPlans { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<CategoryBudget> CategoryBudgets { get; set; }

    public virtual DbSet<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; }

    public virtual DbSet<ChatMessage> ChatMessages { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerSubscription> CustomerSubscriptions { get; set; }

    public virtual DbSet<ImportBatch> ImportBatches { get; set; }

    public virtual DbSet<IncomeSource> IncomeSources { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<SavingGoal> SavingGoals { get; set; }

    public virtual DbSet<SavingGoalContribution> SavingGoalContributions { get; set; }

    public virtual DbSet<ScoringCriterion> ScoringCriteria { get; set; }

    public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

    public virtual DbSet<SystemAnalytic> SystemAnalytics { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<Wallet> Wallets { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Connection string is configured via DI in Infrastructure.DependencyInjection.
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.AdminId).HasName("admin_pkey");

            entity.ToTable("admin");

            entity.HasIndex(e => e.Email, "admin_email_key").IsUnique();

            entity.HasIndex(e => e.Username, "admin_username_key").IsUnique();

            entity.Property(e => e.AdminId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("admin_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
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

        modelBuilder.Entity<AiReport>(entity =>
        {
            entity.HasKey(e => e.ReportId).HasName("ai_report_pkey");

            entity.ToTable("ai_report");

            entity.Property(e => e.ReportId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("report_id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.GeneratedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("generated_date");
            entity.Property(e => e.SpendingScore).HasColumnName("spending_score");

            entity.Ignore(e => e.Transactions);

            entity.HasOne(d => d.Customer).WithMany(p => p.AiReports)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_report_customer_id_fkey");
        });

        modelBuilder.Entity<AiReportDetail>(entity =>
        {
            entity.HasKey(e => e.DetailId).HasName("ai_report_detail_pkey");

            entity.ToTable("ai_report_detail");

            entity.Property(e => e.DetailId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("detail_id");
            entity.Property(e => e.CriterionId).HasColumnName("criterion_id");
            entity.Property(e => e.ReportId).HasColumnName("report_id");
            entity.Property(e => e.Score).HasColumnName("score");

            entity.HasOne(d => d.Criterion).WithMany(p => p.AiReportDetails)
                .HasForeignKey(d => d.CriterionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_report_detail_criterion_id_fkey");

            entity.HasOne(d => d.Report).WithMany(p => p.AiReportDetails)
                .HasForeignKey(d => d.ReportId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_report_detail_report_id_fkey");
        });

        modelBuilder.Entity<BudgetPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId).HasName("budget_plan_pkey");

            entity.ToTable("budget_plan", tb =>
            {
                tb.HasCheckConstraint(
                    "budget_plan_bucket_sum_check",
                    "ABS((needs_pct + wants_pct + savings_pct) - 100) < 0.01");
                tb.HasCheckConstraint(
                    "budget_plan_bucket_range_check",
                    "needs_pct BETWEEN 0 AND 100 AND wants_pct BETWEEN 0 AND 100 AND savings_pct BETWEEN 0 AND 100");
            });

            entity.Property(e => e.PlanId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("plan_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.NeedsPct)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("50")
                .HasColumnName("needs_pct");
            entity.Property(e => e.PlanName)
                .HasMaxLength(100)
                .HasColumnName("plan_name");
            entity.Property(e => e.SavingsPct)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("20")
                .HasColumnName("savings_pct");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.WantsPct)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("30")
                .HasColumnName("wants_pct");

            entity.HasOne(d => d.Customer).WithMany(p => p.BudgetPlans)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("budget_plan_customer_id_fkey");
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
            entity.Property(e => e.IsMandatory)
                .HasDefaultValue(false)
                .HasColumnName("is_mandatory");
            entity.Property(e => e.Type)
                .HasMaxLength(20)
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

        modelBuilder.Entity<CategoryBudget>(entity =>
        {
            entity.HasKey(e => e.CategoryBudgetId).HasName("category_budget_pkey");

            entity.ToTable("category_budget");

            entity.HasIndex(e => e.WalletId, "idx_category_budget_wallet_id");

            entity.HasIndex(e => new { e.PlanId, e.CategoryId, e.WalletId }, "ux_category_budget_plan_category_wallet")
                .IsUnique()
                .AreNullsDistinct(false);

            entity.Property(e => e.CategoryBudgetId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("category_budget_id");
            entity.Property(e => e.AmountLimit)
                .HasPrecision(15, 2)
                .HasColumnName("amount_limit");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.CurrentSpent)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("current_spent");
            entity.Property(e => e.LastAlertThreshold)
                .HasPrecision(5, 2)
                .HasDefaultValueSql("0")
                .HasColumnName("last_alert_threshold");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.ThresholdPct)
                .HasPrecision(5, 2)
                .HasColumnName("threshold_pct");
            entity.Property(e => e.ThresholdType)
                .HasMaxLength(50)
                .HasColumnName("threshold_type");
            entity.Property(e => e.WalletId).HasColumnName("wallet_id");

            entity.HasOne(d => d.Category).WithMany(p => p.CategoryBudgets)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_budget_category_id_fkey");

            entity.HasOne(d => d.Plan).WithMany(p => p.CategoryBudgets)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_budget_plan_id_fkey");

            entity.HasOne(d => d.Wallet).WithMany(p => p.CategoryBudgets)
                .HasForeignKey(d => d.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_budget_wallet_id_fkey");
        });

        modelBuilder.Entity<CategoryCorrectionLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("category_correction_log_pkey");

            entity.ToTable("category_correction_log");

            entity.Property(e => e.LogId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("log_id");
            entity.Property(e => e.AdminId).HasColumnName("admin_id");
            entity.Property(e => e.CorrectedCategoryId).HasColumnName("corrected_category_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.OriginalAiGuess)
                .HasMaxLength(100)
                .HasColumnName("original_ai_guess");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");

            entity.HasOne(d => d.Admin).WithMany(p => p.CategoryCorrectionLogs)
                .HasForeignKey(d => d.AdminId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("category_correction_log_admin_id_fkey");

            entity.HasOne(d => d.CorrectedCategory).WithMany(p => p.CategoryCorrectionLogs)
                .HasForeignKey(d => d.CorrectedCategoryId)
                .OnDelete(DeleteBehavior.Cascade)
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
            entity.Property(e => e.ReportId).HasColumnName("report_id");
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

            entity.HasOne(d => d.Report).WithMany(p => p.ChatMessages)
                .HasForeignKey(d => d.ReportId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("chat_message_report_id_fkey");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("customer_pkey");

            entity.ToTable("customer");

            entity.HasIndex(e => e.Email, "customer_email_key").IsUnique();

            entity.Property(e => e.CustomerId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("customer_id");
            entity.Property(e => e.AdminId).HasColumnName("admin_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .HasColumnName("full_name");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Admin).WithMany(p => p.Customers)
                .HasForeignKey(d => d.AdminId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("customer_admin_id_fkey");
        });

        modelBuilder.Entity<CustomerSubscription>(entity =>
        {
            entity.HasKey(e => e.SubscriptionId).HasName("customer_subscription_pkey");

            entity.ToTable("customer_subscription");

            entity.Property(e => e.SubscriptionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("subscription_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'ACTIVE'::character varying")
                .HasColumnName("status");

            entity.HasOne(d => d.Customer).WithMany(p => p.CustomerSubscriptions)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("customer_subscription_customer_id_fkey");

            entity.HasOne(d => d.Plan).WithMany(p => p.CustomerSubscriptions)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("customer_subscription_plan_id_fkey");
        });

        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.HasKey(e => e.BatchId).HasName("import_batch_pkey");

            entity.ToTable("import_batch");

            entity.Property(e => e.BatchId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("batch_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.ImportDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("import_date");
            entity.Property(e => e.WalletId).HasColumnName("wallet_id");

            entity.Ignore(e => e.Transactions);

            entity.HasOne(d => d.Customer).WithMany(p => p.ImportBatches)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("import_batch_customer_id_fkey");

            entity.HasOne(d => d.Wallet).WithMany(p => p.ImportBatches)
                .HasForeignKey(d => d.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("import_batch_wallet_id_fkey");
        });

        modelBuilder.Entity<IncomeSource>(entity =>
        {
            entity.HasKey(e => e.SourceId).HasName("income_source_pkey");

            entity.ToTable("income_source");

            entity.Property(e => e.SourceId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("source_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");

            entity.Ignore(e => e.Transactions);

            entity.HasOne(d => d.Customer).WithMany(p => p.IncomeSources)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("income_source_customer_id_fkey");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("notification_pkey");

            entity.ToTable("notification");

            entity.Property(e => e.NotificationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("notification_id");
            entity.Property(e => e.CategoryBudgetId).HasColumnName("category_budget_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.GoalId).HasColumnName("goal_id");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");

            entity.HasOne(d => d.CategoryBudget).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.CategoryBudgetId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("notification_category_budget_id_fkey");

            entity.HasOne(d => d.Customer).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("notification_customer_id_fkey");

            entity.HasOne(d => d.Goal).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.GoalId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("notification_goal_id_fkey");
        });

        modelBuilder.Entity<SavingGoal>(entity =>
        {
            entity.HasKey(e => e.GoalId).HasName("saving_goal_pkey");

            entity.ToTable("saving_goal");

            entity.Property(e => e.GoalId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("goal_id");
            entity.Property(e => e.CurrentAmount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("current_amount");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Deadline).HasColumnName("deadline");
            entity.Property(e => e.GoalName)
                .HasMaxLength(100)
                .HasColumnName("goal_name");
            entity.Property(e => e.TargetAmount)
                .HasPrecision(15, 2)
                .HasColumnName("target_amount");

            entity.HasOne(d => d.Customer).WithMany(p => p.SavingGoals)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("saving_goal_customer_id_fkey");
        });

        modelBuilder.Entity<SavingGoalContribution>(entity =>
        {
            entity.HasKey(e => e.ContributionId).HasName("saving_goal_contribution_pkey");

            entity.ToTable("saving_goal_contribution");

            entity.Property(e => e.ContributionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("contribution_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.ContributionDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("contribution_date");
            entity.Property(e => e.GoalId).HasColumnName("goal_id");

            entity.HasOne(d => d.Goal).WithMany(p => p.SavingGoalContributions)
                .HasForeignKey(d => d.GoalId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("saving_goal_contribution_goal_id_fkey");
        });

        modelBuilder.Entity<ScoringCriterion>(entity =>
        {
            entity.HasKey(e => e.CriterionId).HasName("scoring_criteria_pkey");

            entity.ToTable("scoring_criteria");

            entity.Property(e => e.CriterionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("criterion_id");
            entity.Property(e => e.CriterionName)
                .HasMaxLength(100)
                .HasColumnName("criterion_name");
            entity.Property(e => e.Formula)
                .HasMaxLength(255)
                .HasColumnName("formula");
            entity.Property(e => e.Weight)
                .HasPrecision(5, 2)
                .HasColumnName("weight");
        });

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId).HasName("subscription_plan_pkey");

            entity.ToTable("subscription_plan");

            entity.Property(e => e.PlanId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("plan_id");
            entity.Property(e => e.FeaturesJson)
                .HasColumnType("jsonb")
                .HasColumnName("features_json");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");
        });

        modelBuilder.Entity<SystemAnalytic>(entity =>
        {
            entity.HasKey(e => e.AnalyticsId).HasName("system_analytics_pkey");

            entity.ToTable("system_analytics");

            entity.Property(e => e.AnalyticsId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("analytics_id");
            entity.Property(e => e.AdminId).HasColumnName("admin_id");
            entity.Property(e => e.MetricName)
                .HasMaxLength(100)
                .HasColumnName("metric_name");
            entity.Property(e => e.MetricValue)
                .HasPrecision(15, 2)
                .HasColumnName("metric_value");
            entity.Property(e => e.RecordedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
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
                .HasMaxLength(50)
                .HasColumnName("type");
            entity.Property(e => e.EntryMethod)
                .HasMaxLength(20)
                .IsRequired()
                .HasColumnName("entry_method");
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
            entity.Ignore(e => e.SourceId);
            entity.Ignore(e => e.BatchId);
            entity.Ignore(e => e.ReportId);
            entity.Ignore(e => e.IsAiClassified);
            entity.Ignore(e => e.AiConfidence);
            entity.Ignore(e => e.AiCategoryGuess);
            entity.Ignore(e => e.Batch);
            entity.Ignore(e => e.Report);
            entity.Ignore(e => e.Source);

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
            entity.HasKey(e => e.WalletId).HasName("wallet_pkey");

            entity.ToTable("wallet");

            entity.Property(e => e.WalletId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("wallet_id");
            entity.Property(e => e.Balance)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("0.00")
                .HasColumnName("balance");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.WalletName)
                .HasMaxLength(100)
                .IsRequired()
                .HasColumnName("wallet_name");
            entity.Property(e => e.WalletType)
                .HasMaxLength(50)
                .IsRequired()
                .HasColumnName("wallet_type");

            entity.HasOne(d => d.Customer).WithMany(p => p.Wallets)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("wallet_customer_id_fkey");
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

        modelBuilder.Entity<AiClassificationQueueItem>(entity =>
        {
            entity.HasKey(e => e.QueueId).HasName("ai_classification_queue_pkey");

            entity.ToTable("ai_classification_queue");

            entity.HasIndex(e => new { e.Status, e.NextAttemptAt },
                "idx_ai_classification_queue_status_next");

            entity.Property(e => e.QueueId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("queue_id");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.RawInput).HasColumnName("raw_input");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");
            entity.Property(e => e.AttemptCount)
                .HasDefaultValue(0)
                .HasColumnName("attempt_count");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.EnqueuedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("enqueued_at");
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
            entity.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at");

            entity.HasOne(d => d.Transaction).WithMany()
                .HasForeignKey(d => d.TransactionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_classification_queue_transaction_id_fkey");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_classification_queue_customer_id_fkey");
        });

        modelBuilder.Entity<AiUsageLog>(entity =>
        {
            entity.HasKey(e => e.UsageId).HasName("ai_usage_log_pkey");

            entity.ToTable("ai_usage_log");

            entity.HasIndex(e => new { e.CustomerId, e.CalledAt },
                "idx_ai_usage_log_customer_called");

            entity.Property(e => e.UsageId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("usage_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Feature)
                .HasMaxLength(30)
                .HasColumnName("feature");
            entity.Property(e => e.CalledAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("called_at");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ai_usage_log_customer_id_fkey");
        });

        modelBuilder.Entity<BeneficiaryRule>(entity =>
        {
            entity.HasKey(e => e.RuleId).HasName("beneficiary_rule_pkey");

            entity.ToTable("beneficiary_rule");

            entity.HasIndex(e => new { e.CustomerId, e.MatchText },
                "ux_beneficiary_rule_customer_match").IsUnique();

            entity.Property(e => e.RuleId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("rule_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.MatchText).HasColumnName("match_text");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.IsRecurring)
                .HasDefaultValue(false)
                .HasColumnName("is_recurring");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("beneficiary_rule_customer_id_fkey");

            entity.HasOne(d => d.Category).WithMany()
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("beneficiary_rule_category_id_fkey");
        });

        modelBuilder.Entity<UserCategoryBucket>(entity =>
        {
            entity.HasKey(e => new { e.CustomerId, e.CategoryId })
                .HasName("user_category_buckets_pkey");

            entity.ToTable("user_category_buckets");

            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.Bucket)
                .HasMaxLength(10)
                .HasColumnName("bucket");

            entity.HasOne(d => d.Customer).WithMany()
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("user_category_buckets_customer_id_fkey");

            entity.HasOne(d => d.Category).WithMany()
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("user_category_buckets_category_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);}
