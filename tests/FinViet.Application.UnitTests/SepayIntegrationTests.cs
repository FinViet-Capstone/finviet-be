using System.Security.Cryptography;
using System.Text.Json;
using FinViet.Infrastructure.ExternalServices.SePay;
using FinViet.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;

namespace FinViet.Application.UnitTests;

public class SepayIntegrationTests
{
    // Must mirror SepayClient.JsonOptions — these tests exist to catch a drift between the wire
    // format and the models without hitting the real API.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void OAuthTransactionList_Deserializes_AmountsAndPagination()
    {
        const string json = """
            {
              "status": "success",
              "data": [
                {
                  "id": 8123456,
                  "bank_account_id": 42,
                  "bank_brand_name": "MBBank",
                  "account_number": "0123456789",
                  "transaction_date": "2026-07-20 19:59:48",
                  "amount_out": 250000,
                  "amount_in": 0,
                  "accumulated": 1750000,
                  "transaction_content": "CHUYEN TIEN AN TRUA",
                  "reference_number": "FT26202xyz"
                }
              ],
              "meta": { "pagination": { "total": 3, "per_page": 100, "current_page": 1, "last_page": 1 } }
            }
            """;

        var response = JsonSerializer.Deserialize<SepayTransactionListResponse>(json, JsonOptions);

        var transaction = Assert.Single(Assert.IsType<SepayTransactionListResponse>(response).Data);
        Assert.Equal(8123456, transaction.Id);
        Assert.Equal(250000m, transaction.AmountOut);
        Assert.Equal(0m, transaction.AmountIn);
        Assert.Equal("CHUYEN TIEN AN TRUA", transaction.TransactionContent);
        Assert.Equal(1, response!.Meta?.Pagination?.LastPage);
    }

    [Fact]
    public void UserApiTransactionList_Deserializes_StringAmountsUnderTransactionsKey()
    {
        // The static User API differs from the OAuth API: the list sits under `transactions`
        // (not `data`) and every numeric field arrives as a string.
        const string json = """
            {
              "status": 200,
              "error": null,
              "transactions": [
                {
                  "id": "9001",
                  "bank_brand_name": "Vietcombank",
                  "account_number": "0011001234567",
                  "transaction_date": "2026-07-21 08:12:03",
                  "amount_out": "0",
                  "amount_in": "1,500,000",
                  "accumulated": "3,250,000",
                  "transaction_content": "LUONG THANG 7"
                }
              ]
            }
            """;

        var response = JsonSerializer.Deserialize<SepayUserApiListResponse>(json, JsonOptions);

        var transaction = Assert.Single(Assert.IsType<SepayUserApiListResponse>(response).Transactions);
        Assert.Equal("9001", transaction.Id);
        Assert.Equal("1,500,000", transaction.AmountIn);
        Assert.Equal("Vietcombank", transaction.BankBrandName);
    }

    [Fact]
    public void BankAccountList_Deserializes_NestedBankInfo()
    {
        const string json = """
            {
              "status": "success",
              "data": [
                {
                  "id": 42,
                  "label": "MB - Chi tieu",
                  "account_holder_name": "NGUYEN VAN A",
                  "account_number": "0123456789",
                  "accumulated": 1750000,
                  "active": true,
                  "bank": { "short_name": "MBBank", "full_name": "Ngan hang Quan Doi", "code": "MB" }
                }
              ]
            }
            """;

        var response = JsonSerializer.Deserialize<SepayBankAccountListResponse>(json, JsonOptions);

        var account = Assert.Single(Assert.IsType<SepayBankAccountListResponse>(response).Data);
        Assert.Equal(42, account.Id);
        Assert.True(account.Active);
        Assert.Equal(1750000m, account.Accumulated);
        Assert.Equal("MBBank", account.Bank?.ShortName);
    }

