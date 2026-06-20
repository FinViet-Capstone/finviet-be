using FinViet.Domain.Enums;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>Per-customer preferences. Maps to table <c>customer_settings</c> (PK = customer_id).</summary>
public class CustomerSetting
{
    public Guid CustomerId { get; set; }

    public string DefaultCurrency { get; set; } = "VND";

    public AppLanguage Language { get; set; } = AppLanguage.Vi;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool NotifBudget { get; set; } = true;

    public bool NotifReport { get; set; } = true;

    public bool NotifGoals { get; set; } = true;

    public int[] NotifBudgetThresholds { get; set; } = { 80, 100 };

    public string? FcmToken { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;
}
