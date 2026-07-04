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
