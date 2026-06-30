using System;
using System.Collections.Generic;
using FinViet.Domain.Enums;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Customer
{
    /// <summary>Maps to column <c>id</c> in the v3 schema.</summary>
    public Guid CustomerId { get; set; }

    public string Email { get; set; } = null!;

    public string? PasswordHash { get; set; }

    /// <summary>Firebase UID for Google OAuth users. Maps to <c>google_id</c>.</summary>
    public string? GoogleId { get; set; }

    /// <summary>Maps to column <c>display_name</c> in the v3 schema.</summary>
    public string FullName { get; set; } = null!;

    public string? AvatarUrl { get; set; }

    public Gender? Gender { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Maps to column <c>monthly_income</c> in the v3 schema.</summary>
    public decimal? MonthlyIncomeExpected { get; set; }

    // Phân bổ 50-30-20 — NGUỒN SỰ THẬT DUY NHẤT của % hũ (INTEGER, tổng=100).
    public int NeedsPct { get; set; } = 50;

    public int WantsPct { get; set; } = 30;

    public int SavingsPct { get; set; } = 20;

    public bool IsActive { get; set; }

    /// <summary>Maps to column <c>email_verified</c> in the v3 schema.</summary>
    public bool IsEmailVerified { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    public bool OnboardingDone { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // ── Navigation ───────────────────────────────────────────
    public virtual CustomerSetting? Setting { get; set; }

    public virtual ICollection<Budget> Budgets { get; set; } = new List<Budget>();

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual ICollection<CustomerCategory> CustomerCategories { get; set; } = new List<CustomerCategory>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual ICollection<CustomerSubscription> CustomerSubscriptions { get; set; } = new List<CustomerSubscription>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<SavingGoal> SavingGoals { get; set; } = new List<SavingGoal>();

    public virtual ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<EmailVerificationToken> EmailVerificationTokens { get; set; } = new List<EmailVerificationToken>();
}
