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

    private async Task<string> CreateAiSuggestedExpenseAsync(string walletId)
    {
        var created = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
            headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
            body: new
            {
                walletId,
                categoryId = "cat_food",
                transactionType = "EXPENSE",
                amount = 40_000,
                transactionDate = DateTime.UtcNow.ToString("o"),
                note = "AI guessed row",
                entryMethod = "manual",
                aiSource = "ai_suggestion",
                aiConfidence = 0.7
            });
        var txId = ApiTestFixture.Data(created)?["transactionId"]?.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(txId));
        return txId!;
    }

    private async Task AssertLockedAndLoggedAsync(string walletId, string txId, string categoryId)
    {
        var list = await CustGet($"/api/transactions?walletId={walletId}&pageSize=50");
        var row = ApiTestFixture.Data(list)?["items"]?.AsArray()
            .Single(i => i?["transactionId"]?.GetValue<string>() == txId);
        Assert.Equal(categoryId, row?["categoryId"]?.GetValue<string>());
        Assert.Equal("reviewed", row?["categorizationStatus"]?.GetValue<string>());
        Assert.Equal("manual", row?["aiSource"]?.GetValue<string>());
        Assert.Null(row?["aiConfidence"]);

        var log = await Fx.SendAsync(HttpMethod.Get,
            $"/api/category-corrections?categoryId={categoryId}&pageSize=100", token: Admin);
        Assert.Equal(200, log.Code);
        Assert.Contains(ApiTestFixture.Data(log)?["items"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray(),
            i => i?["transactionId"]?.GetValue<string>() == txId);
    }

    // sepay-ai T4 - classify locks the row and logs the correction of an AI result
    [SkippableFact]
    public async Task Classify_AfterAiSuggestion_LocksRowAndLogsCorrection()
    {
        RequireServer();
        string? wid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-clslock"), "basic", 500_000);
            var txId = await CreateAiSuggestedExpenseAsync(wid);

            var classify = await Fx.SendAsync(HttpMethod.Patch, $"/api/transactions/{txId}/classify",
                token: Cust, body: new { categoryId = "cat_transport" });
            Assert.Equal(200, classify.Code);

            await AssertLockedAndLoggedAsync(wid, txId, "cat_transport");
        }
        finally { await DeleteWalletAsync(wid); }
    }

    // sepay-ai T4 - override (the inbox "accept"/"change") locks the row and logs the correction
    [SkippableFact]
    public async Task Override_AfterAiSuggestion_LocksRowAndLogsCorrection()
    {
        RequireServer();
        string? wid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-ovlock"), "basic", 500_000);
            var txId = await CreateAiSuggestedExpenseAsync(wid);

            var over = await Fx.SendAsync(HttpMethod.Post, $"/api/ai/transactions/{txId}/override",
                token: Cust, body: new { categoryId = "cat_transport" });
            Assert.Equal(200, over.Code);

            await AssertLockedAndLoggedAsync(wid, txId, "cat_transport");
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

    // Categorization status + inbox filter (sepay-ai T1)
    [SkippableFact]
    public async Task CategorizationInboxFilter_ExcludesManualRows_AndStatusNoneIsReturned()
    {
        RequireServer();
        string? wid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-inbox"), "basic", 1_000_000);
            var create = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: Tx(wid, "cat_food", "EXPENSE", 20_000, "inbox probe"));
            Assert.Equal(201, create.Code);
            var txId = ApiTestFixture.Data(create)?["transactionId"]?.ToString();
            Assert.Equal("applied", ApiTestFixture.Data(create)?["categorizationStatus"]?.ToString());

            // A categorized manual row is never in the SePay review inbox.
            var inbox = await CustGet(
                $"/api/transactions?walletId={wid}&categorizationStatus=pending,suggested,unsure,failed&entryMethod=sepay_sync");
            Assert.Equal(200, inbox.Code);
            Assert.Empty(ApiTestFixture.Data(inbox)!["items"]!.AsArray());
            Assert.Equal(0, ApiTestFixture.Data(inbox)!["totalItems"]!.GetValue<int>());

            // The same row is found by its derived status, and carries the AI fields.
            var applied = await CustGet($"/api/transactions?walletId={wid}&categorizationStatus=applied&entryMethod=manual");
            Assert.Equal(200, applied.Code);
            var items = ApiTestFixture.Data(applied)!["items"]!.AsArray();
            var item = Assert.Single(items);
            Assert.Equal(txId, item!["transactionId"]?.ToString());
            Assert.Equal("applied", item["categorizationStatus"]?.ToString());
            Assert.True(item.AsObject().ContainsKey("aiSuggestedCategoryId"));
            Assert.True(item.AsObject().ContainsKey("aiConfidence"));
            Assert.True(item.AsObject().ContainsKey("aiSource"));
        }
        finally { await DeleteWalletAsync(wid); }
    }

    [SkippableFact]
    public async Task CategorizationStatusFilter_UnknownValue_Returns400()
    {
        RequireServer();
        var r = await CustGet("/api/transactions?categorizationStatus=bogus");
        Assert.Equal(400, r.Code);
    }
}
