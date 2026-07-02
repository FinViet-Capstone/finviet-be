using System.Text.Json.Serialization;

namespace FinViet.Infrastructure.ExternalServices.Finverse;

internal class FinverseTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("login_identity_id")]
    public string? LoginIdentityId { get; set; }
}

internal sealed class FinverseLinkTokenApiResponse : FinverseTokenResponse
{
    [JsonPropertyName("link_url")]
    public string LinkUrl { get; set; } = string.Empty;
}

internal sealed class FinverseLoginIdentityApiResponse
{
    // Finverse nests the object under "login_identity"; be tolerant of a flat "status" too.
    [JsonPropertyName("login_identity")]
    public FinverseLoginIdentity? LoginIdentity { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    public string? EffectiveStatus => LoginIdentity?.Status ?? Status;
}

internal sealed class FinverseLoginIdentity
{
    [JsonPropertyName("login_identity_id")]
    public string? LoginIdentityId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class FinverseAccountsApiResponse
{
    [JsonPropertyName("accounts")]
    public List<FinverseAccount> Accounts { get; set; } = new();

    [JsonPropertyName("institution")]
    public FinverseInstitution? Institution { get; set; }
}

internal sealed class FinverseAccount
{
    [JsonPropertyName("account_id")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("account_name")]
    public string AccountName { get; set; } = string.Empty;

    [JsonPropertyName("account_nickname")]
    public string? AccountNickname { get; set; }

    [JsonPropertyName("account_number_masked")]
    public string? AccountNumberMasked { get; set; }

    [JsonPropertyName("account_currency")]
    public string? AccountCurrency { get; set; }

    [JsonPropertyName("account_type")]
    public FinverseAccountType? AccountType { get; set; }

    [JsonPropertyName("balance")]
    public FinverseAmount? Balance { get; set; }

    [JsonPropertyName("is_closed")]
    public bool IsClosed { get; set; }

    [JsonPropertyName("is_excluded")]
    public bool IsExcluded { get; set; }

    [JsonPropertyName("is_parent")]
    public bool IsParent { get; set; }
}

internal sealed class FinverseAccountType
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }
}

internal sealed class FinverseInstitution
{
    [JsonPropertyName("institution_id")]
    public string? InstitutionId { get; set; }

    [JsonPropertyName("institution_name")]
    public string? InstitutionName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class FinverseTransactionsApiResponse
{
    [JsonPropertyName("total_transactions")]
    public int TotalTransactions { get; set; }

    [JsonPropertyName("transactions")]
    public List<FinverseTransaction> Transactions { get; set; } = new();
}

internal sealed class FinverseTransaction
{
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("account_id")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public FinverseAmount Amount { get; set; } = new();

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("is_pending")]
    public bool IsPending { get; set; }

    [JsonPropertyName("posted_date")]
    public DateOnly PostedDate { get; set; }

    [JsonPropertyName("transaction_date")]
    public DateOnly? TransactionDate { get; set; }

    [JsonPropertyName("transaction_time")]
    public DateTime? TransactionTime { get; set; }

    [JsonPropertyName("transaction_reference")]
    public string? TransactionReference { get; set; }

    [JsonPropertyName("transaction_details")]
    public FinverseTransactionDetails? TransactionDetails { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

internal sealed class FinverseAmount
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("value")]
    public decimal Value { get; set; }
}

internal sealed class FinverseTransactionDetails
{
    [JsonPropertyName("counterparty_name")]
    public string? CounterpartyName { get; set; }

    [JsonPropertyName("bank_reference")]
    public string? BankReference { get; set; }

    [JsonPropertyName("transaction_type")]
    public string? TransactionType { get; set; }
}
