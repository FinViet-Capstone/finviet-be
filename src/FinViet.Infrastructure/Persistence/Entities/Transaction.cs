using System;
using System.Collections.Generic;

namespace FinViet.Infrastructure.Persistence.Entities;

public partial class Transaction
{
    public Guid TransactionId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid WalletId { get; set; }

    public string? CategoryId { get; set; }

    public Guid? SourceId { get; set; }

    public Guid? BatchId { get; set; }

    public Guid? ReportId { get; set; }

    public string TransactionType { get; set; } = null!;

    /// <summary>How the transaction entered the system: manual, photo, sms_paste, csv_import, sepay_sync.</summary>
    public string? EntryMethod { get; set; }

    /// <summary>Legacy channel column (SMS/MANUAL/CSV/LINKED). Superseded by EntryMethod; kept nullable for older rows.</summary>
    public string? SourceChannel { get; set; }

    public decimal Amount { get; set; }

    public DateTime? TransactionDate { get; set; }

    /// <summary>New v2 free-text description (was Note).</summary>
    public string? Description { get; set; }

    /// <summary>Legacy note column; kept for back-compat. New code writes Description.</summary>
    public string? Note { get; set; }

    /// <summary>Beneficiary/merchant; AI + rule source. (Was BeneficiaryName.)</summary>
    public string? Merchant { get; set; }

    /// <summary>Legacy beneficiary column; kept for back-compat. New code writes Merchant.</summary>
    public string? BeneficiaryName { get; set; }

    /// <summary>Links the two legs (transfer_out/transfer_in) of an internal transfer.</summary>
    public Guid? TransferPairId { get; set; }

    /// <summary>External transaction id from SePay (idempotency / dedup).</summary>
    public string? ExternalId { get; set; }

    public bool IsAiClassified { get; set; }

    public decimal? AiConfidence { get; set; }

    public string? AiCategoryGuess { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ImportBatch? Batch { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<CategoryCorrectionLog> CategoryCorrectionLogs { get; set; } = new List<CategoryCorrectionLog>();

    public virtual AiReport? Report { get; set; }

    public virtual IncomeSource? Source { get; set; }

    public virtual Wallet? Wallet { get; set; }
}
