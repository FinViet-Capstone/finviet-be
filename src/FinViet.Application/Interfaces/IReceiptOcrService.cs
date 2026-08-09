using FinViet.Application.DTOs;

namespace FinViet.Application.Interfaces;

/// <summary>OCRs a receipt photo into a single candidate transaction. No persistence — mirrors
/// ITransactionExtractService's "preview only" contract for SMS/CSV.</summary>
public interface IReceiptOcrService
{
    Task<ExtractedTransactionItem?> ExtractAsync(
        Stream imageStream, string contentType, CancellationToken cancellationToken = default);
}
