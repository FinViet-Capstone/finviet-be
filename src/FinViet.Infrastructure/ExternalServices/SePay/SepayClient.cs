using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    public async Task<SepayTokenResponse> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = _options.RedirectUri
        });

        var response = await _http.PostAsync("/oauth/token", content, cancellationToken);
        return await ReadResponseAsync<SepayTokenResponse>(response, "token exchange", cancellationToken);
    }

    public async Task<SepayTokenResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret
        });

        var response = await _http.PostAsync("/oauth/token", content, cancellationToken);
        return await ReadResponseAsync<SepayTokenResponse>(response, "token refresh", cancellationToken);
    }

    // ── Resource endpoints ──────────────────────────────────────────────────────

    public async Task<SepayUser> GetMeAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(HttpMethod.Get, "/api/v1/me", accessToken);
        var response = await _http.SendAsync(request, cancellationToken);
        var envelope = await ReadResponseAsync<SepayUserResponse>(response, "get user", cancellationToken);
        return envelope.Data ?? throw new ExternalServiceException("SePay returned empty user data.", "sepay_empty_user");
    }

    public async Task<List<SepayBankAccount>> GetBankAccountsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(HttpMethod.Get, "/api/v1/bank-accounts", accessToken);
        var response = await _http.SendAsync(request, cancellationToken);
        var envelope = await ReadResponseAsync<SepayBankAccountListResponse>(response, "get bank accounts", cancellationToken);
        return envelope.Data;
    }

    public async Task<SepayBankAccount> GetBankAccountAsync(
        string accessToken,
        int bankAccountId,
        CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(HttpMethod.Get, $"/api/v1/bank-accounts/{bankAccountId}", accessToken);
        var response = await _http.SendAsync(request, cancellationToken);

        // SePay returns { status, data: { ... } } for single resources.
        // Reuse the list response envelope — single objects return data directly.
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
        if (!string.IsNullOrWhiteSpace(fromDate)) query += $"&from_date={fromDate}";
        if (!string.IsNullOrWhiteSpace(toDate)) query += $"&to_date={toDate}";

        var request = CreateRequest(HttpMethod.Get, $"/api/v1/transactions?{query}", accessToken);
        var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<SepayTransactionListResponse>(response, "get transactions", cancellationToken);
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
        if (!string.IsNullOrWhiteSpace(accountNumber)) query += $"&account_number={accountNumber}";
        if (!string.IsNullOrWhiteSpace(sinceDate)) query += $"&transaction_date_min={sinceDate}";

        // The static User API lives under /userapi (not /api/v1) on the same host.
        var request = CreateRequest(HttpMethod.Get, $"/userapi/transactions/list?{query}", apiToken);
        var response = await _http.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<SepayUserApiListResponse>(response, "userapi transactions", cancellationToken);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            _logger.LogWarning("SePay:ClientId is not configured. SePay integration will not work.");
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            _logger.LogWarning("SePay:ClientSecret is not configured. SePay integration will not work.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
        return request;
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
