using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Transaction
{
    public Guid TransactionId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid WalletId { get; set; }

    public string? CategoryId { get; set; }

    public decimal Amount { get; set; }

    public string TransactionType { get; set; } = null!;

    public string? Description { get; set; }

    public string? Merchant { get; set; }

    public DateTime? TransactionDate { get; set; }

    public string EntryMethod { get; set; } = null!;

    public Guid? TransferPairId { get; set; }

    public string? ExternalId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string SourceChannel
    {
        get => EntryMethod;
        set => EntryMethod = value;
    }

    public string? Note
    {
        get => Description;
        set => Description = value;
    }

    public string? BeneficiaryName
    {
        get => Merchant;
        set => Merchant = value;
    }

    public bool IsAiClassified { get; set; }

    public decimal? AiConfidence { get; set; }

    public string? AiCategoryGuess { get; set; }

    public virtual Category? Category { get; set; }

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual Customer? Customer { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
