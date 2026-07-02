namespace FinViet.Domain.Enums;

/// <summary>
/// Maps to Postgres enum <c>wallet_type</c> (basic, sepay_linked, finverse_linked).
/// Used only at the persistence boundary; the service/API layer works with the
/// equivalent string values via an EF value converter on <c>wallets.type</c>.
/// </summary>
public enum WalletType
{
    Basic,
    SepayLinked,
    FinverseLinked
}
