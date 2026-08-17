using System.Globalization;
using FinViet.Application.DTOs;

namespace FinViet.Infrastructure.ExternalServices.TransactionImport;

/// <summary>Shared per-row business rules for parsing a bank-statement export (column layout,
/// amount/date parsing, skip rules), used by both the Excel and CSV import paths in
/// <see cref="BankStatementExcelParser"/> so the two formats can't drift apart into different
/// parsing behavior for the same statement layout.</summary>
internal static class BankStatementRowParser
{
    public static void ParseRow(IReadOnlyList<string?> cells, ParseResult result)
    {
        var no = GetCell(cells, 1);

        // Rows without a numeric STT are headers/blank lines, not data — ignore silently.
        if (!int.TryParse(no, out _))
            return;

        result.TotalRowsScanned++;

        var dateText = GetCell(cells, 2);
        var debit = ParseMoney(GetCell(cells, 5));
        var credit = ParseMoney(GetCell(cells, 6));
        var description = GetCell(cells, 11);
        var correspondent = GetCell(cells, 13);

        var amount = credit > 0 ? credit : debit;
        if (amount <= 0)
        {
            result.SkippedDuringParse++;
            result.ParseErrors.Add($"Row {no}: no valid debit/credit amount.");
            return;
        }

        if (!TryParseVietnameseDateTime(dateText, out var transactionDate))
        {
            result.SkippedDuringParse++;
            result.ParseErrors.Add($"Row {no}: unrecognized date '{dateText}'.");
            return;
        }

        var transactionType = credit > 0 ? "INCOME" : "EXPENSE";
        var note = string.IsNullOrWhiteSpace(correspondent)
            ? description
            : $"{description} | Doi ung: {correspondent}";

        result.Rows.Add(new ParsedTransactionDto
        {
            TransactionType = transactionType,
            Amount = amount,
            TransactionDate = transactionDate,
            Note = TrimNote(note),
            RawText = string.Join(" | ", cells)
        });
    }

    private static string GetCell(IReadOnlyList<string?> cells, int index)
    {
        if (index >= cells.Count)
            return string.Empty;

        return cells[index]?.Trim() ?? string.Empty;
    }

    private static decimal ParseMoney(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var normalized = value.Replace(",", "").Replace("VND", "", StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0;
    }

    private static bool TryParseVietnameseDateTime(string value, out DateTime date)
    {
        var formats = new[]
        {
            "dd/MM/yyyy HH:mm:ss",
            "d/M/yyyy HH:mm:ss",
            "dd/MM/yyyy HH:mm",
            "d/M/yyyy HH:mm",
            "dd/MM/yyyy",
            "d/M/yyyy"
        };

        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
        {
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date))
        {
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    private static string TrimNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return string.Empty;

        return note.Length <= 500 ? note : note[..500];
    }
}
