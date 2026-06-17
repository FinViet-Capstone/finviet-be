namespace FinViet.Application.DTOs.Transactions;

/// <summary>Create a single transaction (expense/income). Spec §4 POST /transactions.</summary>
public class CreateTransactionRequest
{
    public Guid WalletId { get; set; }
    public string? CategoryId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>expense | income. transfer_* legs are created via POST /transactions/transfer.</summary>
    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public DateTime? TransactionDate { get; set; }

    /// <summary>manual | photo | sms_paste | csv_import | sepay_sync. Defaults to manual.</summary>
    public string? EntryMethod { get; set; }
}

/// <summary>Patch an existing transaction. Spec §4 PATCH /transactions/{id}.
/// All fields optional; only provided fields change.</summary>
public class UpdateTransactionRequest
{
    public string? CategoryId { get; set; }
    public decimal? Amount { get; set; }
    public Guid? WalletId { get; set; }
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public DateTime? TransactionDate { get; set; }

    /// <summary>Original AI-guessed category slug from the review screen; if it differs from the
    /// new category, a category_correction_log row is written.</summary>
    public string? OriginalAiGuess { get; set; }
}

/// <summary>Internal transfer between two wallets. Spec §4 POST /transactions/transfer.</summary>
public class TransferRequest
{
    public Guid FromWalletId { get; set; }
    public Guid ToWalletId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime? TransferDate { get; set; }
}

/// <summary>Both legs created by a transfer.</summary>
public class TransferResponse
{
    public TransactionResponse Out { get; set; } = null!;
    public TransactionResponse In { get; set; } = null!;
}

/// <summary>One item of a batch save (photo "Chấp nhận tất cả"). Spec §4 POST /transactions/batch.</summary>
public class BatchTransactionItem
{
    public string? CategoryId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Merchant { get; set; }
    public DateTime? TransactionDate { get; set; }
}

public class BatchTransactionRequest
{
    public Guid WalletId { get; set; }
    public List<BatchTransactionItem> Items { get; set; } = new();
}
