using System.Text.Json.Serialization;

namespace FinViet.Infrastructure.ExternalServices.SePay;

// ─── Webhook management API (/api/v1/webhooks, OAuth2) ───────────────────────
// Lets FinViet register its own receiver with SePay instead of the user wiring the
// URL by hand in the SePay dashboard. Requires the webhook:read / webhook:write /
// webhook:delete scopes on the OAuth application.

/// <summary>Valid values for the SePay webhook fields FinViet sets.</summary>
internal static class SepayWebhookConstants
{
    /// <summary>Deliver both money-in and money-out events (also: In_only, Out_only).</summary>
    public const string EventTypeAll = "All";

    /// <summary>
    /// Shared-secret auth. SePay then calls the receiver with
    /// <c>Authorization: Apikey &lt;api_key&gt;</c>, which is what the receiver validates.
    /// </summary>
    public const string AuthenTypeApiKey = "Api_Key";

    /// <summary>Send the body as JSON (also: multipart_form-data, application_x-www-form-urlencoded).</summary>
    public const string ContentTypeJson = "Json";
}

internal sealed class SepayWebhookCreateRequest
{
    [JsonPropertyName("bank_account_id")]
    public int BankAccountId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = SepayWebhookConstants.EventTypeAll;

    [JsonPropertyName("authen_type")]
    public string AuthenType { get; set; } = SepayWebhookConstants.AuthenTypeApiKey;

    [JsonPropertyName("webhook_url")]
    public string WebhookUrl { get; set; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("request_content_type")]
    public string RequestContentType { get; set; } = SepayWebhookConstants.ContentTypeJson;

    /// <summary>
    /// 0 — deliver every transaction. FinViet mirrors a whole bank account rather than
    /// matching payment codes, so filtering on a payment code would drop real transactions.
    /// </summary>
    [JsonPropertyName("is_verify_payment")]
    public int IsVerifyPayment { get; set; }

    [JsonPropertyName("active")]
    public int Active { get; set; } = 1;
}

internal sealed class SepayWebhookCreateResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public SepayWebhookCreatedData? Data { get; set; }
}

internal sealed class SepayWebhookCreatedData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

internal sealed class SepayWebhookListResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<SepayWebhookInfo> Data { get; set; } = [];
}

internal sealed class SepayWebhookInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("bank_account_id")]
    public int BankAccountId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; set; }

    [JsonPropertyName("authen_type")]
    public string? AuthenType { get; set; }

    [JsonPropertyName("webhook_url")]
    public string? WebhookUrl { get; set; }

    [JsonPropertyName("active")]
    public int Active { get; set; }
}
