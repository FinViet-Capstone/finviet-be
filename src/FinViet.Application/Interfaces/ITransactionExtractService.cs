using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

/// <summary>Extracts candidate transactions from SMS text / bank statement files and
/// AI-suggests categories. Does not persist anything (review-before-import flow).</summary>
public interface ITransactionExtractService
{
    Task<ExtractResponse> ExtractSmsAsync(Guid customerId, string text, CancellationToken cancellationToken = default);

    Task<ExtractResponse> ExtractCsvAsync(Guid customerId, Stream fileStream, string fileExtension, int? maxRows, CancellationToken cancellationToken = default);

    /// <summary>Applies the same rule-then-AI category suggestion used by SMS/CSV extraction to a
    /// single already-extracted row (in place), for callers that don't go through
    /// <see cref="ExtractSmsAsync"/>/<see cref="ExtractCsvAsync"/> — currently photo/receipt OCR.</summary>
    Task CategorizeItemAsync(Guid customerId, ExtractedTransactionItem item, CancellationToken cancellationToken = default);
}
