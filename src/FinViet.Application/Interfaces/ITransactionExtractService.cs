using FinViet.Application.DTOs.Transactions;

namespace FinViet.Application.Interfaces;

/// <summary>Extracts candidate transactions from SMS text / CSV files and AI-suggests categories.
/// Spec §4 POST /extract/sms and /extract/csv. Does not persist anything.</summary>
public interface ITransactionExtractService
{
    Task<ExtractResponse> ExtractSmsAsync(string text, CancellationToken cancellationToken = default);

    Task<ExtractResponse> ExtractCsvAsync(Stream fileStream, int? maxRows, CancellationToken cancellationToken = default);
}
