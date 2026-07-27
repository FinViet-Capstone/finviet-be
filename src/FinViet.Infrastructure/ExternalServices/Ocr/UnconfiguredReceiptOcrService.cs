using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.Ocr;

/// <summary>
/// Placeholder IReceiptOcrService until a real provider (Google Cloud Vision / Azure AI Vision /
/// other) is chosen and wired in. Always throws IntegrationUnavailableException — same "not
/// configured" pattern as SepayWalletService's OAuth checks — so the photo-extraction endpoint
/// fails predictably (503) instead of silently returning empty results.
///
/// To go live: add Infrastructure/ExternalServices/{Provider}/ implementing IReceiptOcrService,
/// swap the DI registration in DependencyInjection.cs for it, and set Ocr:Provider/Ocr:ApiKey via
/// user-secrets or an env var. No controller or interface changes needed.
/// </summary>
public sealed class UnconfiguredReceiptOcrService : IReceiptOcrService
{
    private readonly OcrOptions _options;

    public UnconfiguredReceiptOcrService(IOptions<OcrOptions> options)
    {
        _options = options.Value;
    }

    public Task<ExtractedTransactionItem?> ExtractAsync(
        Stream imageStream, string contentType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Provider))
        {
            throw new IntegrationUnavailableException(
                "Receipt OCR is not configured on this server.",
                "ocr_not_configured");
        }

        throw new IntegrationUnavailableException(
            $"OCR provider '{_options.Provider}' has no implementation registered yet.",
            "ocr_not_configured");
    }
}
