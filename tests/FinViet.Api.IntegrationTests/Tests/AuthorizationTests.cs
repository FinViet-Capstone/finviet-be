using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

/// <summary>Role-based access control negative tests.</summary>
public class AuthorizationTests : ApiTestBase
{
    public AuthorizationTests(ApiTestFixture fx) : base(fx) { }

    // TC-AUTHZ-01 — customer cannot list category requests (admin-only)
    [SkippableFact]
    public async Task Customer_ListingCategoryRequests_Returns403()
    {
        RequireServer();
        var r = await CustGet("/api/category-requests");
        Assert.Equal(403, r.Code);
    }

    // TC-AUTHZ-02 — customer cannot create a global category (admin-only)
    [SkippableFact]
    public async Task Customer_CreatingCategory_Returns403()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/categories", token: Cust,
            body: new { categoryId = "cat_x", categoryName = "X", type = "expense", expenseClass = "wants" });
        Assert.Equal(403, r.Code);
    }

    // TC-AUTHZ-03 — admin cannot use customer-only endpoints
    [SkippableFact]
    public async Task Admin_AccessingWallets_Returns403()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await AdminGet("/api/wallets");
        Assert.Equal(403, r.Code);
    }

    // TC-AUTHZ-04 — unauthenticated access is rejected
    [SkippableFact]
    public async Task NoToken_AccessingWallets_Returns401()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Get, "/api/wallets");
        Assert.Equal(401, r.Code);
    }
}
