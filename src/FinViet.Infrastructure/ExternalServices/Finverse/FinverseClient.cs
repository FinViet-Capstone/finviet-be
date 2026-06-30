using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.Finverse;

/// <summary>
/// REST wrapper over the Finverse Data API. Finverse JSON is snake_case, mapped via
/// JsonNamingPolicy.SnakeCaseLower. The backend client-credentials token is cached; per-user
/// login-identity tokens are passed in by the caller (held encrypted on wallet_links).
/// </summary>
public class FinverseClient : IFinverseClient
{
    private const string CustomerTokenCacheKey = "finverse:customer_token";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly FinverseOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FinverseClient> _logger;

    public FinverseClient(
        HttpClient http,
        IOptions<FinverseOptions> options,
        IMemoryCache cache,
        ILogger<FinverseClient> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<FinverseTokenResponse> GetCustomerTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CustomerTokenCacheKey, out FinverseTokenResponse? cached) && cached is not null)
            return cached;

        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new BusinessRuleException("Finverse chưa được cấu hình (client id/secret).", "finverse_not_configured");

        var token = await PostJsonAsync<FinverseTokenResponse>(
            "auth/customer/token",
            new
            {
                client_id = _options.ClientId,
                client_secret = _options.ClientSecret,
                grant_type = "client_credentials",
            },
            bearer: null,
            cancellationToken);

        // Refresh a minute early to avoid edge-of-expiry failures.
        var ttl = TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 60));
        _cache.Set(CustomerTokenCacheKey, token, ttl);
        return token;
    }

    public Task<FinverseTokenResponse> CreateLinkAsync(
        string customerToken, string userId, string state, CancellationToken cancellationToken = default)
        => PostJsonAsync<FinverseTokenResponse>(
            "link/token",
            new
            {
                client_id = _options.ClientId,
                user_id = userId,
                redirect_uri = _options.RedirectUri,
                state,
                grant_type = "client_credentials",
                response_mode = "query",        // mobile WebView reads ?code= from the redirect
                response_type = "code",
                // Auto-redirect to redirect_uri after success so the user doesn't have to
                // tap "Continue" inside the WebView (that button sat under the home indicator).
                ui_mode = "auto_redirect",
                link_mode = _options.LinkMode,
                language = "vi",
            },
            bearer: customerToken,
            cancellationToken);

    public async Task<FinverseExchangeResponse> ExchangeCodeAsync(
        string customerToken, string code, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["grant_type"] = "authorization_code",
        });
        return await SendAsync<FinverseExchangeResponse>(
            HttpMethod.Post, "auth/token", customerToken, form, cancellationToken);
    }

    public async Task<FinverseExchangeResponse> RefreshAsync(
        string customerToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        return await SendAsync<FinverseExchangeResponse>(
            HttpMethod.Post, "auth/token/refresh", customerToken, form, cancellationToken);
    }

    public Task<FinverseAccountsResponse> GetAccountsAsync(
        string loginIdentityToken, CancellationToken cancellationToken = default)
        => SendAsync<FinverseAccountsResponse>(
            HttpMethod.Get, "accounts", loginIdentityToken, content: null, cancellationToken);

    public Task<FinverseTransactionsResponse> GetTransactionsAsync(
        string loginIdentityToken, string accountId, int offset = 0, int limit = 500,
        CancellationToken cancellationToken = default)
        => SendAsync<FinverseTransactionsResponse>(
            HttpMethod.Get,
            $"transactions/{Uri.EscapeDataString(accountId)}?offset={offset}&limit={limit}",
            loginIdentityToken, content: null, cancellationToken);

    // ── transport helpers ─────────────────────────────────────────────────────────

    private Task<T> PostJsonAsync<T>(string path, object body, string? bearer, CancellationToken ct)
        => SendAsync<T>(HttpMethod.Post, path, bearer, JsonContent.Create(body, options: JsonOpts), ct);

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, string? bearer, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (!string.IsNullOrWhiteSpace(bearer))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Finverse request to {Path} failed at transport level.", path);
            throw new BusinessRuleException("Không kết nối được tới Finverse. Vui lòng thử lại sau.", "finverse_unavailable");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Finverse {Path} returned {Status}: {Body}", path, (int)response.StatusCode, errBody);
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new BusinessRuleException("Phiên liên kết Finverse không hợp lệ hoặc đã hết hạn.", "finverse_unauthorized"),
                HttpStatusCode.TooManyRequests =>
                    new BusinessRuleException("Finverse đang giới hạn tần suất. Vui lòng thử lại sau.", "finverse_rate_limited"),
                _ => new BusinessRuleException("Finverse gặp lỗi. Vui lòng thử lại sau.", "finverse_error"),
            };
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct);
        if (result is null)
            throw new BusinessRuleException("Finverse trả về dữ liệu không hợp lệ.", "finverse_bad_payload");

        return result;
    }
}
