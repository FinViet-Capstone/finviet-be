using System.Net;
using System.Text;
using System.Text.Json;
using FinViet.Application.Exceptions;
using FinViet.Infrastructure.ExternalServices.OpenAiCompatible;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinViet.Application.UnitTests;

public class OpenAiCompatibleAiClientTests
{
    [Fact]
    public async Task ClassifyAsync_ValidResponse_UsesJsonModeAndMatchesAllowedCategory()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"choices":[{"message":{"content":"{\"category\":\"Ăn uống\",\"confidence\":1.4}"}}]}"""));
        var client = CreateModelClient(handler);

        var result = await client.ClassifyAsync("Highlands Coffee", ["Ăn uống", "Di chuyển"]);

        Assert.Equal("Ăn uống", result.CategoryName);
        Assert.Equal(1m, result.Confidence);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.RequestUri?.ToString());

        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("qwen3:4b", request.RootElement.GetProperty("model").GetString());
        Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(
            "json_object",
            request.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Contains(
            "Highlands Coffee",
            request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ClassifyAsync_StringConfidence_UsesInvariantJsonNumberFormat()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"choices":[{"message":{"content":"{\"category\":\"Ăn uống\",\"confidence\":\"0.85\"}"}}]}"""));
        var client = CreateModelClient(handler);

        var result = await client.ClassifyAsync("Highlands Coffee", ["Ăn uống"]);

        Assert.Equal("Ăn uống", result.CategoryName);
        Assert.Equal(0.85m, result.Confidence);
    }

    [Fact]
    public async Task ClassifyAsync_FencedDisallowedCategory_ReturnsUnresolved()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"choices":[{"message":{"content":"```json\n{\"category\":\"Không tồn tại\",\"confidence\":0.9}\n```"}}]}"""));
        var client = CreateModelClient(handler);

        var result = await client.ClassifyAsync("mơ hồ", ["Ăn uống"]);

        Assert.Null(result.CategoryName);
        Assert.Equal(0m, result.Confidence);
    }

    [Fact]
    public async Task ChatAsync_WithApiKey_SendsBearerAndExtractsContent()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"choices":[{"message":{"content":"Câu trả lời tiếng Việt"}}]}"""));
        var client = CreateModelClient(handler, apiKey: "local-token");

        var result = await client.ChatAsync("context", [], "question");

        Assert.Equal("Câu trả lời tiếng Việt", result);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("local-token", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task ChatAsync_TransportFailure_ThrowsProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        var client = CreateModelClient(handler);

        await Assert.ThrowsAsync<AiProviderUnavailableException>(
            () => client.ChatAsync("context", [], "question"));
    }

    [Fact]
    public async Task EmbedAsync_ValidResponse_ReturnsConfiguredDimension()
    {
        var values = string.Join(',', Enumerable.Repeat("0.125", 768));
        var handler = new StubHttpMessageHandler(_ => JsonResponse($"{{\"data\":[{{\"embedding\":[{values}]}}]}}"));
        var service = CreateEmbeddingService(handler);

        var result = await service.EmbedAsync("FinViet");

        Assert.Equal(768, result.Length);
        Assert.Equal("http://localhost:11434/v1/embeddings", handler.RequestUri?.ToString());
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("nomic-embed-text", request.RootElement.GetProperty("model").GetString());
        Assert.Equal("FinViet", request.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task EmbedAsync_WrongDimension_ThrowsProviderUnavailable()
    {
        var values = string.Join(',', Enumerable.Repeat("0.125", 767));
        var handler = new StubHttpMessageHandler(_ => JsonResponse($"{{\"data\":[{{\"embedding\":[{values}]}}]}}"));
        var service = CreateEmbeddingService(handler);

        await Assert.ThrowsAsync<AiProviderUnavailableException>(() => service.EmbedAsync("FinViet"));
    }

    [Fact]
    public async Task EmbedAsync_MalformedResponse_ThrowsProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("{\"data\":[]}"));
        var service = CreateEmbeddingService(handler);

        await Assert.ThrowsAsync<AiProviderUnavailableException>(() => service.EmbedAsync("FinViet"));
    }

    private static OpenAiCompatibleAiModelClient CreateModelClient(
        HttpMessageHandler handler,
        string apiKey = "")
    {
        var options = Options.Create(new AiOptions
        {
            BaseUrl = "http://localhost:11434/v1",
            ApiKey = apiKey,
            ClassificationModel = "qwen3:4b",
            GenerationModel = "qwen3:4b"
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/v1/") };
        return new OpenAiCompatibleAiModelClient(
            http,
            options,
            NullLogger<OpenAiCompatibleAiModelClient>.Instance);
    }

    private static OpenAiCompatibleEmbeddingService CreateEmbeddingService(HttpMessageHandler handler)
    {
        var options = Options.Create(new AiOptions
        {
            BaseUrl = "http://localhost:11434/v1",
            EmbeddingModel = "nomic-embed-text",
            EmbeddingDimensions = 768
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/v1/") };
        return new OpenAiCompatibleEmbeddingService(
            http,
            options,
            NullLogger<OpenAiCompatibleEmbeddingService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return _response(request);
        }
    }
}
