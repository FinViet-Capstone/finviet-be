using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

/// <summary>Extracts candidate transactions from SMS text / bank statement files and
/// AI-suggests categories. Does not persist anything (review-before-import flow).</summary>
public interface ITransactionExtractService
{
    Task<ExtractResponse> ExtractSmsAsync(string text, CancellationToken cancellationToken = default);

    Task<ExtractResponse> ExtractCsvAsync(Stream fileStream, int? maxRows, CancellationToken cancellationToken = default);
}
