namespace FinViet.Application.DTOs.Subscriptions;

public sealed class SubscribeToPlanResultDto
{
    public string RedirectUrl { get; init; } = null!;
}

/// <summary>
/// Read-only status for the browser return leg. Never authoritative — see
/// GetVNPayReturnStatusQueryHandler's remarks. The IPN is what actually finalizes a payment.
/// </summary>
public sealed class VNPayReturnStatusDto
{
    public string Status { get; init; } = null!; // pending | succeeded | failed | canceled | unknown
    public bool HashValid { get; init; }
    public Guid? SubscriptionId { get; init; }
}

/// <summary>
/// VNPay requires this exact small JSON shape as the HTTP 200 body for its IPN endpoint —
/// not the app's usual ApiResponse&lt;T&gt; envelope. VNPay's retry/ack behavior is driven by
/// this body's content, not the HTTP status code.
/// </summary>
public sealed class VNPayIpnReplyDto
{
    public string RspCode { get; init; } = null!;
    public string Message { get; init; } = null!;

    public static VNPayIpnReplyDto Of(string rspCode, string message) => new() { RspCode = rspCode, Message = message };
}
