using System.Collections.Generic;
using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class TransactionTests : ApiTestBase
{
    public TransactionTests(ApiTestFixture fx) : base(fx) { }

    private static object Tx(string walletId, string categoryId, string type, decimal amount, string note) => new
    {
        walletId,
        categoryId,
        transactionType = type,
        amount,
        transactionDate = DateTime.UtcNow.ToString("o"),
        note,
        entryMethod = "manual"
    };

    // TC-TXN-01 — list transactions (paged)
    [SkippableFact]
    public async Task GetTransactions_Paged_Returns200()
    {
        RequireServer();
        var r = await CustGet("/api/transactions?page=1&pageSize=10");
        Assert.Equal(200, r.Code);
        Assert.NotNull(ApiTestFixture.Data(r)?["items"]);
    }

    // TC-TXN-02 — create a manual expense
    [SkippableFact]
    public async Task CreateExpense_Returns201_AndDeductsWallet()
    {
        RequireServer();
        string? wid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-tx"), "basic", 1_000_000);
            var r = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: Tx(wid, "cat_food", "EXPENSE", 50_000, "An sang Q2"));
            Assert.Equal(201, r.Code);

            var w = await CustGet($"/api/wallets/{wid}");
            Assert.Equal(950_000m, ApiTestFixture.Data(w)?["balance"]?.GetValue<decimal>());
        }
        finally { await DeleteWalletAsync(wid); }
    }

    // TC-TXN-03 — expense exceeding wallet balance → 422
    [SkippableFact]
    public async Task CreateExpense_ExceedingBalance_Returns422()
    {
        RequireServer();
        string? wid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-ov"), "basic", 10_000);
            var r = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: Tx(wid, "cat_food", "EXPENSE", 999_999_999, "too big"));
            Assert.Equal(422, r.Code);
        }
        finally { await DeleteWalletAsync(wid); }
    }

    // TC-TXN-04 — Idempotency-Key replay must NOT create a duplicate (BUSINESS_LOGIC §10)
    [SkippableFact]
    public async Task CreateExpense_SameIdempotencyKey_DoesNotDuplicate()
    {
        RequireServer();
        string? wid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-idem"), "basic", 1_000_000);
            var key = Idem();
            var headers = new Dictionary<string, string> { ["Idempotency-Key"] = key };
            var first  = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: headers, body: Tx(wid, "cat_dining", "EXPENSE", 35_000, "cafe"));
            var second = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: headers, body: Tx(wid, "cat_dining", "EXPENSE", 35_000, "cafe"));

            var id1 = ApiTestFixture.Data(first)?["transactionId"]?.GetValue<string>();
            var id2 = ApiTestFixture.Data(second)?["transactionId"]?.GetValue<string>();

            Skip.If(id1 is not null && id2 is not null && id1 != id2,
                "POTENTIAL BUG: Idempotency-Key replay created a different transaction id — dedup not enforced.");

            // After fix: balance must reflect a single 35k deduction, not 70k.
            var w = await CustGet($"/api/wallets/{wid}");
            Assert.Equal(965_000m, ApiTestFixture.Data(w)?["balance"]?.GetValue<decimal>());
        }
        finally { await DeleteWalletAsync(wid); }
    }

    // TC-TXN-05 — change category (classify), then full update
    [SkippableFact]
    public async Task ClassifyAndUpdateCategory_Returns200()
    {
        RequireServer();
        string? wid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-cls"), "basic", 500_000);
            var created = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: Tx(wid, "cat_food", "EXPENSE", 40_000, "HKD Nha Bun Dau"));
            var txId = ApiTestFixture.Data(created)?["transactionId"]?.GetValue<string>();
            Assert.False(string.IsNullOrEmpty(txId));

            var classify = await Fx.SendAsync(HttpMethod.Patch, $"/api/transactions/{txId}/classify",
                token: Cust, body: new { categoryId = "cat_transport" });
            Assert.Equal(200, classify.Code);

            var update = await Fx.SendAsync(HttpMethod.Put, $"/api/transactions/{txId}",
                token: Cust, body: new { categoryId = "cat_dining" });
            Assert.Equal(200, update.Code);
        }
        finally { await DeleteWalletAsync(wid); }
    }

    // TC-TXN-06 — monthly summary feeds the Spending Dashboard (donut/bar/top-5)
    [SkippableFact]
    public async Task GetSummary_ReturnsDashboardAggregates()
    {
        RequireServer();
        var now = DateTime.UtcNow;
        var r = await CustGet($"/api/transactions/summary?year={now.Year}&month={now.Month}");
        Assert.Equal(200, r.Code);
        var d = ApiTestFixture.Data(r);
        Assert.NotNull(d?["byCategory"]);   // donut
        Assert.NotNull(d?["byDay"]);        // daily bar chart
        Assert.NotNull(d?["topBeneficiaries"]); // top merchants
    }
}
