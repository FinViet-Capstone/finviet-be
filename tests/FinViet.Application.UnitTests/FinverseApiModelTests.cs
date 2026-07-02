using System.Text.Json;
using FinViet.Infrastructure.ExternalServices.Finverse;
using Microsoft.AspNetCore.DataProtection;

namespace FinViet.Application.UnitTests;

public class FinverseApiModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Accounts_response_maps_bank_metadata_and_balance()
    {
        const string json = """
            {
              "accounts": [{
                "account_id": "01ACCOUNT",
                "account_name": "Savings",
                "account_nickname": "Rainy day",
                "account_number_masked": "1234***89",
                "account_currency": "VND",
                "account_type": { "type": "DEPOSIT", "subtype": "SAVINGS" },
                "balance": { "currency": "VND", "value": 1250000.50 },
                "is_closed": false,
                "is_excluded": false,
                "is_parent": false
              }],
              "institution": {
                "institution_id": "testbank-vn",
                "institution_name": "Test Bank Vietnam"
              }
            }
            """;

        var response = JsonSerializer.Deserialize<FinverseAccountsApiResponse>(json, JsonOptions);

        var account = Assert.Single(Assert.IsType<FinverseAccountsApiResponse>(response).Accounts);
        Assert.Equal("01ACCOUNT", account.AccountId);
        Assert.Equal("SAVINGS", account.AccountType?.Subtype);
        Assert.Equal(1_250_000.50m, account.Balance?.Value);
        Assert.Equal("Test Bank Vietnam", response!.Institution?.InstitutionName);
    }

    [Fact]
    public void Transactions_response_maps_posted_date_signed_amount_and_counterparty()
    {
        const string json = """
            {
              "total_transactions": 1,
              "transactions": [{
                "transaction_id": "01TRANSACTION",
                "account_id": "01ACCOUNT",
                "amount": { "currency": "VND", "value": -99000 },
                "description": "Coffee",
                "is_pending": false,
                "posted_date": "2026-06-30",
                "transaction_details": { "counterparty_name": "Cafe Fin" }
              }]
            }
            """;

        var response = JsonSerializer.Deserialize<FinverseTransactionsApiResponse>(json, JsonOptions);

        var transaction = Assert.Single(Assert.IsType<FinverseTransactionsApiResponse>(response).Transactions);
        Assert.Equal(new DateOnly(2026, 6, 30), transaction.PostedDate);
        Assert.Equal(-99_000m, transaction.Amount.Value);
        Assert.Equal("Cafe Fin", transaction.TransactionDetails?.CounterpartyName);
    }

    [Fact]
    public void Link_state_is_compact_single_use_and_rejects_tampering()
    {
        var protector = new FinverseLinkStateProtector();
        var customerId = Guid.NewGuid();

        var state = protector.Protect(customerId, TimeSpan.FromMinutes(1));

        Assert.True(state.Length <= 100);
        Assert.Equal(customerId, protector.UnprotectCustomerId(state));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => protector.UnprotectCustomerId(state));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => protector.UnprotectCustomerId($"{state}tampered"));
    }

    [Fact]
    public void Login_identity_tokens_are_encrypted_at_rest_and_round_trip()
    {
        var protector = new FinverseTokenProtector(new EphemeralDataProtectionProvider());
        const string token = "finverse-secret-token";

        var encrypted = protector.Protect(token);

        Assert.NotEqual(token, encrypted);
        Assert.Equal(token, protector.Unprotect(encrypted));
    }
}
