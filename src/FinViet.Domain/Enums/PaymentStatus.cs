namespace FinViet.Domain.Enums;

/// <summary>Maps to Postgres enum <c>payment_status</c> (pending, succeeded, failed, canceled).</summary>
public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Canceled
}
