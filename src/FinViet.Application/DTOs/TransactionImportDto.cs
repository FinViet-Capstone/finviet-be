namespace FinViet.Application.DTOs;

/// <summary>A single candidate transaction produced by a parser (SMS or bank statement),
/// before any persistence. Consumed by the extract flow.</summary>
public class ParsedTransactionDto
{
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Note { get; set; } = string.Empty;

    /// <summary>Counterparty/beneficiary name, when the source has a distinct column for it (a
    /// recognized correspondent header, or the legacy fixed-position layout's column 13) — kept
    /// separate from <see cref="Note"/> rather than appended into it, so callers can show it as
    /// its own field instead of it being permanently baked into the description text.</summary>
    public string? CorrespondentName { get; set; }

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
