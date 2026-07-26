using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.SePay;

internal sealed class SepayClient : ISepayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly SepayOptions _options;
    private readonly ILogger<SepayClient> _logger;

    public SepayClient(
        HttpClient httpClient,
        IOptions<SepayOptions> options,
        ILogger<SepayClient> logger)
    {
        _http = httpClient;
        _options = options.Value;
        _logger = logger;
        EnsureConfigured();
    }

    // ── OAuth token exchange ────────────────────────────────────────────────────

    public Task<SepayTokenResponse> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
        => PostFormAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = _options.RedirectUri
            },
            "token exchange",
            cancellationToken);

    public Task<SepayTokenResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
        => PostFormAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            },
            "token refresh",
            cancellationToken);

    private async Task<SepayTokenResponse> PostFormAsync(
        Dictionary<string, string> form,
        string operation,
        CancellationToken cancellationToken)
    {
        // The token endpoint is form-urlencoded per the OAuth2 spec, unlike the JSON resource API.
        var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
            {
                Content = new FormUrlEncodedContent(form)
            },
            operation,
            cancellationToken);
        return await ReadResponseAsync<SepayTokenResponse>(response, operation, cancellationToken);
    }

    // ── Resource endpoints ──────────────────────────────────────────────────────

    public async Task<SepayUser> GetMeAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, "/api/v1/me", accessToken), "get user", cancellationToken);
        var envelope = await ReadResponseAsync<SepayUserResponse>(response, "get user", cancellationToken);
        return envelope.Data ?? throw new ExternalServiceException("SePay returned empty user data.", "sepay_empty_user");
    }

    public async Task<List<SepayBankAccount>> GetBankAccountsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, "/api/v1/bank-accounts", accessToken),
            "get bank accounts",
            cancellationToken);
        var envelope = await ReadResponseAsync<SepayBankAccountListResponse>(response, "get bank accounts", cancellationToken);
        return envelope.Data;
    }

    public async Task<SepayBankAccount> GetBankAccountAsync(
        string accessToken,
        int bankAccountId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, $"/api/v1/bank-accounts/{bankAccountId}", accessToken),
            "get bank account",
            cancellationToken);

        // SePay returns { status, data: { ... } } for single resources — unwrap `data` by hand
        // because the list envelope types `data` as an array.
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SePay get bank account {Id} failed: {Status} {Body}",
                bankAccountId, (int)response.StatusCode, Truncate(body));
            throw MapSepayError(response, body, "get bank account");
        }

        using var doc = JsonDocument.Parse(body);
        var dataElement = doc.RootElement.GetProperty("data");
        return JsonSerializer.Deserialize<SepayBankAccount>(dataElement.GetRawText(), JsonOptions)
            ?? throw new ExternalServiceException("SePay returned empty bank account.", "sepay_empty_account");
    }

    public async Task<SepayTransactionListResponse> GetTransactionsAsync(
        string accessToken,
        int bankAccountId,
        int page = 1,
        int limit = 100,
        string? fromDate = null,
        string? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"bank_account_id={bankAccountId}&page={page}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(fromDate)) query += $"&from_date={Uri.EscapeDataString(fromDate)}";
        if (!string.IsNullOrWhiteSpace(toDate)) query += $"&to_date={Uri.EscapeDataString(toDate)}";

        var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, $"/api/v1/transactions?{query}", accessToken),
            "get transactions",
            cancellationToken);
        return await ReadResponseAsync<SepayTransactionListResponse>(response, "get transactions", cancellationToken);
    }

    // ── Webhook management ──────────────────────────────────────────────────────

    public async Task<List<SepayWebhookInfo>> GetWebhooksAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, "/api/v1/webhooks", accessToken),
            "list webhooks",
            cancellationToken);
        var envelope = await ReadResponseAsync<SepayWebhookListResponse>(response, "list webhooks", cancellationToken);
        return envelope.Data;
    }

    public async Task<int> CreateWebhookAsync(
        string accessToken,
        SepayWebhookCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        // Serialized once so a retry re-sends exactly the same body.
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        var response = await SendWithRetryAsync(
            () =>
            {
                var message = CreateRequest(HttpMethod.Post, "/api/v1/webhooks", accessToken);
                message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                return message;
            },
            "create webhook",
            cancellationToken);

        var envelope = await ReadResponseAsync<SepayWebhookCreateResponse>(response, "create webhook", cancellationToken);
        if (envelope.Data is null || envelope.Data.Id <= 0)
        {
            throw new ExternalServiceException(
                "SePay did not return the id of the created webhook.",
                "sepay_webhook_id_missing");
        }

        return envelope.Data.Id;
    }

    public async Task DeleteWebhookAsync(
        string accessToken,
        int webhookId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Delete, $"/api/v1/webhooks/{webhookId}", accessToken),
            "delete webhook",
            cancellationToken);

        // A webhook the user already removed in the SePay dashboard is not an error for us —
        // the desired end state (it is gone) already holds.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("SePay webhook {WebhookId} was already gone.", webhookId);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("SePay delete webhook {WebhookId} failed: {Status} {Body}",
                webhookId, (int)response.StatusCode, Truncate(body));
            throw MapSepayError(response, body, "delete webhook");
        }
    }

    // ── Static User API (personal token) ────────────────────────────────────────

    public async Task<SepayUserApiListResponse> GetUserApiTransactionsAsync(
        string apiToken,
        int limit = 5000,
        string? accountNumber = null,
        string? sinceDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"limit={limit}";
        if (!string.IsNullOrWhiteSpace(accountNumber)) query += $"&account_number={Uri.EscapeDataString(accountNumber)}";
        if (!string.IsNullOrWhiteSpace(sinceDate)) query += $"&transaction_date_min={Uri.EscapeDataString(sinceDate)}";

        // The static User API lives under /userapi (not /api/v1) on the same host.
        var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, $"/userapi/transactions/list?{query}", apiToken),
            "userapi transactions",
            cancellationToken);
        return await ReadResponseAsync<SepayUserApiListResponse>(response, "userapi transactions", cancellationToken);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            _logger.LogWarning("SePay:ClientId is not configured. The SePay OAuth flow will not work.");
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            _logger.LogWarning("SePay:ClientSecret is not configured. The SePay OAuth flow will not work.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
        return request;
    }

    /// <summary>
    /// Sends a request, retrying rate limits, 5xx and network faults with exponential backoff.
    /// SePay throttles per client, and a sync fans out over many pages, so a single 429 should
    /// not abort the whole run. The request is rebuilt each attempt because
    /// <see cref="HttpRequestMessage"/> cannot be sent twice.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        string operation,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Clamp(_options.MaxRetries, 0, 5);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(requestFactory(), cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries
                                       && ex is HttpRequestException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "SePay {Operation} attempt {Attempt} failed; retrying.", operation, attempt + 1);
                await Task.Delay(BackoffDelay(attempt), cancellationToken);
                continue;
            }

            if (attempt >= maxRetries || !IsTransient(response.StatusCode))
                return response;

            var delay = RetryAfter(response) ?? BackoffDelay(attempt);
            _logger.LogWarning("SePay {Operation} returned {Status}; retrying in {Delay}.",
                operation, (int)response.StatusCode, delay);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status)
        => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan BackoffDelay(int attempt) => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                return wait;
        }

        return null;
    }

    private async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SePay {Operation} failed: {Status} {Body}",
                operation, (int)response.StatusCode, Truncate(body));
            throw MapSepayError(response, body, operation);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new ExternalServiceException(
                    $"SePay {operation} returned null.", $"sepay_{operation}_null");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SePay {Operation} returned unparseable JSON: {Body}",
                operation, Truncate(body));
            throw new ExternalServiceException(
                $"SePay returned an unexpected response format during {operation}.",
                "sepay_parse_error");
        }
    }

    private static ExternalServiceException MapSepayError(
        HttpResponseMessage response,
        string body,
        string operation)
    {
        var statusCode = (int)response.StatusCode;
        var errorCode = statusCode switch
        {
            401 => "sepay_unauthorized",
            403 => "sepay_forbidden",
            404 => "sepay_not_found",
            400 => "sepay_validation_error",
            429 => "sepay_rate_limited",
            _ => $"sepay_error_{statusCode}"
        };

        return new ExternalServiceException(
            $"SePay {operation} failed with HTTP {statusCode}: {Truncate(body)}",
            errorCode);
    }

    private static string Truncate(string? value, int maxLength = 500)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
