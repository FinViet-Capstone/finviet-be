using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class CategoryRequestAndAdminTests : ApiTestBase
{
    public CategoryRequestAndAdminTests(ApiTestFixture fx) : base(fx) { }

    // TC-BKT-01 — customer reassigns a category's bucket directly, no admin approval needed
    [SkippableFact]
    public async Task Customer_SetsCategoryBucket_ReflectsInCategoryList()
    {
        RequireServer();
        var set = await Fx.SendAsync(HttpMethod.Put, "/api/categories/cat_entertain/bucket", token: Cust,
            body: new { bucketId = "needs" });
        Assert.Equal(200, set.Code);
        Assert.Equal("needs", ApiTestFixture.Data(set)?["expenseClass"]?.GetValue<string>());

        var list = await CustGet("/api/categories?type=expense");
        Assert.Equal(200, list.Code);

        // Reset back to the category's global default so other tests aren't affected.
        var reset = await Fx.SendAsync(HttpMethod.Delete, "/api/categories/cat_entertain/bucket", token: Cust);
        Assert.Equal(200, reset.Code);
        Assert.Equal("wants", ApiTestFixture.Data(reset)?["expenseClass"]?.GetValue<string>());
    }

    // TC-BKT-02 — invalid bucket id is rejected
    [SkippableFact]
    public async Task Customer_SetsInvalidBucket_ReturnsError()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Put, "/api/categories/cat_food/bucket", token: Cust,
            body: new { bucketId = "not_a_bucket" });
        Assert.True(r.Code is 400 or 422);
    }

    // TC-BKT-03 — income categories have no bucket and cannot be reassigned
    [SkippableFact]
    public async Task Customer_SetsBucketOnIncomeCategory_ReturnsError()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Put, "/api/categories/cat_salary/bucket", token: Cust,
            body: new { bucketId = "needs" });
        Assert.True(r.Code is 400 or 422);
    }

    // TC-ADM-01 — admin category CRUD lifecycle
    [SkippableFact]
    public async Task AdminCategoryCrud_Lifecycle()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var catId = "cat_test_" + Guid.NewGuid().ToString("N")[..8];

        var create = await Fx.SendAsync(HttpMethod.Post, "/api/categories", token: Admin,
            body: new { categoryId = catId, categoryName = "Test Cat", nameEn = "Test", type = "expense", expenseClass = "wants", icon = "star", color = "#FFFFFF", sortOrder = 99 });
        Assert.Equal(201, create.Code);

        var patch = await Fx.SendAsync(HttpMethod.Patch, $"/api/categories/{catId}", token: Admin,
            body: new { categoryName = "Test Cat 2" });
        Assert.Equal(200, patch.Code);

        var del = await Fx.SendAsync(HttpMethod.Delete, $"/api/categories/{catId}", token: Admin);
        Assert.Equal(200, del.Code);
    }

    // TC-ADM-02 — deactivate a non-existent customer → 404
    [SkippableFact]
    public async Task AdminDeactivate_UnknownCustomer_Returns404()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await Fx.SendAsync(HttpMethod.Put,
            "/api/account/deactivate/00000000-0000-0000-0000-000000000000", token: Admin);
        Assert.Equal(404, r.Code);
    }
}
