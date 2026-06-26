using System.Text.Json.Serialization;

namespace FinViet.Infrastructure.ExternalServices.Sepay;

// DTOs mirroring the SePay v2 REST API. snake_case JSON → PascalCase via JsonPropertyName.

/// <summary>Envelope for list endpoints: <c>{ status, data: [...], meta }</c>.</summary>
public class SepayListResponse<T>
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public SepayMeta? Meta { get; set; }
}

public class SepayMeta
{
    [JsonPropertyName("pagination")]
    public SepayPagination? Pagination { get; set; }
}

public class SepayPagination
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("last_page")]
    public int LastPage { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }
}

/// <summary>One transaction from <c>/v2/transactions</c>.</summary>
public class SepayTransaction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("transaction_date")]
    public string TransactionDate { get; set; } = string.Empty;

    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("transfer_type")]
    public string TransferType { get; set; } = string.Empty; // "in" | "out"

    [JsonPropertyName("amount_in")]
    public decimal AmountIn { get; set; }

    [JsonPropertyName("amount_out")]
    public decimal AmountOut { get; set; }

    [JsonPropertyName("accumulated")]
    public decimal Accumulated { get; set; }

    [JsonPropertyName("transaction_content")]
    public string? TransactionContent { get; set; }

    [JsonPropertyName("reference_number")]
    public string? ReferenceNumber { get; set; }

    [JsonPropertyName("bank_brand_name")]
    public string? BankBrandName { get; set; }

    [JsonPropertyName("bank_account_id")]
    public string? BankAccountId { get; set; }
}

/// <summary>One linked bank account from <c>/v2/bank-accounts</c>.</summary>
public class SepayBankAccount
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("account_holder_name")]
    public string? AccountHolderName { get; set; }

    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("bank_short_name")]
    public string? BankShortName { get; set; }

    [JsonPropertyName("bank_full_name")]
    public string? BankFullName { get; set; }

    [JsonPropertyName("bank_code")]
    public string? BankCode { get; set; }
}
