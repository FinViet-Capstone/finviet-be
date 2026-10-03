namespace FinViet.Application.Interfaces;

/// <summary>
/// Answers "is this customer still allowed to use the API?" on every authenticated
/// request. JWT access tokens are stateless, so without this check a customer locked
/// by an admin could keep using a still-valid access token until it expires.
/// </summary>
public interface ICustomerAccountStatusService
{
    /// <summary>
    /// True when the customer exists, is active and not soft-deleted.
    /// Implementations may cache briefly; a lock takes effect within that window.
    /// </summary>
    Task<bool> IsActiveAsync(Guid customerId, CancellationToken cancellationToken = default);
}
