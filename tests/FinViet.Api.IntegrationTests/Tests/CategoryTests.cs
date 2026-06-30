using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class CategoryTests : ApiTestBase
{
    public CategoryTests(ApiTestFixture fx) : base(fx) { }

    // TC-CAT-01 — global library is readable by a customer
    [SkippableFact]
    public async Task GetCategories_ReturnsGlobalLibrary()
    {
        RequireServer();
        var r = await CustGet("/api/categories");
        Assert.Equal(200, r.Code);
        Assert.True(ArrayLen(ApiTestFixture.Data(r)) >= 14, "Expected the seeded global category library.");
    }

    // TC-CAT-02 — filter expense
    [SkippableFact]
    public async Task GetCategories_FilterExpense_ReturnsOnlyExpense()
    {
        RequireServer();
        var r = await CustGet("/api/categories?type=expense");
        Assert.Equal(200, r.Code);
        Assert.True(ArrayLen(ApiTestFixture.Data(r)) > 0);
    }

    // TC-CAT-03 — filter income
    [SkippableFact]
    public async Task GetCategories_FilterIncome_ReturnsOnlyIncome()
    {
        RequireServer();
        var r = await CustGet("/api/categories?type=income");
        Assert.Equal(200, r.Code);
        Assert.True(ArrayLen(ApiTestFixture.Data(r)) > 0);
    }

    // TC-CAT-04 — single category by id
    [SkippableFact]
    public async Task GetCategory_KnownId_Returns200()
    {
        RequireServer();
        var r = await CustGet("/api/categories/cat_food");
        Assert.Equal(200, r.Code);
        Assert.Equal("cat_food", ApiTestFixture.Data(r)?["categoryId"]?.GetValue<string>());
    }

    // TC-CAT-05 — unknown category id → 404
    [SkippableFact]
    public async Task GetCategory_UnknownId_Returns404()
    {
        RequireServer();
        var r = await CustGet("/api/categories/cat_does_not_exist");
        Assert.Equal(404, r.Code);
    }
}
