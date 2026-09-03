namespace FinViet.Application.DTOs;

public class CreateTransactionDto
{
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public string? EntryMethod { get; set; }
    /// <summary>Set only when the category came from AI/rule suggestion at import time
    /// (e.g. "AI_BATCH", "AI_PHOTO", "RULE") — null for a manually chosen category. Used to
    /// write a categorization_decision audit row so import decisions are traceable, matching
    /// what already happens for the interactive suggest-category flow.</summary>
    public string? AiSource { get; set; }
    public decimal? AiConfidence { get; set; }
}

public class UpdateTransactionDto
{
    // Partial update: a field left null is left unchanged. amount/merchant/transactionDate are
    // rejected (422 synced_transaction_fields_locked) when the transaction's wallet is sepay_linked.
    public string? CategoryId { get; set; }
    public decimal? Amount { get; set; }
    public string? Merchant { get; set; }
    public DateTime? TransactionDate { get; set; }
}

public class ClassifyTransactionDto
{
    public string? CategoryId { get; set; }
}

public class TransactionResponseDto
{
    public Guid TransactionId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string SourceChannel { get; set; } = string.Empty;
    public string EntryMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public Guid? TransferPairId { get; set; }
    public string? ExternalId { get; set; }

    /// <summary>
    /// Shared by every transaction produced from one split, so a client can group them and
    /// show that they came from a single payment. Null on transactions that were never split.
    /// </summary>
    public Guid? SplitGroupId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Request body for <c>POST /api/Transactions/{id}/split</c>.</summary>
public class SplitTransactionDto
{
    public List<SplitPartRequest>? Parts { get; set; }
}

/// <summary>One part of a split — the category it belongs to and how much of the original it takes.</summary>
public class SplitPartRequest
{
    public string? CategoryId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Optional per-part note. Falls back to the original transaction's note when omitted.</summary>
    public string? Note { get; set; }
}
