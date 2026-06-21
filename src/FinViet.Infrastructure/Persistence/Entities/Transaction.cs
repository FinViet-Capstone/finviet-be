using System;
using System.Collections.Generic;
using FinViet.Domain.Enums;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Transaction
{
    public Guid TransactionId { get; set; }

    public Guid CustomerId { get; set; }

    public Guid WalletId { get; set; }

    public string? CategoryId { get; set; }

    public decimal Amount { get; set; }

    public TransactionType TransactionType { get; set; }

    public string? Description { get; set; }

    public string? Merchant { get; set; }

    public DateTime? TransactionDate { get; set; }

    public EntryMethod EntryMethod { get; set; }

    public Guid? TransferPairId { get; set; }

    public string? ExternalId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

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

    // CLR-only compatibility members (not persisted in the v2.1 schema).
    public string? SourceChannel { get; set; }

    public bool IsAiClassified { get; set; }

    public decimal? AiConfidence { get; set; }

    public string? AiCategoryGuess { get; set; }

    public virtual Category? Category { get; set; }

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual Customer? Customer { get; set; }

    public virtual Wallet? Wallet { get; set; }
}

