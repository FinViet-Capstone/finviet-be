using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface IBankStatementParser
{
    ParseResult Parse(Stream fileStream, int? maxRows = null);
}

public interface ISmsTransactionParser
{
    ParseResult Parse(string content);
}
