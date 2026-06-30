using System.Collections.Generic;
using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

/// <summary>Merchant auto-categorization rules (api_list #41–43) + BUSINESS_LOGIC §2/§8.</summary>
public class RulesTests : ApiTestBase
{
    public RulesTests(ApiTestFixture fx) : base(fx) { }

    private object Tx(string walletId, string categoryId, decimal amount, string merchant) => new
    {
        walletId,
        categoryId,
        transactionType = "EXPENSE",
        amount,
        transactionDate = DateTime.UtcNow.ToString("o"),
        note = merchant,
        merchant,
        entryMethod = "manual"
    };

    // TC-RULE-01 — list rules
    [SkippableFact]
    public async Task GetRules_Returns200()
    {
        RequireServer();
        var r = await CustGet("/api/rules");
        Assert.Equal(200, r.Code);
    }

    // TC-RULE-02 — creating a rule retro-applies its category to matching transactions (§2)
    [SkippableFact]
    public async Task CreateRule_RetroAppliesToMatchingTransactions()
    {
        RequireServer();
        string? wid = null, ruleId = null;
        var keyword = "ZZRuleShop" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-rule"), "basic", 1_000_000);
            // Seed a transaction categorized as food, with a merchant containing the keyword.
            var created = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: Tx(wid, "cat_food", 65_000, keyword + " Coffee"));
            Assert.Equal(201, created.Code);
            var txId = ApiTestFixture.Data(created)?["transactionId"]?.GetValue<string>();

            // Create a rule mapping the keyword → dining; expect retro-apply.
            var rule = await Fx.SendAsync(HttpMethod.Post, "/api/rules", token: Cust,
                body: new { merchantKeyword = keyword, categoryId = "cat_dining" });
            Assert.Equal(201, rule.Code);
            ruleId = ApiTestFixture.Data(rule)?["rule"]?["ruleId"]?.GetValue<string>();
            Assert.True(ApiTestFixture.Data(rule)?["appliedCount"]?.GetValue<int>() >= 1,
                "Rule should have retro-applied to at least the seeded transaction.");

            // The transaction's category should now be dining.
            var txn = await CustGet($"/api/transactions/{txId}");
            Assert.Equal("cat_dining", ApiTestFixture.Data(txn)?["categoryId"]?.GetValue<string>());
        }
        finally
        {
            if (ruleId is not null) await Fx.SendAsync(HttpMethod.Delete, $"/api/rules/{ruleId}", token: Cust);
            await DeleteWalletAsync(wid);
        }
    }

    // TC-RULE-03 — duplicate keyword → 409
    [SkippableFact]
    public async Task CreateRule_DuplicateKeyword_Returns409()
    {
        RequireServer();
        string? id1 = null, id2 = null;
        var keyword = "ZZDup" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            var first = await Fx.SendAsync(HttpMethod.Post, "/api/rules", token: Cust,
                body: new { merchantKeyword = keyword, categoryId = "cat_shopping" });
            Assert.Equal(201, first.Code);
            id1 = ApiTestFixture.Data(first)?["rule"]?["ruleId"]?.GetValue<string>();

            var second = await Fx.SendAsync(HttpMethod.Post, "/api/rules", token: Cust,
                body: new { merchantKeyword = keyword.ToLowerInvariant(), categoryId = "cat_shopping" });
            Assert.Equal(409, second.Code);
            id2 = ApiTestFixture.Data(second)?["rule"]?["ruleId"]?.GetValue<string>();
        }
        finally
        {
            if (id1 is not null) await Fx.SendAsync(HttpMethod.Delete, $"/api/rules/{id1}", token: Cust);
            if (id2 is not null) await Fx.SendAsync(HttpMethod.Delete, $"/api/rules/{id2}", token: Cust);
        }
    }

    // TC-RULE-04 — unknown category → 404
    [SkippableFact]
    public async Task CreateRule_UnknownCategory_Returns404()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/rules", token: Cust,
            body: new { merchantKeyword = "ZZNope" + Guid.NewGuid().ToString("N")[..6], categoryId = "cat_nope" });
        Assert.Equal(404, r.Code);
    }

    // TC-RULE-05 — delete missing rule → 404
    [SkippableFact]
    public async Task DeleteRule_Missing_Returns404()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Delete,
            "/api/rules/00000000-0000-0000-0000-000000000000", token: Cust);
        Assert.Equal(404, r.Code);
    }

    // TC-RULE-06 — rules are a customer-only resource
    [SkippableFact]
    public async Task GetRules_AsAdmin_Returns403()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await AdminGet("/api/rules");
        Assert.Equal(403, r.Code);
    }

    // TC-RULE-07 — a new transaction with NO category auto-applies a matching rule (§2b)
    [SkippableFact]
    public async Task CreateTransaction_WithoutCategory_AutoAppliesRule()
    {
        RequireServer();
        string? wid = null, ruleId = null;
        var keyword = "ZZAuto" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-auto"), "basic", 1_000_000);
            var rule = await Fx.SendAsync(HttpMethod.Post, "/api/rules", token: Cust,
                body: new { merchantKeyword = keyword, categoryId = "cat_dining" });
            Assert.Equal(201, rule.Code);
            ruleId = ApiTestFixture.Data(rule)?["rule"]?["ruleId"]?.GetValue<string>();

            // create an EXPENSE with no categoryId; the note matches the rule keyword
            var created = await Fx.SendAsync(HttpMethod.Post, "/api/transactions", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new
                {
                    walletId = wid,
                    transactionType = "EXPENSE",
                    amount = 90_000,
                    transactionDate = DateTime.UtcNow.ToString("o"),
                    note = keyword + " Quan an",
                    entryMethod = "manual"
                });
            Assert.Equal(201, created.Code);
            Assert.Equal("cat_dining", ApiTestFixture.Data(created)?["categoryId"]?.GetValue<string>());
        }
        finally
        {
            if (ruleId is not null) await Fx.SendAsync(HttpMethod.Delete, $"/api/rules/{ruleId}", token: Cust);
            await DeleteWalletAsync(wid);
        }
    }

    // TC-RULE-08 — SMS extraction: a matching rule takes precedence over AI (deterministic)
    [SkippableFact]
    public async Task ExtractSms_MatchingRule_TakesPrecedence()
    {
        RequireServer();
        string? ruleId = null;
        var keyword = "ZZSms" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            var rule = await Fx.SendAsync(HttpMethod.Post, "/api/rules", token: Cust,
                body: new { merchantKeyword = keyword, categoryId = "cat_shopping" });
            Assert.Equal(201, rule.Code);
            ruleId = ApiTestFixture.Data(rule)?["rule"]?["ruleId"]?.GetValue<string>();

            var sms = $"TK 0123 -120,000 VND luc 12/06/2025 10:00. ND: {keyword} thanh toan";
            var r = await Fx.SendAsync(HttpMethod.Post, "/api/extract/sms", token: Cust, body: new { text = sms });
            Assert.Equal(200, r.Code);
            var row = ApiTestFixture.Data(r)?["rows"]?.AsArray()?.FirstOrDefault();
            Assert.NotNull(row);
            Assert.Equal("cat_shopping", row!["categoryId"]?.GetValue<string>());
            Assert.Equal(1.0m, row["confidence"]?.GetValue<decimal>());
        }
        finally
        {
            if (ruleId is not null) await Fx.SendAsync(HttpMethod.Delete, $"/api/rules/{ruleId}", token: Cust);
        }
    }
}