    [Fact]
    public void TokenProtector_RoundTrips_AndDoesNotStoreTokenInClearText()
    {
        var protector = new SepayTokenProtector(new EphemeralDataProtectionProvider());
        const string token = "sepay-secret-access-token";

        var protectedToken = protector.Protect(token);

        Assert.DoesNotContain(token, protectedToken, StringComparison.Ordinal);
        Assert.Equal(token, protector.Unprotect(protectedToken));
    }

    [Fact]
    public void WebhookCreateRequest_SerializesTheFieldNamesSepayExpects()
    {
        // SePay validates on exact field names and enum spellings; a rename here fails silently
        // at runtime as a 400, so the contract is pinned in a test.
        var request = new SepayWebhookCreateRequest
        {
            BankAccountId = 19,
            Name = "FinViet - MBBank",
            WebhookUrl = "https://api.finviet.vn/api/wallets/sepay/webhook",
            ApiKey = "secret"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(19, root.GetProperty("bank_account_id").GetInt32());
        Assert.Equal("All", root.GetProperty("event_type").GetString());
        Assert.Equal("Api_Key", root.GetProperty("authen_type").GetString());
        Assert.Equal("Json", root.GetProperty("request_content_type").GetString());
        Assert.Equal("secret", root.GetProperty("api_key").GetString());
        Assert.Equal(1, root.GetProperty("active").GetInt32());
        // 0 = deliver everything; 1 would drop transactions whose content has no payment code.
        Assert.Equal(0, root.GetProperty("is_verify_payment").GetInt32());
    }

    [Fact]
    public void WebhookCreateResponse_ReadsTheAssignedId()
    {
        const string json = """
            { "status": "success", "message": "Webhook created successfully", "data": { "id": 23 } }
            """;

        var response = JsonSerializer.Deserialize<SepayWebhookCreateResponse>(json, JsonOptions);

        Assert.Equal(23, Assert.IsType<SepayWebhookCreateResponse>(response).Data?.Id);
    }

    [Fact]
    public void WebhookList_DeserializesRegisteredWebhooks()
    {
        const string json = """
            {
              "status": "success",
              "data": [
                {
                  "id": 23, "bank_account_id": 19, "name": "FinViet - MBBank",
                  "event_type": "All", "authen_type": "Api_Key",
                  "webhook_url": "https://api.finviet.vn/api/wallets/sepay/webhook", "active": 1
                }
              ]
            }
            """;

        var response = JsonSerializer.Deserialize<SepayWebhookListResponse>(json, JsonOptions);

        var webhook = Assert.Single(Assert.IsType<SepayWebhookListResponse>(response).Data);
        Assert.Equal(23, webhook.Id);
        Assert.Equal(19, webhook.BankAccountId);
        Assert.Equal("https://api.finviet.vn/api/wallets/sepay/webhook", webhook.WebhookUrl);
    }

    [Fact]
    public void LinkStateProtector_RoundTripsCustomerId()
    {
        var protector = new SepayLinkStateProtector(new EphemeralDataProtectionProvider());
        var customerId = Guid.NewGuid();

        var state = protector.Protect(customerId, TimeSpan.FromMinutes(10));

        Assert.Equal(customerId, protector.UnprotectCustomerId(state));
    }

    [Fact]
    public void LinkStateProtector_RejectsStateFromAnotherApplication()
    {
        var issuer = new SepayLinkStateProtector(new EphemeralDataProtectionProvider());
        var attacker = new SepayLinkStateProtector(new EphemeralDataProtectionProvider());

        var state = issuer.Protect(Guid.NewGuid(), TimeSpan.FromMinutes(10));

        Assert.Throws<CryptographicException>(() => attacker.UnprotectCustomerId(state));
    }

    [Theory]
    [InlineData("0123456789", "****6789")]
    [InlineData("6789", "6789")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AccountNumberMask_ExposesOnlyTheLastFourDigits(string? input, string? expected)
        => Assert.Equal(expected, AccountNumberMask.Apply(input));
}
