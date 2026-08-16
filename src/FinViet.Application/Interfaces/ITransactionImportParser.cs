using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface IBankStatementParser
{
    /// <param name="fileExtension">The uploaded file's extension (e.g. ".csv", ".xlsx"),
    /// including the leading dot — selects which format reader parses the stream.</param>
    ParseResult Parse(Stream fileStream, string fileExtension, int? maxRows = null);
}

public interface ISmsTransactionParser
{
    ParseResult Parse(string content);
}
