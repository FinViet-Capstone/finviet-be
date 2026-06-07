using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface IBankStatementParser
{
    List<ParsedTransactionDto> Parse(Stream fileStream, int? maxRows = null);
}

public interface ISmsTransactionParser
{
    List<ParsedTransactionDto> Parse(string content);
}
