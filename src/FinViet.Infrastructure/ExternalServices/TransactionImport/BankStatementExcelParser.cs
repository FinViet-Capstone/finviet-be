using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;

namespace FinViet.Infrastructure.ExternalServices.TransactionImport;

public class BankStatementExcelParser : IBankStatementParser
{
    public List<ParsedTransactionDto> Parse(Stream fileStream, int? maxRows = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var reader = ExcelReaderFactory.CreateReader(fileStream);
        var dataSet = reader.AsDataSet();

        var rows = new List<ParsedTransactionDto>();
        foreach (DataTable sheet in dataSet.Tables)
        {
            rows.AddRange(ParseSheet(sheet));
        }

        if (maxRows.HasValue && maxRows.Value > 0)
            rows = rows.Take(maxRows.Value).ToList();

        return rows;
    }

    private static List<ParsedTransactionDto> ParseSheet(DataTable sheet)
    {
        var result = new List<ParsedTransactionDto>();

        for (var i = 0; i < sheet.Rows.Count; i++)
        {
            var row = sheet.Rows[i];
            var no = GetCell(row, 1);
            if (!int.TryParse(no, out _))
                continue;

            var dateText = GetCell(row, 2);
            var debit = ParseMoney(GetCell(row, 5));
            var credit = ParseMoney(GetCell(row, 6));
            var description = GetCell(row, 11);
            var correspondent = GetCell(row, 13);

            var amount = credit > 0 ? credit : debit;
            if (amount <= 0 || !TryParseVietnameseDateTime(dateText, out var transactionDate))
                continue;

            var transactionType = credit > 0 ? "INCOME" : "EXPENSE";
            var note = string.IsNullOrWhiteSpace(correspondent)
                ? description
                : $"{description} | Doi ung: {correspondent}";

            result.Add(new ParsedTransactionDto
            {
                TransactionType = transactionType,
                Amount = amount,
                TransactionDate = transactionDate,
                Note = TrimNote(note),
                RawText = string.Join(" | ", row.ItemArray.Select(x => x?.ToString()))
            });
        }

        return result;
    }

    private static string GetCell(DataRow row, int index)
    {
        if (index >= row.ItemArray.Length)
            return string.Empty;

        return row[index]?.ToString()?.Trim() ?? string.Empty;
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
