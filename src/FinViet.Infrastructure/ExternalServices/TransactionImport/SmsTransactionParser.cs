using System.Globalization;
using System.Text.RegularExpressions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;

namespace FinViet.Infrastructure.ExternalServices.TransactionImport;

public class SmsTransactionParser : ISmsTransactionParser
{
    public List<ParsedTransactionDto> Parse(string content)
    {
        var result = new List<ParsedTransactionDto>();
        var messages = Regex.Split(content.Trim(), @"\r?\n\s*\r?\n")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (messages.Count == 0 && !string.IsNullOrWhiteSpace(content))
            messages.Add(content.Trim());

        foreach (var message in messages)
        {
            var amount = ExtractAmount(message);
            if (amount <= 0)
                continue;

            var lowered = message.ToLowerInvariant();
            var isIncome = lowered.Contains("cong")
                || lowered.Contains("cộng")
                || lowered.Contains("credited")
                || lowered.Contains("nhan")
                || lowered.Contains("nhận")
                || lowered.Contains("+");
            var isExpense = lowered.Contains("tru")
                || lowered.Contains("trừ")
                || lowered.Contains("debited")
                || lowered.Contains("thanh toan")
                || lowered.Contains("thanh toán")
                || lowered.Contains("chuyen tien")
                || lowered.Contains("chuyển tiền")
                || lowered.Contains("-");

            var transactionType = isIncome && !isExpense ? "INCOME" : "EXPENSE";
            var transactionDate = ExtractDateTime(message) ?? DateTime.UtcNow;

            result.Add(new ParsedTransactionDto
            {
                TransactionType = transactionType,
                Amount = amount,
                TransactionDate = transactionDate,
                Note = TrimNote(message),
                RawText = message
            });
        }

        return result;
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
