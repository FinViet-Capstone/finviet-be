using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Customer
{
    public Guid CustomerId { get; set; }

    public Guid? AdminId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    // ── Auth & Profile fields ────────────────────────────────
    public string? AvatarUrl { get; set; }

    public decimal? MonthlyIncomeExpected { get; set; }

    // Phân bổ 50-30-20 — NGUỒN SỰ THẬT DUY NHẤT của % hũ (schema v2.1: trên customer, INTEGER, tổng=100).
    public int NeedsPct { get; set; } = 50;

    public int WantsPct { get; set; } = 30;

    public int SavingsPct { get; set; } = 20;

    /// <summary>Firebase UID for Google OAuth users</summary>
    public string? GoogleId { get; set; }

    public bool IsEmailVerified { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    public bool IsActive { get; set; }

    public DateTime? DeletedAt { get; set; }

    // ── Navigation ───────────────────────────────────────────
    public virtual Admin? Admin { get; set; }

    public virtual ICollection<AiReport> AiReports { get; set; } = new List<AiReport>();

    public virtual ICollection<BudgetPlan> BudgetPlans { get; set; } = new List<BudgetPlan>();

    public virtual ICollection<Budget> Budgets { get; set; } = new List<Budget>();

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual ICollection<CustomerCategory> CustomerCategories { get; set; } = new List<CustomerCategory>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual ICollection<CustomerSubscription> CustomerSubscriptions { get; set; } = new List<CustomerSubscription>();

    public virtual ICollection<ImportBatch> ImportBatches { get; set; } = new List<ImportBatch>();

    public virtual ICollection<IncomeSource> IncomeSources { get; set; } = new List<IncomeSource>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<SavingGoal> SavingGoals { get; set; } = new List<SavingGoal>();

    public virtual ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<EmailVerificationToken> EmailVerificationTokens { get; set; } = new List<EmailVerificationToken>();
}
