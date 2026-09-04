using System.Globalization;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.ExternalServices.Gemini;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenAiType = Google.GenAI.Types.Type;

namespace FinViet.Infrastructure.ExternalServices.Ocr;

/// <summary>Reads a receipt photo with the Gemini multimodal model already wired up for FinViet's
/// other AI features (score/report/chat) — no separate OCR vendor/credentials needed. Vision
/// accuracy is a general LLM reading an image rather than a purpose-built document-extraction
/// model, but the extract flow already treats every row as a preview the customer confirms, so a
/// wrong read is corrected on the review screen rather than silently trusted.</summary>
public sealed class GeminiReceiptOcrService : IReceiptOcrService
{
    private const string SystemInstruction =
        "Bạn là bộ trích xuất hóa đơn của FinViet. Hãy quan sát toàn bộ ảnh ở mọi hướng xoay trước " +
        "khi kết luận. Chấp nhận hóa đơn bán lẻ, hóa đơn VAT, biên lai thanh toán và phiếu tính " +
        "tiền có bố cục giao dịch đọc được. Chỉ đọc dữ liệu xuất hiện trên ảnh, không suy đoán hay " +
        "bịa số liệu. isReceipt=true khi ảnh có bằng chứng rõ ràng về một giao dịch, kể cả hóa đơn " +
        "không có logo hoặc bị xoay. Chỉ trả isReceipt=false khi ảnh không phải chứng từ giao dịch " +
        "hoặc không thể đọc được cả tổng tiền.";

    private static readonly Schema ReceiptSchema = new()
    {
        Type = GenAiType.Object,
        Properties = new Dictionary<string, Schema>
        {
            ["isReceipt"] = new()
            {
                Type = GenAiType.Boolean,
                Description = "true nếu ảnh là một hóa đơn/biên lai mua hàng đọc được, ngược lại false."
            },
            ["amount"] = new()
            {
                Type = GenAiType.Number,
                Description = "Số tiền cuối cùng khách phải thanh toán, ưu tiên nhãn Tổng cộng, " +
                    "Tổng thanh toán, Thành tiền, Phải trả hoặc Total. Không dùng giá từng món, " +
                    "tiền khách đưa, tiền thừa hay điểm tích lũy. Đơn vị VND; số dương không có " +
                    "ký hiệu tiền tệ hay dấu phân cách."
            },
            ["merchant"] = new()
            {
                Type = GenAiType.String,
                Description = "Tên cửa hàng hoặc người bán trên hóa đơn."
            },
            ["description"] = new()
            {
                Type = GenAiType.String,
                Description = "Mô tả ngắn gọn nội dung mua hàng (ví dụ loại mặt hàng chính)."
            },
            ["transactionDate"] = new()
            {
                Type = GenAiType.String,
                Description = "Ngày trên hóa đơn theo định dạng yyyy-MM-dd. Để trống nếu không đọc được."
            },
            ["amountConfidence"] = ConfidenceSchema(
                "Mức chắc chắn 0-1 rằng amount là tổng tiền cuối cùng đọc đúng từ ảnh."),
            ["merchantConfidence"] = ConfidenceSchema(
                "Mức chắc chắn 0-1 rằng merchant là đúng tên người bán/cửa hàng trên ảnh."),
            ["transactionDateConfidence"] = ConfidenceSchema(
                "Mức chắc chắn 0-1 rằng transactionDate được đọc đúng từ ảnh; 0 nếu để trống.")
        },
        Required =
        [
            "isReceipt", "amount", "merchant", "description", "transactionDate",
            "amountConfidence", "merchantConfidence", "transactionDateConfidence"
        ],
        PropertyOrdering =
        [
            "isReceipt", "amount", "merchant", "description", "transactionDate",
            "amountConfidence", "merchantConfidence", "transactionDateConfidence"
        ]
    };

    private static Schema ConfidenceSchema(string description) => new()
    {
        Type = GenAiType.Number,
        Description = description,
        Minimum = 0,
        Maximum = 1
    };

    private readonly IGeminiSdkClient _client;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiReceiptOcrService> _logger;

