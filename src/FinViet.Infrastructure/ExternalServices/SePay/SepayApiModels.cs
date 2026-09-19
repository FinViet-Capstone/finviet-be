using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinViet.Infrastructure.ExternalServices.SePay;

// ─── OAuth token exchange ────────────────────────────────────────────────────

internal sealed class SepayTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

// ─── User profile ────────────────────────────────────────────────────────────

internal sealed class SepayUserResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public SepayUser? Data { get; set; }
}

internal sealed class SepayUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
}

// ─── Bank accounts ───────────────────────────────────────────────────────────

internal sealed class SepayBankAccountListResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<SepayBankAccount> Data { get; set; } = [];
}

internal sealed class SepayBankAccount
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("account_holder_name")]
    public string AccountHolderName { get; set; } = string.Empty;

    [JsonPropertyName("account_number")]
    public string AccountNumber { get; set; } = string.Empty;

    [JsonPropertyName("accumulated")]
    public decimal Accumulated { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("bank")]
    public SepayBankInfo? Bank { get; set; }
}

internal sealed class SepayBankInfo
{
    [JsonPropertyName("short_name")]
    public string ShortName { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("logo_url")]
    public string? LogoUrl { get; set; }
}

// ─── Transactions ────────────────────────────────────────────────────────────

internal sealed class SepayTransactionListResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<SepayTransaction> Data { get; set; } = [];

    [JsonPropertyName("meta")]
    public SepayMeta? Meta { get; set; }
}

internal sealed class SepayTransaction
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("bank_account_id")]
    public int BankAccountId { get; set; }

    [JsonPropertyName("bank_brand_name")]
    public string? BankBrandName { get; set; }

    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("transaction_date")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("amount_out")]
    public decimal AmountOut { get; set; }

    [JsonPropertyName("amount_in")]
    public decimal AmountIn { get; set; }

    [JsonPropertyName("accumulated")]
    public decimal Accumulated { get; set; }

    [JsonPropertyName("transaction_content")]
    public string? TransactionContent { get; set; }

    [JsonPropertyName("reference_number")]
    public string? ReferenceNumber { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("sub_account")]
    public string? SubAccount { get; set; }
}

internal sealed class SepayMeta
{
    [JsonPropertyName("pagination")]
    public SepayPagination? Pagination { get; set; }
}

internal sealed class SepayPagination
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("last_page")]
    public int LastPage { get; set; }
}

// ─── Static User API (my.sepay.vn/userapi) ───────────────────────────────────
// Personal API token endpoint. Amounts come back as strings and the list lives
// under a `transactions` key (not `data`).

internal sealed class SepayUserApiListResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("transactions")]
    public List<SepayUserApiTransaction> Transactions { get; set; } = [];
}

internal sealed class SepayUserApiTransaction
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("bank_account_id")]
    public string? BankAccountId { get; set; }

    [JsonPropertyName("bank_brand_name")]
    public string? BankBrandName { get; set; }

    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("transaction_date")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("amount_out")]
    public string? AmountOut { get; set; }

    [JsonPropertyName("amount_in")]
    public string? AmountIn { get; set; }

    [JsonPropertyName("accumulated")]
    public string? Accumulated { get; set; }

    [JsonPropertyName("transaction_content")]
    public string? TransactionContent { get; set; }

    [JsonPropertyName("reference_number")]
    public string? ReferenceNumber { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("sub_account")]
    public string? SubAccount { get; set; }
}

// ─── User API v2 Sandbox ─────────────────────────────────────────────────────

internal sealed class SepayV2BankAccountListResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<SepayV2BankAccount> Data { get; set; } = [];
}

internal sealed class SepayV2BankAccount
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(SepayStringOrNumberConverter))]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("account_holder_name")]
    public string AccountHolderName { get; set; } = string.Empty;

    [JsonPropertyName("account_number")]
    public string AccountNumber { get; set; } = string.Empty;

    [JsonPropertyName("accumulated")]
    public decimal Accumulated { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public int Active { get; set; }

    [JsonPropertyName("bank_short_name")]
    public string BankShortName { get; set; } = string.Empty;

    [JsonPropertyName("bank_code")]
    public string BankCode { get; set; } = string.Empty;
}

internal sealed class SepayV2TransactionListResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<SepayV2Transaction> Data { get; set; } = [];

    [JsonPropertyName("meta")]
    public SepayV2Meta? Meta { get; set; }
}

internal sealed class SepayV2Transaction
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(SepayStringOrNumberConverter))]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("bank_account_id")]
    [JsonConverter(typeof(SepayStringOrNumberConverter))]
    public string BankAccountId { get; set; } = string.Empty;

    [JsonPropertyName("transaction_date")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("amount_out")]
    public decimal AmountOut { get; set; }

    [JsonPropertyName("amount_in")]
    public decimal AmountIn { get; set; }

    [JsonPropertyName("accumulated")]
    public decimal Accumulated { get; set; }

    [JsonPropertyName("transaction_content")]
    public string? TransactionContent { get; set; }
}

internal sealed class SepayV2Meta
{
    [JsonPropertyName("pagination")]
    public SepayV2Pagination? Pagination { get; set; }
}

internal sealed class SepayV2Pagination
{
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("last_page")]
    public int LastPage { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}

/// <summary>
/// SePay API v2 documents account and transaction IDs as UUID strings, while Test Mode can
/// return the legacy numeric IDs used by its dashboard. Keep one string representation in the
/// integration layer so both response shapes can be linked and synchronized.
/// </summary>
internal sealed class SepayStringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException($"Expected a SePay identifier as string or number, got {reader.TokenType}.")
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    private static string ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var integer))
            return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (reader.TryGetDecimal(out var number))
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        throw new JsonException("The SePay numeric identifier is outside the supported range.");
    }
}
