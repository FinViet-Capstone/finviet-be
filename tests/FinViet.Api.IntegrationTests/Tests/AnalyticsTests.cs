using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class AnalyticsTests : ApiTestBase
{
    public AnalyticsTests(ApiTestFixture fx) : base(fx) { }

    // TC-ANL-01 — summary returns the expected shape
    [SkippableFact]
    public async Task GetSummary_ReturnsExpectedShape()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await AdminGet("/api/analytics/summary");
        Assert.Equal(200, r.Code);
        var d = ApiTestFixture.Data(r);
        Assert.NotNull(d?["totalCustomers"]);
        Assert.NotNull(d?["activeCustomers"]);
        Assert.NotNull(d?["newCustomers"]);
        Assert.NotNull(d?["totalTransactions"]);
        Assert.NotNull(d?["totalWallets"]);
        Assert.NotNull(d?["totalBudgets"]);
        Assert.NotNull(d?["freeSubscriptions"]);
        Assert.NotNull(d?["premiumSubscriptions"]);
    }

    // TC-ANL-02 — trend returns exactly `days` zero-filled points
    [SkippableFact]
    public async Task GetTrend_ReturnsDailyPoints()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await AdminGet("/api/analytics/trend?metric=signups&days=7");
        Assert.Equal(200, r.Code);
        Assert.Equal(7, ArrayLen(ApiTestFixture.Data(r)));
    }

    // TC-ANL-03 — an out-of-range `days` clamps to the default rather than erroring
    [SkippableFact]
    public async Task GetTrend_DaysOutOfRange_ClampsToDefault()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Admin), "Admin token unavailable.");
        var r = await AdminGet("/api/analytics/trend?metric=transactions&days=9999");
        Assert.Equal(200, r.Code);
        Assert.Equal(30, ArrayLen(ApiTestFixture.Data(r)));
    }

    // TC-ANL-04 — analytics is an admin-only resource
    [SkippableFact]
    public async Task GetSummary_AsCustomer_Returns403()
    {
        RequireServer();
        var r = await CustGet("/api/analytics/summary");
        Assert.Equal(403, r.Code);
    }
}