    internal GeminiReceiptOcrService(
        IGeminiSdkClient client,
        IOptions<GeminiOptions> options,
        ILogger<GeminiReceiptOcrService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExtractedTransactionItem?> ExtractAsync(
        Stream imageStream, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer, cancellationToken);

        var content = new Content
        {
            Role = "user",
            Parts =
            [
                Part.FromBytes(buffer.ToArray(), contentType),
                Part.FromText(
                    "Đọc ảnh hóa đơn/biên lai này. Tìm tên người bán, ngày giao dịch và số tiền " +
                    "cuối cùng phải trả. Kiểm tra các nhãn Tổng cộng/Tổng thanh toán/Thành tiền/" +
                    "Phải trả/Total; phân biệt chúng với tiền khách đưa và tiền thừa. Trả đúng JSON " +
                    "theo schema kèm độ tin cậy riêng cho từng trường.")
            ]
        };

        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content { Parts = [new Part { Text = SystemInstruction }] },
            Temperature = 0.1,
            MaxOutputTokens = 512,
            ThinkingConfig = new ThinkingConfig { IncludeThoughts = false },
            ResponseMimeType = "application/json",
            ResponseSchema = ReceiptSchema
        };

        var raw = await GenerateAsync(content, config, cancellationToken);
        return ParseReceipt(raw);
    }

    private async Task<string> GenerateAsync(
        Content content, GenerateContentConfig config, CancellationToken cancellationToken)
    {
        if (!_options.TryGetGenerationModels(out var models))
            throw new ExternalServiceException(
                "Gemini receipt OCR has no valid generation model configuration.", "ocr_provider_error");

        for (var index = 0; index < models.Length; index++)
        {
            var model = models[index];
            try
            {
                var result = await _client.GenerateContentAsync(model, content, config, cancellationToken);
                if (!string.IsNullOrWhiteSpace(result.Text))
                    return result.Text.Trim();

                _logger.LogWarning("Gemini receipt OCR model {Model} returned an empty response.", model);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ClientError ex) when (ex.StatusCode == 429 && index < models.Length - 1)
            {
                _logger.LogWarning(
                    "Gemini receipt OCR model {Model} rate-limited (429); falling back.", model);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini receipt OCR model {Model} failed.", model);
                throw new ExternalServiceException("Gemini receipt OCR request failed.", "ocr_provider_error", ex);
            }
        }

        throw new ExternalServiceException(
            "Gemini receipt OCR quota is temporarily exhausted across all configured models.",
            "ocr_provider_error");
    }

    internal static ExtractedTransactionItem? ParseReceipt(string raw)
    {
        try
        {
            var json = ExtractJsonObject(raw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isReceipt = root.TryGetProperty("isReceipt", out var isReceiptEl)
                && isReceiptEl.ValueKind == JsonValueKind.True;
            if (!isReceipt)
                return null;

            var amount = root.TryGetProperty("amount", out var amountEl) && amountEl.ValueKind == JsonValueKind.Number
                ? amountEl.GetDecimal()
                : 0m;
            if (amount <= 0)
                return null;

            var merchant = GetString(root, "merchant");
            var description = GetString(root, "description");
            var dateText = GetString(root, "transactionDate");
            var hasParsedDate = DateTime.TryParse(
                dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate);
            var transactionDate = hasParsedDate ? parsedDate : DateTime.UtcNow;
            var amountConfidence = GetConfidence(root, "amountConfidence");
            var merchantConfidence = GetConfidence(root, "merchantConfidence");
            var transactionDateConfidence = hasParsedDate
                ? GetConfidence(root, "transactionDateConfidence")
                : 0m;

            return new ExtractedTransactionItem
            {
                Amount = amount,
                Type = "EXPENSE",
                Merchant = merchant,
                Description = description ?? merchant,
                TransactionDate = transactionDate,
                AmountConfidence = amountConfidence,
                MerchantConfidence = merchantConfidence,
                TransactionDateConfidence = transactionDateConfidence
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new ExternalServiceException(
                "Gemini returned an unparseable receipt OCR response.", "ocr_provider_error", ex);
        }
    }

    private static string? GetString(JsonElement root, string property)
        => root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static decimal GetConfidence(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element))
            return 0m;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var numericValue))
            return Math.Clamp(numericValue, 0m, 1m);

        if (element.ValueKind == JsonValueKind.String
            && decimal.TryParse(element.GetString(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var stringValue))
            return Math.Clamp(stringValue, 0m, 1m);

        return 0m;
    }

    private static string ExtractJsonObject(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && closingFence > firstLineEnd)
                trimmed = trimmed[(firstLineEnd + 1)..closingFence].Trim();
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        return objectStart >= 0 && objectEnd >= objectStart
            ? trimmed[objectStart..(objectEnd + 1)]
            : trimmed;
    }
}
