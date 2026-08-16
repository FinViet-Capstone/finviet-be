using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

/// <summary>Extracts candidate transactions from SMS text / bank statement files and
/// AI-suggests categories. Does not persist anything (review-before-import flow).</summary>
public interface ITransactionExtractService
{
    Task<ExtractResponse> ExtractSmsAsync(Guid customerId, string text, CancellationToken cancellationToken = default);

    Task<ExtractResponse> ExtractCsvAsync(Guid customerId, Stream fileStream, string fileExtension, int? maxRows, CancellationToken cancellationToken = default);
}
