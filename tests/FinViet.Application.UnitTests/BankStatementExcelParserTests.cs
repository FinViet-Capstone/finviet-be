using System.Text;
using FinViet.Infrastructure.ExternalServices.TransactionImport;

namespace FinViet.Application.UnitTests;

public class BankStatementExcelParserTests
{
    [Fact]
    public void Parse_Csv_ParsesCreditRowAsIncome()
    {
        var csv = BuildCsv(
            BuildRow(no: "1", date: "15/08/2026", debit: "", credit: "150000", description: "Coffee shop", correspondent: "ABC Corp"));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal("INCOME", row.TransactionType);
        Assert.Equal(150_000m, row.Amount);
        Assert.Equal(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), row.TransactionDate);
        Assert.Equal("Coffee shop | Doi ung: ABC Corp", row.Note);
        Assert.Equal(1, result.TotalRowsScanned);
        Assert.Equal(0, result.SkippedDuringParse);
    }

    [Fact]
    public void Parse_Csv_ParsesDebitRowAsExpenseAndHandlesThousandsSeparatorAndVndSuffix()
    {
        var csv = BuildCsv(
            BuildRow(no: "2", date: "1/8/2026 09:30", debit: "1,250,000 VND", credit: "", description: "Grocery", correspondent: ""));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal("EXPENSE", row.TransactionType);
        Assert.Equal(1_250_000m, row.Amount);
        Assert.Equal("Grocery", row.Note);
    }

    [Fact]
    public void Parse_Csv_SkipsHeaderAndBlankRowsSilently()
    {
        var csv = BuildCsv(
            BuildRow(no: "STT", date: "Ngay", debit: "No", credit: "Co", description: "Dien giai", correspondent: "Doi ung"),
            BuildRow(no: "", date: "", debit: "", credit: "", description: "", correspondent: ""),
            BuildRow(no: "1", date: "15/08/2026", debit: "", credit: "50000", description: "Real row", correspondent: ""));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        Assert.Single(result.Rows);
        Assert.Equal(1, result.TotalRowsScanned);
        Assert.Empty(result.ParseErrors);
    }

    [Fact]
    public void Parse_Csv_SkipsRowWithNoAmountAndRecordsError()
    {
        var csv = BuildCsv(
            BuildRow(no: "1", date: "15/08/2026", debit: "", credit: "0", description: "No amount", correspondent: ""));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.TotalRowsScanned);
        Assert.Equal(1, result.SkippedDuringParse);
        Assert.Contains(result.ParseErrors, e => e.Contains("no valid debit/credit amount"));
    }

    [Fact]
    public void Parse_Csv_SkipsRowWithUnrecognizedDateAndRecordsError()
    {
        var csv = BuildCsv(
            BuildRow(no: "1", date: "not-a-date", debit: "", credit: "50000", description: "Bad date", correspondent: ""));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.SkippedDuringParse);
        Assert.Contains(result.ParseErrors, e => e.Contains("unrecognized date"));
    }

    [Fact]
    public void Parse_Csv_RespectsMaxRows()
    {
        var csv = BuildCsv(
            BuildRow(no: "1", date: "15/08/2026", debit: "", credit: "10000", description: "Row 1", correspondent: ""),
            BuildRow(no: "2", date: "15/08/2026", debit: "", credit: "20000", description: "Row 2", correspondent: ""),
            BuildRow(no: "3", date: "15/08/2026", debit: "", credit: "30000", description: "Row 3", correspondent: ""));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv", maxRows: 2);

        Assert.Equal(2, result.Rows.Count);
    }

    private static string BuildRow(string no, string date, string debit, string credit, string description, string correspondent)
        => string.Join(",", new[] { "", no, date, "", "", debit, credit, "", "", "", "", description, "", correspondent }
            .Select(QuoteIfNeeded));

    // Real bank CSV exports quote fields containing commas (e.g. "1,250,000 VND") — quoting here
    // matches that shape and exercises CsvHelper's normal quoted-field handling.
    private static string QuoteIfNeeded(string field) => field.Contains(',') ? $"\"{field}\"" : field;

    private static string BuildCsv(params string[] rows) => string.Join("\n", rows);

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));
}
