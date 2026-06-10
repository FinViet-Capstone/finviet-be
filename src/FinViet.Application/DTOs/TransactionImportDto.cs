namespace FinViet.Application.DTOs;

public class BankExcelImportRequestDto
{
    public Guid WalletId { get; set; }
    public int? MaxRows { get; set; }
}

public class SmsImportRequestDto
{
    public Guid WalletId { get; set; }
    public string Content { get; set; } = string.Empty;
}

public class ImportTransactionsResponseDto
{
    public Guid? BatchId { get; set; }
    public int TotalRowsScanned { get; set; }
    public int TotalParsed { get; set; }
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public decimal NewWalletBalance { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<ImportedTransactionDto> Transactions { get; set; } = new();
}

public class ImportedTransactionDto
{
    public Guid TransactionId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
}

public class ParsedTransactionDto
{
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Note { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
}

/// <summary>Result of parsing an import source, including rows skipped before persistence.</summary>
public class ParseResult
{
    public List<ParsedTransactionDto> Rows { get; set; } = new();

    /// <summary>Total rows (Excel) or messages (SMS) examined, including those skipped.</summary>
    public int TotalRowsScanned { get; set; }

    /// <summary>Number of rows discarded during parsing (bad date, no amount, header rows, etc.).</summary>
    public int SkippedDuringParse { get; set; }

    /// <summary>Human-readable reasons for rows skipped during parsing.</summary>
    public List<string> ParseErrors { get; set; } = new();
}
