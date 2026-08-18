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
        // Correspondent is kept as its own field rather than merged into Note, so a caller can
        // show it separately (e.g. mobile's "Người nhận" field) instead of it being permanently
        // baked into the description text.
        Assert.Equal("Coffee shop", row.Note);
        Assert.Equal("ABC Corp", row.CorrespondentName);
        Assert.Equal(1, result.TotalRowsScanned);
        Assert.Equal(0, result.SkippedDuringParse);
    }

    [Fact]
    public void Parse_Csv_NoCorrespondentColumn_LeavesCorrespondentNameNull()
    {
        var csv = BuildCsv(
            BuildRow(no: "2", date: "1/8/2026 09:30", debit: "1,250,000 VND", credit: "", description: "Grocery", correspondent: ""));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal("Grocery", row.Note);
        Assert.Null(row.CorrespondentName);
    }

    [Theory]
    [InlineData("Ten doi tac")]
    [InlineData("Nguoi thu huong")]
    [InlineData("Doi tuong giao dich")]
    [InlineData("Ten doi tac giao dich")]
    public void Parse_Csv_RecognizesCorrespondentColumnAcrossBankHeaderVariants(string correspondentHeader)
    {
        // Different banks phrase this column differently — matched by substring, not exact
        // equality, so headers not literally in the alias list (e.g. "Ten doi tac giao dich")
        // still resolve via the "doi tac" substring.
        var csv = $"Ngay,Mo ta,So tien,{correspondentHeader}\n12/06/2026,Chuyen khoan,-45000,Circle K\n";
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal("Chuyen khoan", row.Note);
        Assert.Equal("Circle K", row.CorrespondentName);
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

    [Fact]
    public void Parse_Csv_SimpleThreeColumnHeaderVietnamese_ParsesSignedAmounts()
    {
        var csv = "Ngay,Mo ta,So tien\n12/06/2026,Grab 4.7km,-45000\n12/06/2026,Tien luong,2500000\n";
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("EXPENSE", result.Rows[0].TransactionType);
        Assert.Equal(45_000m, result.Rows[0].Amount);
        Assert.Equal("Grab 4.7km", result.Rows[0].Note);
        Assert.Equal("INCOME", result.Rows[1].TransactionType);
        Assert.Equal(2_500_000m, result.Rows[1].Amount);
        Assert.Equal(2, result.TotalRowsScanned);
        Assert.Empty(result.ParseErrors);
    }

    [Fact]
    public void Parse_Csv_SimpleThreeColumnHeaderEnglish_ParsesRows()
    {
        var csv = "Date,Description,Amount\n2026-08-15,Coffee,-65000\n";
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal("EXPENSE", row.TransactionType);
        Assert.Equal(65_000m, row.Amount);
        Assert.Equal(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), row.TransactionDate);
    }

    [Fact]
    public void Parse_Csv_SemicolonDelimited_ParsesRows()
    {
        var csv = "Ngay;Mo ta;So tien\n12/06/2026;Grab 4.7km;-45000\n";
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal("EXPENSE", row.TransactionType);
        Assert.Equal(45_000m, row.Amount);
    }

    [Fact]
    public void Parse_Csv_VietnameseLocaleAmountFormat_ParsesCorrectMagnitude()
    {
        var csv = "Ngay,Mo ta,So tien\n12/06/2026,Big purchase,\"-1.250.000,50\"\n";
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal(1_250_000.50m, row.Amount);
    }

    [Fact]
    public void Parse_Csv_NoRecognizableHeader_FallsBackToLegacyPositionalLayout()
    {
        var csv = BuildCsv(
            BuildRow(no: "1", date: "15/08/2026", debit: "", credit: "50000", description: "Legacy row", correspondent: ""));
        var parser = new BankStatementExcelParser();

        var result = parser.Parse(ToStream(csv), ".csv");

        var row = Assert.Single(result.Rows);
        Assert.Equal("INCOME", row.TransactionType);
        Assert.Equal(50_000m, row.Amount);
        Assert.Equal("Legacy row", row.Note);
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
