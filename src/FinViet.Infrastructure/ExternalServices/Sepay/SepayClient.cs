using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinViet.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.ExternalServices.Sepay;

/// <summary>
/// REST wrapper over the SePay v2 API. Authenticates per-call with the caller's SePay token
/// (Bearer). Maps transport/status failures to the app's exception types so callers get clear errors.
/// </summary>
public class SepayClient : ISepayClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SepayOptions _options;
    private readonly ILogger<SepayClient> _logger;

    public SepayClient(HttpClient http, IOptions<SepayOptions> options, ILogger<SepayClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public Task<SepayListResponse<SepayTransaction>> GetTransactionsAsync(
        string apiToken,
        string? sinceId = null,
        int perPage = 100,
        string sort = "asc",
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"per_page={perPage}",
            $"transaction_date_sort={sort}",
            $"page={page}"
        };
        if (!string.IsNullOrWhiteSpace(sinceId))
            query.Add($"since_id={Uri.EscapeDataString(sinceId)}");

        return GetAsync<SepayListResponse<SepayTransaction>>(
            apiToken, $"v2/transactions?{string.Join("&", query)}", cancellationToken);
    }

    public Task<SepayListResponse<SepayBankAccount>> GetBankAccountsAsync(
        string apiToken,
        CancellationToken cancellationToken = default)
        => GetAsync<SepayListResponse<SepayBankAccount>>(apiToken, "v2/bank-accounts", cancellationToken);

    private async Task<T> GetAsync<T>(string apiToken, string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
            throw new BadRequestException("SePay API token is required.");

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "SePay request to {Path} failed at transport level.", path);
            throw new BusinessRuleException("Không kết nối được tới SePay. Vui lòng thử lại sau.", "sepay_unavailable");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("SePay {Path} returned {Status}: {Body}", path, (int)response.StatusCode, body);

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new BusinessRuleException(
                    "Token SePay không hợp lệ hoặc đã hết hạn.", "sepay_unauthorized"),
                HttpStatusCode.NotFound => new NotFoundException("SePay resource not found."),
                HttpStatusCode.TooManyRequests => new BusinessRuleException(
                    "SePay đang giới hạn tần suất. Vui lòng thử lại sau.", "sepay_rate_limited"),
                _ => new BusinessRuleException("SePay gặp lỗi. Vui lòng thử lại sau.", "sepay_error")
            };
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOpts, cancellationToken);
        if (result is null)
            throw new BusinessRuleException("SePay trả về dữ liệu không hợp lệ.", "sepay_bad_payload");

        return result;
    }
}
