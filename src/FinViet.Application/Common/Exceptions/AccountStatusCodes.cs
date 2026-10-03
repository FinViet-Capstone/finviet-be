namespace FinViet.Application.Common.Exceptions;

/// <summary>
/// Stable, machine-readable error codes for account-status failures. Returned in the
/// <c>code</c> field of the error envelope so clients can react (e.g. force logout)
/// without parsing the human-readable message.
/// </summary>
public static class AccountStatusCodes
{
    /// <summary>The customer account was deactivated (locked by an admin) or deleted.</summary>
    public const string AccountDeactivated = "account_deactivated";
}
