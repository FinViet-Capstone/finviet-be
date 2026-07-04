using System.Globalization;
using System.Text.RegularExpressions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;

namespace FinViet.Infrastructure.ExternalServices.TransactionImport;

public class SmsTransactionParser : ISmsTransactionParser
{
    public ParseResult Parse(string content)
    {
        var result = new ParseResult();
        var messages = Regex.Split(content.Trim(), @"\r?\n\s*\r?\n")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (messages.Count == 0 && !string.IsNullOrWhiteSpace(content))
            messages.Add(content.Trim());

        result.TotalRowsScanned = messages.Count;

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var amount = ExtractAmount(message);
            if (amount <= 0)
            {
                result.SkippedDuringParse++;
                result.ParseErrors.Add(
                    $"Tin nhắn {i + 1}: không tìm thấy số tiền. Cần có số tiền kèm đơn vị VND/VNĐ/đ " +
                    $"(ví dụ: 350,000 VND hoặc -350.000đ).");
                continue;
            }

            var transactionType = ClassifyDirection(message);
            var transactionDate = ExtractDateTime(message) ?? DateTime.UtcNow;

            result.Rows.Add(new ParsedTransactionDto
            {
                TransactionType = transactionType,
                Amount = amount,
                TransactionDate = transactionDate,
                Note = ExtractNote(message),
                RawText = message
            });
        }

        return result;
    }

    /// <summary>
    /// Decide income vs expense from the account's perspective. Explicit credit/debit
    /// markers ("ghi có" / "ghi nợ" / credited / debited) win; then the +/- sign right
    /// before the amount. Words like "chuyển tiền" describe the counterparty and are NOT
    /// used — they appear in incoming-transfer descriptions too and would misclassify.
    /// </summary>
    private static string ClassifyDirection(string message)
    {
        var lowered = message.ToLowerInvariant();

        // Strong, unambiguous credit/debit indicators.
        if (Regex.IsMatch(lowered, @"ghi\s*co|ghi\s*có|credited|\bcong\b|\bcộng\b|\bnhận\s*tiền\b|\bbáo\s*có\b"))
            return "INCOME";
        if (Regex.IsMatch(lowered, @"ghi\s*no|ghi\s*nợ|debited|\btru\b|\btrừ\b|\bbáo\s*nợ\b"))
            return "EXPENSE";

        // Sign immediately before a number (e.g. "+2.000.000", "-350,000").
        if (Regex.IsMatch(message, @"\+\s*\d"))
            return "INCOME";
        if (Regex.IsMatch(message, @"-\s*\d"))
            return "EXPENSE";

        // Fall back to weaker payment keywords, else default to expense.
        if (Regex.IsMatch(lowered, @"thanh\s*toan|thanh\s*toán"))
            return "EXPENSE";

        return "EXPENSE";
    }

    /// <summary>
    /// Prefer the transaction content after a "Nội dung:" / "ND:" / "Content:" marker;
    /// fall back to the whole (trimmed) message when no marker is present.
    /// </summary>
    private static string ExtractNote(string message)
    {
        var match = Regex.Match(
            message,
            @"(?:nội\s*dung|noi\s*dung|nd|content|description|mô\s*tả|mo\s*ta)\s*[:\-]\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            var content = match.Groups[1].Value.Trim().TrimEnd('.').Trim();
            if (!string.IsNullOrWhiteSpace(content))
                return TrimNote(content);
        }

        return TrimNote(message);
    }

    private static decimal ExtractAmount(string text)
    {
        var keywordPatterns = new[]
        {
            @"(?:bi\s*tru|bị\s*trừ|tru|trừ|duoc\s*cong|được\s*cộng|cong|cộng|credited|debited|amount|so\s*tien|số\s*tiền)\D{0,40}(\d{1,3}(?:[,.]\d{3})+|\d+)(?:\s*)(?:VND|VNĐ|đ|d)\b",
            @"(?:VND|VNĐ|đ|d)\s*(\d{1,3}(?:[,.]\d{3})+|\d+)"
        };

        foreach (var pattern in keywordPatterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var amount = ParseMoney(match.Groups[1].Value.Replace(".", ""));
                if (amount > 0)
                    return amount;
            }
        }

        var currencyMatch = Regex.Match(text, @"(?<!\d)(\d{1,3}(?:[,.]\d{3})+|\d+)(?:\s*)(?:VND|VNĐ|đ|d)\b", RegexOptions.IgnoreCase);
        if (currencyMatch.Success)
            return ParseMoney(currencyMatch.Groups[1].Value.Replace(".", ""));

        return 0;
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

    private static DateTime? ExtractDateTime(string text)
    {
        var match = Regex.Match(text, @"\b\d{1,2}/\d{1,2}/\d{4}(?:\s+\d{1,2}:\d{1,2}(?::\d{1,2})?)?\b");
        if (!match.Success)
            return null;

        return TryParseVietnameseDateTime(match.Value, out var date) ? date : null;
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
