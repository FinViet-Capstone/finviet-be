using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

public interface ITransactionImportRepository
{
    Task<ImportTransactionsResponseDto> SaveImportedTransactionsAsync(
        Guid walletId,
        Guid customerId,
        string fileName,
        string sourceChannel,
        ParseResult parseResult,
        CancellationToken cancellationToken = default);
}
