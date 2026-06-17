using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Transaction
{
    public Guid TransactionId { get; set; }

    public Guid WalletId { get; set; }

    public Guid? CategoryId { get; set; }

    public Guid? SourceId { get; set; }

    public Guid? BatchId { get; set; }

    public Guid? ReportId { get; set; }

    public string TransactionType { get; set; } = null!;

    /// <summary>How the transaction entered the system: SMS, MANUAL, CSV, or LINKED.</summary>
    public string SourceChannel { get; set; } = null!;

    public decimal Amount { get; set; }

    public DateTime? TransactionDate { get; set; }

    public string? Note { get; set; }

    public string? BeneficiaryName { get; set; }

    public bool IsAiClassified { get; set; }

    public decimal? AiConfidence { get; set; }

    public Guid? AiCategoryGuess { get; set; }

    public virtual ImportBatch? Batch { get; set; }

    public virtual Category? Category { get; set; }

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual AiReport? Report { get; set; }

    public virtual IncomeSource? Source { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
