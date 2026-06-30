namespace FinViet.Domain.Enums;

/// <summary>Maps to Postgres enum <c>transaction_type</c> (expense, income, transfer_out, transfer_in).</summary>
public enum TransactionType
{
    Expense,
    Income,
    TransferOut,
    TransferIn
}
