using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class AuthAndProfileTests : ApiTestBase
{
    public AuthAndProfileTests(ApiTestFixture fx) : base(fx) { }

    // TC-AUTH-01
    [SkippableFact]
    public async Task Login_WithValidCustomerCredentials_ReturnsTokens()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/auth/login",
            body: new { email = Fx.CustomerEmail, password = Fx.CustomerPassword });

        Assert.Equal(200, r.Code);
        Assert.True(r.Success);
        Assert.False(string.IsNullOrEmpty(ApiTestFixture.Data(r)?["accessToken"]?.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(ApiTestFixture.Data(r)?["refreshToken"]?.GetValue<string>()));
    }

    // TC-AUTH-02
    [SkippableFact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/auth/login",
            body: new { email = Fx.CustomerEmail, password = "definitely-wrong" });
        Assert.Equal(401, r.Code);
    }

    // TC-AUTH-03
    [SkippableFact]
    public async Task AdminLogin_WithValidCredentials_ReturnsToken()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/auth/admin-login",
            body: new { username = Fx.AdminUsername, password = Fx.AdminPassword });
        Assert.Equal(200, r.Code);
        Assert.False(string.IsNullOrEmpty(ApiTestFixture.Data(r)?["accessToken"]?.GetValue<string>()));
    }

    // TC-AUTH-04 — refresh-token rotation
    [SkippableFact]
    public async Task RefreshToken_WithValidRefreshToken_ReturnsNewTokens()
    {
        RequireServer();
        Skip.If(string.IsNullOrEmpty(Fx.CustomerRefreshToken), "No refresh token captured at login.");
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/auth/refresh-token",
            body: new { refreshToken = Fx.CustomerRefreshToken });
        Assert.Equal(200, r.Code);
        Assert.False(string.IsNullOrEmpty(ApiTestFixture.Data(r)?["accessToken"]?.GetValue<string>()));
    }

    // TC-PROF-01
    [SkippableFact]
    public async Task GetProfile_ReturnsCustomerProfile()
    {
        RequireServer();
        var r = await CustGet("/api/profile");
        Assert.Equal(200, r.Code);
        Assert.Equal(Fx.CustomerEmail, ApiTestFixture.Data(r)?["email"]?.GetValue<string>());
    }

    // TC-PROF-02 — update profile + monthly income (Onboarding requirement)
    [SkippableFact]
    public async Task UpdateProfile_PersistsMonthlyIncome()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Put, "/api/profile", token: Cust,
            body: new { fullName = "Tran Khang Vinh", monthlyIncomeExpected = 12_000_000 });
        Assert.Equal(200, r.Code);
        Assert.Equal(12_000_000m, ApiTestFixture.Data(r)?["monthlyIncomeExpected"]?.GetValue<decimal>());
    }

    // TC-PROF-03 — profile requires authentication
    [SkippableFact]
    public async Task GetProfile_WithoutToken_Returns401()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Get, "/api/profile");
        Assert.Equal(401, r.Code);
    }
}
