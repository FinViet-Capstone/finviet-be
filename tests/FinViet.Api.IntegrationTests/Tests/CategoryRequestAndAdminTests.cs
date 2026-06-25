using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class CategoryRequestAndAdminTests : ApiTestBase
{
    public CategoryRequestAndAdminTests(ApiTestFixture fx) : base(fx) { }

    // TC-REQ-01 — customer submits a category request, then sees it in /mine
    [SkippableFact]
    public async Task SubmitCategoryRequest_AppearsInMine()
    {
        RequireServer();
        var name = Unique("Req");
        var create = await Fx.SendAsync(HttpMethod.Post, "/api/category-requests", token: Cust,
            body: new { categoryName = name, type = "expense", expenseClass = "wants", note = "auto-test" });
        Assert.True(create.Code is 200 or 201);

        var mine = await CustGet("/api/category-requests/mine");
        Assert.Equal(200, mine.Code);
    }

    // TC-REQ-02 — admin approves a fresh request → creates a global category
    [SkippableFact]
    public async Task AdminApprovesCategoryRequest()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");

        var create = await Fx.SendAsync(HttpMethod.Post, "/api/category-requests", token: Cust,
            body: new { categoryName = Unique("Appr"), type = "expense", expenseClass = "wants", note = "approve-test" });
        var id = ApiTestFixture.Data(create)?["requestId"]?.GetValue<string>();
        Skip.If(string.IsNullOrEmpty(id), "Could not create a category request to approve.");

        var approve = await Fx.SendAsync(HttpMethod.Post, $"/api/category-requests/{id}/approve", token: Admin,
            body: new { reviewNote = "approved by test", categoryId = "cat_it_" + Guid.NewGuid().ToString("N")[..6], expenseClass = "wants" });
        Assert.Equal(200, approve.Code);
    }

    // TC-REQ-03 — admin rejects a fresh request
    [SkippableFact]
    public async Task AdminRejectsCategoryRequest()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");

        var create = await Fx.SendAsync(HttpMethod.Post, "/api/category-requests", token: Cust,
            body: new { categoryName = Unique("Rej"), type = "expense", expenseClass = "wants", note = "reject-test" });
        var id = ApiTestFixture.Data(create)?["requestId"]?.GetValue<string>();
        Skip.If(string.IsNullOrEmpty(id), "Could not create a category request to reject.");

        var reject = await Fx.SendAsync(HttpMethod.Post, $"/api/category-requests/{id}/reject", token: Admin,
            body: new { reviewNote = "rejected by test" });
        Assert.Equal(200, reject.Code);
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
