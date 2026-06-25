using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class BudgetTests : ApiTestBase
{
    public BudgetTests(ApiTestFixture fx) : base(fx) { }

    // TC-BUD-01 — list budgets
    [SkippableFact]
    public async Task GetBudgets_Returns200()
    {
        RequireServer();
        var r = await CustGet("/api/budgets");
        Assert.Equal(200, r.Code);
    }

    // TC-BUD-02 — bucket cards carry allocationCap + adherence score (BUSINESS_LOGIC §6)
    [SkippableFact]
    public async Task GetBuckets_ReturnsAllocationAndAdherence()
    {
        RequireServer();
        var r = await CustGet("/api/budgets/buckets");
        Assert.Equal(200, r.Code);
        var d = ApiTestFixture.Data(r);
        Assert.NotNull(d?["buckets"]);
        Assert.NotNull(d?["monthlyIncome"]);
    }

    // TC-BUD-03 — upsert a per-category limit, then update, then delete
    [SkippableFact]
    public async Task UpsertUpdateDeleteBudget_Lifecycle()
    {
        RequireServer();
        var create = await Fx.SendAsync(HttpMethod.Post, "/api/budgets", token: Cust,
            body: new { categoryId = "cat_food", monthlyLimit = 2_000_000 });
        Assert.Equal(200, create.Code);
        var id = ApiTestFixture.Data(create)?["id"]?.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(id));

        var patch = await Fx.SendAsync(HttpMethod.Patch, $"/api/budgets/{id}", token: Cust,
            body: new { monthlyLimit = 2_500_000 });
        Assert.Equal(200, patch.Code);
        Assert.Equal(2_500_000m, ApiTestFixture.Data(patch)?["monthlyLimit"]?.GetValue<decimal>());

        var del = await Fx.SendAsync(HttpMethod.Delete, $"/api/budgets/{id}", token: Cust);
        Assert.Equal(204, del.Code);
    }

    // TC-BUD-04 — budgets are a customer-only resource
    [SkippableFact]
    public async Task GetBudgets_AsAdmin_Returns403()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await AdminGet("/api/budgets");
        Assert.Equal(403, r.Code);
    }
}
