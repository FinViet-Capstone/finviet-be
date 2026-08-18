using System.Globalization;
using System.Text;
using FinViet.Application.DTOs;

namespace FinViet.Infrastructure.ExternalServices.TransactionImport;

/// <summary>Shared per-row business rules for parsing a bank-statement export (column layout,
/// amount/date parsing, skip rules), used by both the Excel and CSV import paths in
/// <see cref="BankStatementExcelParser"/> so the two formats can't drift apart into different
/// parsing behavior for the same statement layout.
///
/// Column resolution has two modes: if a header row naming its columns (Vietnamese or English) is
/// found, columns are resolved by name — this is what lets the simple "date, description, amount"
/// layout the mobile app advertises work. If no such header is recognized, parsing falls back to
/// the original fixed-position full bank-statement export layout (STT/date/debit/credit/description
/// /correspondent at columns 1/2/5/6/11/13), preserved exactly for backward compatibility.</summary>
internal static class BankStatementRowParser
{
    private static readonly string[] DateAliases = { "ngay", "ngay gd", "ngay giao dich", "date" };
    private static readonly string[] DescriptionAliases = { "dien giai", "noi dung", "mo ta", "description", "content" };
    private static readonly string[] AmountAliases = { "so tien", "amount" };
    private static readonly string[] DebitAliases = { "ghi no", "no", "debit" };
    private static readonly string[] CreditAliases = { "ghi co", "co", "credit" };

    // Different banks label the counterparty/beneficiary column very differently (unlike
    // date/description/amount, which are fairly standardized across the exports this app has
    // seen) — matched by substring below rather than exact equality, so e.g. "Tên đối tác giao
    // dịch" or "Người thụ hưởng tại NH" still resolve via "doi tac"/"thu huong" without needing
    // every bank's exact header phrase enumerated here.
    private static readonly string[] CorrespondentAliases =
    {
        "doi ung", "correspondent", "beneficiary",
        "doi tac", "thu huong", "nguoi nhan", "nguoi gui",
        "doi tuong giao dich", "ten khach hang doi ung",
    };

    private sealed class ColumnLayout
    {
        public int DateIndex;
        public int DescriptionIndex;
        public int? AmountIndex;
        public int? DebitIndex;
        public int? CreditIndex;
        public int? CorrespondentIndex;
    }

    public static void ParseRows(IEnumerable<IReadOnlyList<string?>> rows, ParseResult result)
    {
        ColumnLayout? layout = null;

        foreach (var cells in rows)
        {
            if (IsBlankRow(cells))
                continue;

            if (layout is null)
            {
                layout = TryResolveHeaderLayout(cells);
                if (layout is not null)
                    continue; // header row itself is not data
            }

            if (layout is not null)
                ParseNamedRow(cells, layout, result);
            else
                ParseLegacyPositionalRow(cells, result);
        }
    }

    private static bool IsBlankRow(IReadOnlyList<string?> cells)
        => cells.All(c => string.IsNullOrWhiteSpace(c));

    private static ColumnLayout? TryResolveHeaderLayout(IReadOnlyList<string?> cells)
    {
        int? dateIndex = null, descriptionIndex = null, amountIndex = null, debitIndex = null, creditIndex = null, correspondentIndex = null;

        for (var i = 0; i < cells.Count; i++)
        {
            var normalized = Normalize(cells[i]);
            if (normalized.Length == 0)
                continue;

            if (dateIndex is null && DateAliases.Contains(normalized)) dateIndex = i;
            else if (descriptionIndex is null && DescriptionAliases.Contains(normalized)) descriptionIndex = i;
            else if (amountIndex is null && AmountAliases.Contains(normalized)) amountIndex = i;
            else if (debitIndex is null && DebitAliases.Contains(normalized)) debitIndex = i;
            else if (creditIndex is null && CreditAliases.Contains(normalized)) creditIndex = i;
            else if (correspondentIndex is null && IsCorrespondentHeader(normalized)) correspondentIndex = i;
        }

        var hasAmount = amountIndex is not null || (debitIndex is not null && creditIndex is not null);
        if (dateIndex is null || descriptionIndex is null || !hasAmount)
            return null;

        return new ColumnLayout
        {
            DateIndex = dateIndex.Value,
            DescriptionIndex = descriptionIndex.Value,
            AmountIndex = amountIndex,
            DebitIndex = debitIndex,
            CreditIndex = creditIndex,
            CorrespondentIndex = correspondentIndex
        };
    }

