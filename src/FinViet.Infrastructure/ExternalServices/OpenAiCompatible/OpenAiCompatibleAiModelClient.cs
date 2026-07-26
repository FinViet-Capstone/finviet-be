using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinViet.Application.DTOs.Ai;
using FinViet.Application.Exceptions;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.OpenAiCompatible;

/// <summary>
/// REST client for an OpenAI-compatible chat-completions endpoint. Ollama exposes this contract
/// locally under /v1. Prompts and defensive classification parsing remain owned by FinViet.
/// </summary>
public class OpenAiCompatibleAiModelClient : IAiModelClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleAiModelClient> _logger;

    public OpenAiCompatibleAiModelClient(
        HttpClient http,
        IOptions<AiOptions> options,
        ILogger<OpenAiCompatibleAiModelClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiClassificationResult> ClassifyAsync(
        string input,
        IReadOnlyList<string> allowedCategories,
        CancellationToken cancellationToken = default)
    {
        var categoryList = string.Join("\n", allowedCategories.Select(c => $"- {c}"));

        var prompt =
            "Bạn là trợ lý phân loại giao dịch tài chính cá nhân của người Việt Nam.\n" +
            "Nhiệm vụ: đọc mô tả giao dịch (có thể chứa chữ viết tắt, tiếng lóng, tên cửa hàng, " +
            "tên người nhận) và chọn ĐÚNG MỘT danh mục phù hợp nhất từ danh sách bên dưới.\n\n" +
            "Hiểu các ví dụ viết tắt/tiếng lóng thường gặp:\n" +
            "- 'Grab', 'Be', 'Gojek', 'xe ôm', 'km' → di chuyển\n" +
            "- 'Nap Lien Quan', 'nạp game', 'Steam', 'Netflix', 'FF' → giải trí\n" +
            "- 'An sang', 'an trua', 'com', 'pho', 'bun', 'tra sua' → ăn uống\n" +
            "- 'tien dien', 'tien nuoc', 'internet', 'wifi' → nhà ở & tiện ích\n\n" +
            $"Danh sách danh mục hợp lệ:\n{categoryList}\n\n" +
            $"Mô tả giao dịch: \"{input}\"\n\n" +
            "Trả lời DUY NHẤT bằng JSON đúng định dạng sau, không thêm chữ nào khác:\n" +
            "{\"category\": \"<tên danh mục chính xác từ danh sách>\", \"confidence\": <số thực 0..1>}";

        var raw = await GenerateAsync(
            _options.ClassificationModel,
            prompt,
            expectJson: true,
            temperature: 0.2,
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(StripCodeFences(raw));
            var root = doc.RootElement;

            var category = root.TryGetProperty("category", out var catEl)
                ? catEl.GetString()
                : null;

            decimal confidence = 0m;
            if (root.TryGetProperty("confidence", out var confEl))
            {
                confidence = confEl.ValueKind == JsonValueKind.Number
                    ? confEl.GetDecimal()
                    : decimal.TryParse(
                        confEl.GetString(),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsed)
                        ? parsed
                        : 0m;
            }

            confidence = Math.Clamp(confidence, 0m, 1m);
            var matched = allowedCategories.FirstOrDefault(
                c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));

            return new AiClassificationResult
            {
                CategoryName = matched,
                Confidence = matched is null ? 0m : confidence
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to parse AI classification response: {Raw}", raw);
            throw new AiProviderUnavailableException(
                "The AI provider returned an unparseable classification response.", ex);
        }
    }

    public Task<string> GenerateScoreCommentAsync(
        string scoreContext,
        CancellationToken cancellationToken = default)
    {
        var prompt =
            "Bạn là cố vấn tài chính cá nhân thân thiện. Dựa trên dữ liệu điểm chi tiêu dưới đây, " +
            "viết MỘT nhận xét ngắn gọn (1-2 câu) bằng tiếng Việt, tích cực và mang tính khích lệ, " +
            "không dùng markdown, không emoji.\n\n" +
            $"Dữ liệu:\n{scoreContext}";

        return GenerateAsync(
            _options.GenerationModel,
            prompt,
            expectJson: false,
            temperature: 0.7,
            cancellationToken);
    }

    public Task<string> GenerateReportAsync(
        string reportContext,
        CancellationToken cancellationToken = default)
    {
        var prompt =
            "Bạn là cố vấn tài chính cá nhân. Viết một báo cáo tài chính tuần bằng tiếng Việt, " +
            "độ dài 150-200 từ, văn phong tự nhiên, thân thiện. Báo cáo cần: (1) tóm tắt tình hình " +
            "chi tiêu tuần qua, (2) nêu rõ các danh mục vượt ngân sách nếu có, (3) đưa ra MỘT mẹo " +
            "tiết kiệm cụ thể và khả thi. Không dùng markdown, không emoji, không tiêu đề.\n\n" +
            $"Dữ liệu tuần:\n{reportContext}";

        return GenerateAsync(
            _options.GenerationModel,
            prompt,
            expectJson: false,
            temperature: 0.7,
            cancellationToken);
    }

    public Task<string> ChatAsync(
        string contextBlock,
        IReadOnlyList<AiChatTurn> recentTurns,
        string question,
        CancellationToken cancellationToken = default)
    {
        var history = new StringBuilder();
        foreach (var turn in recentTurns)
        {
            var who = string.Equals(turn.SenderType, "AI", StringComparison.OrdinalIgnoreCase)
                ? "Trợ lý"
                : "Người dùng";
            history.AppendLine($"{who}: {turn.Content}");
        }

        var prompt =
            "Bạn là trợ lý tài chính cá nhân của ứng dụng FinViet. Trả lời bằng tiếng Việt, ngắn gọn, " +
            "chính xác, chỉ dựa trên dữ liệu tài chính của người dùng được cung cấp bên dưới. Nếu dữ liệu " +
            "không đủ để trả lời, hãy nói rõ điều đó. Không bịa số liệu. Không dùng markdown.\n\n" +
            $"=== DỮ LIỆU TÀI CHÍNH CỦA NGƯỜI DÙNG ===\n{contextBlock}\n\n" +
            (history.Length > 0 ? $"=== LỊCH SỬ HỘI THOẠI GẦN ĐÂY ===\n{history}\n" : "") +
            $"=== CÂU HỎI ===\n{question}";

        return GenerateAsync(
            _options.GenerationModel,
            prompt,
            expectJson: false,
            temperature: 0.7,
            cancellationToken);
    }

    private async Task<string> GenerateAsync(
        string model,
        string prompt,
        bool expectJson,
        double temperature,
        CancellationToken cancellationToken)
    {
        object body = expectJson
            ? new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                stream = false,
                temperature,
                response_format = new { type = "json_object" }
            }
            : new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                stream = false,
                temperature
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };
        OpenAiCompatibleHttp.AddAuthorization(request, _options.ApiKey);

        var payload = await OpenAiCompatibleHttp.SendAsync(
            _http,
            request,
            _logger,
            "chat completion request",
            cancellationToken);
        var text = ExtractText(payload);
        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderUnavailableException("The AI provider returned an empty response.");

        return text;
    }

    private static string? ExtractText(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or
                                   IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string StripCodeFences(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```"))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
            return trimmed.Trim('`').Trim();

        var inner = trimmed[(firstNewline + 1)..];
        var closing = inner.LastIndexOf("```", StringComparison.Ordinal);
        if (closing >= 0)
            inner = inner[..closing];

        return inner.Trim();
    }
}
