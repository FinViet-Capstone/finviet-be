using System;

namespace FinViet.Infrastructure.Persistence.Entities;

/// <summary>
/// A customer-defined auto-categorization rule: a merchant/beneficiary keyword that maps
/// to a category. New transactions whose merchant contains the keyword are assigned this
/// category; creating a rule also retro-applies it to matching historical transactions.
/// (BUSINESS_LOGIC §2 "Rule Assignment", §8 "Merchant rule".)
/// </summary>
public partial class MerchantRule
{
    public Guid RuleId { get; set; }

    public Guid CustomerId { get; set; }

    public string MerchantKeyword { get; set; } = null!;

    public string CategoryId { get; set; } = null!;

    /// <summary>How many transactions this rule has been applied to (retroactive + ongoing).</summary>
    public int AppliedCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