    private static bool IsCorrespondentHeader(string normalizedHeader)
        => CorrespondentAliases.Any(alias => normalizedHeader.Contains(alias, StringComparison.Ordinal));

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim().ToLowerInvariant();
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd');
    }

    private static void ParseNamedRow(IReadOnlyList<string?> cells, ColumnLayout layout, ParseResult result)
    {
        result.TotalRowsScanned++;
        var rowLabel = $"Row {result.TotalRowsScanned}";

        var dateText = GetCell(cells, layout.DateIndex);
        var description = GetCell(cells, layout.DescriptionIndex);
        var correspondent = layout.CorrespondentIndex.HasValue ? GetCell(cells, layout.CorrespondentIndex.Value) : string.Empty;

        decimal amount;
        string transactionType;

        if (layout.AmountIndex.HasValue)
        {
            var signedAmount = ParseMoney(GetCell(cells, layout.AmountIndex.Value));
            amount = Math.Abs(signedAmount);
            transactionType = signedAmount < 0 ? "EXPENSE" : "INCOME";
        }
        else
        {
            var debit = layout.DebitIndex.HasValue ? ParseMoney(GetCell(cells, layout.DebitIndex.Value)) : 0;
            var credit = layout.CreditIndex.HasValue ? ParseMoney(GetCell(cells, layout.CreditIndex.Value)) : 0;
            amount = credit > 0 ? credit : debit;
            transactionType = credit > 0 ? "INCOME" : "EXPENSE";
        }

        if (amount <= 0)
        {
            result.SkippedDuringParse++;
            result.ParseErrors.Add($"{rowLabel}: no valid amount.");
            return;
        }

        if (!TryParseVietnameseDateTime(dateText, out var transactionDate))
        {
            result.SkippedDuringParse++;
            result.ParseErrors.Add($"{rowLabel}: unrecognized date '{dateText}'.");
            return;
        }

        result.Rows.Add(new ParsedTransactionDto
        {
            TransactionType = transactionType,
            Amount = amount,
            TransactionDate = transactionDate,
            Note = TrimNote(description),
            CorrespondentName = string.IsNullOrWhiteSpace(correspondent) ? null : correspondent.Trim(),
            RawText = string.Join(" | ", cells)
        });
    }

    private static void ParseLegacyPositionalRow(IReadOnlyList<string?> cells, ParseResult result)
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

        result.Rows.Add(new ParsedTransactionDto
        {
            TransactionType = transactionType,
            Amount = amount,
            TransactionDate = transactionDate,
            Note = TrimNote(description),
            CorrespondentName = string.IsNullOrWhiteSpace(correspondent) ? null : correspondent.Trim(),
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

        var isNegative = value.TrimStart().StartsWith("-", StringComparison.Ordinal);

        var normalized = value
            .Replace("VNĐ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("VND", "", StringComparison.OrdinalIgnoreCase)
            .Replace("đ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₫", "")
            .Trim();

        // Vietnamese-locale formatting uses '.' as the thousands separator and ',' as the decimal
        // separator (e.g. "1.250.000,50") — the inverse of the invariant convention this parser
        // otherwise assumes ("1,250,000.50"). Detect it before stripping separators so such values
        // parse to the right magnitude instead of silently landing three orders of magnitude off.
        var isVietnameseLocaleFormat =
            System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^-?\d{1,3}(\.\d{3})+(,\d+)?$");

        normalized = isVietnameseLocaleFormat
            ? normalized.Replace(".", "").Replace(",", ".")
            : normalized.Replace(",", "");

        normalized = normalized.Trim();

        if (!decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return 0;

        return isNegative && amount > 0 ? -amount : amount;
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
            "d/M/yyyy",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "yyyy/MM/dd"
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
