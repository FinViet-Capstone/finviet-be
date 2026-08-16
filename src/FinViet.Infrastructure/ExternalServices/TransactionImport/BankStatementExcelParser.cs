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

        var result = new ParseResult();
        foreach (DataTable sheet in dataSet.Tables)
        {
            for (var i = 0; i < sheet.Rows.Count; i++)
            {
                var cells = sheet.Rows[i].ItemArray.Select(value => value?.ToString()).ToArray();
                BankStatementRowParser.ParseRow(cells, result);
            }
        }

        return result;
    }

    private static ParseResult ParseCsv(Stream fileStream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var result = new ParseResult();
        using var streamReader = new StreamReader(fileStream, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(streamReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            MissingFieldFound = null,
            BadDataFound = null
        });

        while (csv.Read())
        {
            var cells = new string?[csv.Parser.Count];
            for (var i = 0; i < cells.Length; i++)
                cells[i] = csv[i];

            BankStatementRowParser.ParseRow(cells, result);
        }

        return result;
    }
}
