using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Wallets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.Finverse;

internal sealed class FinverseClient : IFinverseClient
{
    private const string CustomerTokenCacheKey = "finverse:customer-token";
    private static readonly SemaphoreSlim CustomerTokenLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly FinverseOptions _options;
    private readonly IMemoryCache _cache;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public FinverseClient(
        HttpClient httpClient,
        IOptions<FinverseOptions> options,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
    }

    public async Task<FinverseLinkTokenApiResponse> CreateLinkTokenAsync(
        Guid customerId,
        string state,
        CreateFinverseLinkRequest request,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customerToken = await GetCustomerTokenAsync(cancellationToken);
        var payload = new
        {
            client_id = _options.ClientId,
            user_id = customerId.ToString("D"),
            redirect_uri = _options.RedirectUri,
            state,
            grant_type = "client_credentials",
            response_mode = "form_post",
            response_type = "code",
            institution_id = string.IsNullOrWhiteSpace(request.InstitutionId) ? "" : request.InstitutionId.Trim(),
            language = NormalizeLanguage(request.Language),
            ui_mode = NormalizeUiMode(request.UiMode),
            products_requested = new[] { "ACCOUNTS", "TRANSACTIONS" }
        };

        using var message = CreateJsonRequest(HttpMethod.Post, "link/token", payload, customerToken);
        return await SendAsync<FinverseLinkTokenApiResponse>(message, cancellationToken);
    }

    public async Task<FinverseTokenResponse> ExchangeCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customerToken = await GetCustomerTokenAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, "auth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri,
                ["grant_type"] = "authorization_code"
            })
        };
        AddHeaders(message, customerToken);
        return await SendAsync<FinverseTokenResponse>(message, cancellationToken);
    }

    public async Task<FinverseTokenResponse> RefreshLoginIdentityTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customerToken = await GetCustomerTokenAsync(cancellationToken);
        // Finverse's auth/token/refresh only accepts application/json; posting
        // application/x-www-form-urlencoded here returns HTTP 415 and breaks sync.
        using var message = CreateJsonRequest(HttpMethod.Post, "auth/token/refresh", new
        {
            client_id = _options.ClientId,
            refresh_token = refreshToken,
            grant_type = "refresh_token"
        }, customerToken);
        return await SendAsync<FinverseTokenResponse>(message, cancellationToken);
    }

    public async Task<FinverseAccountsApiResponse> GetAccountsAsync(
        string loginIdentityToken,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "accounts");
        AddHeaders(message, loginIdentityToken);
        return await SendAsync<FinverseAccountsApiResponse>(message, cancellationToken);
    }

    public async Task<FinverseTransactionsApiResponse> GetTransactionsAsync(
        string loginIdentityToken,
        string accountId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var path = $"transactions/{Uri.EscapeDataString(accountId)}?offset={offset}&limit={limit}";
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        AddHeaders(message, loginIdentityToken);
        return await SendAsync<FinverseTransactionsApiResponse>(message, cancellationToken);
    }

    private async Task<string> GetCustomerTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(CustomerTokenCacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
            return cached;

        await CustomerTokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<string>(CustomerTokenCacheKey, out cached) && !string.IsNullOrWhiteSpace(cached))
                return cached;

            using var message = CreateJsonRequest(HttpMethod.Post, "auth/customer/token", new
            {
                client_id = _options.ClientId,
                client_secret = _options.ClientSecret,
                grant_type = "client_credentials"
            });
            var token = await SendAsync<FinverseTokenResponse>(message, cancellationToken);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new ExternalServiceException("Finverse did not return a customer token.", "finverse_token_missing");

            var lifetime = TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 120));
            _cache.Set(CustomerTokenCacheKey, token.AccessToken, lifetime);
            return token.AccessToken;
        }
        finally
        {
            CustomerTokenLock.Release();
        }
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, object payload, string? bearerToken = null)
    {
        var message = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload)
        };
        AddHeaders(message, bearerToken);
        return message;
    }

    private static void AddHeaders(HttpRequestMessage message, string? bearerToken)
    {
        message.Headers.TryAddWithoutValidation("X-Request-Id", Guid.NewGuid().ToString("N"));
        if (!string.IsNullOrWhiteSpace(bearerToken))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var requestId = response.Headers.TryGetValues("X-Request-Id", out var values)
                    ? values.FirstOrDefault()
                    : null;
                throw new ExternalServiceException(
                    $"Finverse returned HTTP {(int)response.StatusCode}." +
                    (string.IsNullOrWhiteSpace(requestId) ? string.Empty : $" Request ID: {requestId}."),
                    "finverse_api_error");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
            return result ?? throw new ExternalServiceException(
                "Finverse returned an empty response.",
                "finverse_empty_response");
        }
        catch (ExternalServiceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalServiceException("Finverse request timed out.", "finverse_timeout");
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceException("Unable to reach Finverse.", "finverse_unreachable", ex);
        }
        catch (JsonException ex)
        {
            throw new ExternalServiceException("Finverse returned an invalid response.", "finverse_invalid_response", ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId)
            || string.IsNullOrWhiteSpace(_options.ClientSecret)
            || string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new IntegrationUnavailableException(
                "Finverse is not configured. Set Finverse:ClientId, Finverse:ClientSecret, and Finverse:RedirectUri.",
                "finverse_not_configured");
        }
    }

    private static string NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private static string NormalizeUiMode(string? uiMode)
        => uiMode?.Trim().ToLowerInvariant() switch
        {
            "iframe" => "iframe",
            "auto_redirect" => "auto_redirect",
            "standalone" => "standalone",
            _ => "redirect"
        };
}
