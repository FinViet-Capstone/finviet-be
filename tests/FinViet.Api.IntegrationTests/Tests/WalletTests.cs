using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class WalletTests : ApiTestBase
{
    public WalletTests(ApiTestFixture fx) : base(fx) { }

    // TC-WAL-01 — list wallets returns total balance + array
    [SkippableFact]
    public async Task GetWallets_ReturnsTotalBalanceAndList()
    {
        RequireServer();
        var r = await CustGet("/api/wallets");
        Assert.Equal(200, r.Code);
        Assert.NotNull(ApiTestFixture.Data(r)?["wallets"]);
    }

    // TC-WAL-02 — create basic wallet
    [SkippableFact]
    public async Task CreateBasicWallet_Returns201()
    {
        RequireServer();
        string? id = null;
        try
        {
            id = await CreateWalletAsync(Unique("TEST-cash"), "basic", 1_000_000);
            var r = await CustGet($"/api/wallets/{id}");
            Assert.Equal(200, r.Code);
            Assert.Equal(1_000_000m, ApiTestFixture.Data(r)?["balance"]?.GetValue<decimal>());
        }
        finally { await DeleteWalletAsync(id); }
    }

    // TC-WAL-03 — invalid wallet type → 400
    [SkippableFact]
    public async Task CreateWallet_InvalidType_Returns400()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/wallets", token: Cust,
            body: new { walletName = "Bad", walletType = "nonsense", initialBalance = 0 });
        Assert.Equal(400, r.Code);
    }

    // TC-WAL-04 — rename wallet
    [SkippableFact]
    public async Task RenameWallet_PersistsName()
    {
        RequireServer();
        string? id = null;
        try
        {
            id = await CreateWalletAsync(Unique("TEST-ren"), "basic");
            var renamed = Unique("TEST-new");
            var r = await Fx.SendAsync(HttpMethod.Patch, $"/api/wallets/{id}", token: Cust,
                body: new { walletName = renamed });
            Assert.Equal(200, r.Code);
            Assert.Equal(renamed, ApiTestFixture.Data(r)?["walletName"]?.GetValue<string>());
        }
        finally { await DeleteWalletAsync(id); }
    }

    // TC-WAL-05 — wallet type is immutable after creation (BUSINESS_LOGIC §1)
    [SkippableFact]
    public async Task ChangeWalletType_AfterCreation_Returns400()
    {
        RequireServer();
        string? id = null;
        try
        {
            id = await CreateWalletAsync(Unique("TEST-typ"), "basic");
            var r = await Fx.SendAsync(HttpMethod.Patch, $"/api/wallets/{id}", token: Cust,
                body: new { walletType = "sepay_linked" });
            Assert.Equal(400, r.Code);
        }
        finally { await DeleteWalletAsync(id); }
    }

    // TC-WAL-06 — wallets require authentication
    [SkippableFact]
    public async Task GetWallets_WithoutToken_Returns401()
    {
        RequireServer();
        var r = await Fx.SendAsync(HttpMethod.Get, "/api/wallets");
        Assert.Equal(401, r.Code);
    }
}
