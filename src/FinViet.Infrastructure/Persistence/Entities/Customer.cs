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

    public virtual Admin? Admin { get; set; }

    public virtual ICollection<AiReport> AiReports { get; set; } = new List<AiReport>();

    public virtual ICollection<BudgetPlan> BudgetPlans { get; set; } = new List<BudgetPlan>();

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();

    public virtual ICollection<CustomerSubscription> CustomerSubscriptions { get; set; } = new List<CustomerSubscription>();

    public virtual ICollection<ImportBatch> ImportBatches { get; set; } = new List<ImportBatch>();

    public virtual ICollection<IncomeSource> IncomeSources { get; set; } = new List<IncomeSource>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<SavingGoal> SavingGoals { get; set; } = new List<SavingGoal>();

    public virtual ICollection<Wallet> Wallets { get; set; } = new List<Wallet>();
}
