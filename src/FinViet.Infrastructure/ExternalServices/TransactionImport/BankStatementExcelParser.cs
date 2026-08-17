using System.Data;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;

namespace FinViet.Infrastructure.ExternalServices.TransactionImport;

/// <summary>Parses a bank-statement export into candidate transactions. Handles both binary
/// Excel (.xlsx/.xls, via <see cref="ExcelDataReader"/>) and plain-text .csv (via
/// <see cref="CsvHelper"/>) — the two readers land on the same row-cell shape, so both delegate
/// to <see cref="BankStatementRowParser"/> for the actual column/business rules.</summary>
public class BankStatementExcelParser : IBankStatementParser
{
    public ParseResult Parse(Stream fileStream, string fileExtension, int? maxRows = null)
    {
        var result = string.Equals(fileExtension, ".csv", StringComparison.OrdinalIgnoreCase)
            ? ParseCsv(fileStream)
            : ParseExcel(fileStream);

        if (maxRows.HasValue && maxRows.Value > 0 && result.Rows.Count > maxRows.Value)
            result.Rows = result.Rows.Take(maxRows.Value).ToList();

        return result;
    }

    private static ParseResult ParseExcel(Stream fileStream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var reader = ExcelReaderFactory.CreateReader(fileStream);
        var dataSet = reader.AsDataSet();

        var rows = new List<IReadOnlyList<string?>>();
        foreach (DataTable sheet in dataSet.Tables)
        {
            for (var i = 0; i < sheet.Rows.Count; i++)
                rows.Add(sheet.Rows[i].ItemArray.Select(value => value?.ToString()).ToArray());
        }

        var result = new ParseResult();
        BankStatementRowParser.ParseRows(rows, result);
        return result;
    }

    private static ParseResult ParseCsv(Stream fileStream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var delimiter = SniffDelimiter(fileStream);

        var result = new ParseResult();
        using var streamReader = new StreamReader(fileStream, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(streamReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            MissingFieldFound = null,
            BadDataFound = null,
            Delimiter = delimiter
        });

        var rows = new List<IReadOnlyList<string?>>();
        while (csv.Read())
        {
            var cells = new string?[csv.Parser.Count];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = csv[i];

            rows.Add(cells);
        }

        BankStatementRowParser.ParseRows(rows, result);
        return result;
    }

    /// <summary>Picks whichever of comma/semicolon/tab occurs most often on the first non-blank
    /// line, defaulting to comma. Real-world exports vary by locale/spreadsheet tool (e.g.
    /// semicolon-delimited from Excel under a Vietnamese/EU locale) — CsvHelper otherwise only
    /// ever splits on comma, silently mis-parsing everything into a single column.</summary>
    private static string SniffDelimiter(Stream fileStream)
    {
        const string defaultDelimiter = ",";
        if (!fileStream.CanSeek)
            return defaultDelimiter;

        var startPosition = fileStream.Position;
        string? firstLine;
        using (var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            firstLine = reader.ReadLine();

        fileStream.Position = startPosition;

        if (string.IsNullOrEmpty(firstLine))
            return defaultDelimiter;

        var candidates = new[] { ",", ";", "\t" };
        var best = defaultDelimiter;
        var bestCount = 0;
        foreach (var candidate in candidates)
        {
            var count = firstLine.Count(c => c == candidate[0]);
            if (count > bestCount)
            {
                bestCount = count;
                best = candidate;
            }
        }

        return best;
    }
}
