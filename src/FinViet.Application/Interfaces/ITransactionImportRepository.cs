using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface ITransactionImportRepository
{
    Task<ImportTransactionsResponseDto> SaveImportedTransactionsAsync(
        Guid walletId,
        Guid customerId,
        string fileName,
        ParseResult parseResult,
        CancellationToken cancellationToken = default);
}
