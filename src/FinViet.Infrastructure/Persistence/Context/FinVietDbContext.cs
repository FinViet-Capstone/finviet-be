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

    public virtual DbSet<BudgetPlan> BudgetPlans { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<CategoryBudget> CategoryBudgets { get; set; }

    public virtual DbSet<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; }

    public virtual DbSet<ChatMessage> ChatMessages { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerSubscription> CustomerSubscriptions { get; set; }

    public virtual DbSet<FinancialModel> FinancialModels { get; set; }

    public virtual DbSet<ImportBatch> ImportBatches { get; set; }

    public virtual DbSet<IncomeSource> IncomeSources { get; set; }

    public virtual DbSet<ModelAllocation> ModelAllocations { get; set; }

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

            entity.ToTable("budget_plan");

            entity.Property(e => e.PlanId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("plan_id");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.PlanName)
                .HasMaxLength(100)
                .HasColumnName("plan_name");
            entity.Property(e => e.StartDate).HasColumnName("start_date");

            entity.HasOne(d => d.Customer).WithMany(p => p.BudgetPlans)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("budget_plan_customer_id_fkey");

            entity.HasOne(d => d.Model).WithMany(p => p.BudgetPlans)
                .HasForeignKey(d => d.ModelId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("budget_plan_model_id_fkey");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("category_pkey");

            entity.ToTable("category");

            entity.Property(e => e.CategoryId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("category_id");
            entity.Property(e => e.CategoryName)
                .HasMaxLength(100)
                .HasColumnName("category_name");
            entity.Property(e => e.ExpenseClass)
                .HasMaxLength(50)
                .HasColumnName("expense_class");
            entity.Property(e => e.IsMandatory)
                .HasDefaultValue(false)
                .HasColumnName("is_mandatory");
            entity.Property(e => e.ModelBucket)
                .HasMaxLength(50)
                .HasColumnName("model_bucket");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("type");
        });

        modelBuilder.Entity<CategoryBudget>(entity =>
        {
            entity.HasKey(e => e.CategoryBudgetId).HasName("category_budget_pkey");

            entity.ToTable("category_budget");

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
            entity.Property(e => e.PlanId).HasColumnName("plan_id");
            entity.Property(e => e.ThresholdPct)
                .HasPrecision(5, 2)
                .HasColumnName("threshold_pct");
            entity.Property(e => e.ThresholdType)
                .HasMaxLength(50)
                .HasColumnName("threshold_type");

            entity.HasOne(d => d.Category).WithMany(p => p.CategoryBudgets)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_budget_category_id_fkey");

            entity.HasOne(d => d.Plan).WithMany(p => p.CategoryBudgets)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("category_budget_plan_id_fkey");
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

        modelBuilder.Entity<FinancialModel>(entity =>
        {
            entity.HasKey(e => e.ModelId).HasName("financial_model_pkey");

            entity.ToTable("financial_model");

            entity.Property(e => e.ModelId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("model_id");
            entity.Property(e => e.AllocationRules)
                .HasColumnType("jsonb")
                .HasColumnName("allocation_rules");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ModelType)
                .HasMaxLength(50)
                .HasColumnName("model_type");
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

            entity.HasOne(d => d.Customer).WithMany(p => p.IncomeSources)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("income_source_customer_id_fkey");
        });

        modelBuilder.Entity<ModelAllocation>(entity =>
        {
            entity.HasKey(e => e.AllocationId).HasName("model_allocation_pkey");

            entity.ToTable("model_allocation");

            entity.Property(e => e.AllocationId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("allocation_id");
            entity.Property(e => e.CategoryType)
                .HasMaxLength(50)
                .HasColumnName("category_type");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.Percentage)
                .HasPrecision(5, 2)
                .HasColumnName("percentage");

            entity.HasOne(d => d.Model).WithMany(p => p.ModelAllocations)
                .HasForeignKey(d => d.ModelId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("model_allocation_model_id_fkey");
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
            entity.HasKey(e => e.TransactionId).HasName("transaction_pkey");

            entity.ToTable("transaction");

            entity.Property(e => e.TransactionId)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("transaction_id");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BatchId).HasColumnName("batch_id");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.ReportId).HasColumnName("report_id");
            entity.Property(e => e.SourceId).HasColumnName("source_id");
            entity.Property(e => e.TransactionDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("transaction_date");
            entity.Property(e => e.TransactionType)
                .HasMaxLength(50)
                .HasColumnName("transaction_type");
            entity.Property(e => e.WalletId).HasColumnName("wallet_id");

            entity.HasOne(d => d.Batch).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.BatchId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("transaction_batch_id_fkey");

            entity.HasOne(d => d.Category).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("transaction_category_id_fkey");

            entity.HasOne(d => d.Report).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.ReportId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("transaction_report_id_fkey");

            entity.HasOne(d => d.Source).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.SourceId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("transaction_source_id_fkey");

            entity.HasOne(d => d.Wallet).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.WalletId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("transaction_wallet_id_fkey");
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
                .IsRequired()
                .HasColumnName("balance");
            entity.Property(e => e.CustomerId)
                .IsRequired()
                .HasColumnName("customer_id");
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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
