using System.Collections.Generic;
using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class TransferWithdrawTests : ApiTestBase
{
    public TransferWithdrawTests(ApiTestFixture fx) : base(fx) { }

    // TC-TRF-01 — internal transfer between two wallets
    [SkippableFact]
    public async Task Transfer_BetweenWallets_MovesBalance()
    {
        RequireServer();
        string? a = null, b = null;
        try
        {
            a = await CreateWalletAsync(Unique("TEST-A"), "basic", 1_000_000);
            b = await CreateWalletAsync(Unique("TEST-B"), "basic", 500_000);
            var r = await Fx.SendAsync(HttpMethod.Post, "/api/wallets/transfer", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { fromWalletId = a, toWalletId = b, amount = 100_000, description = "test transfer" });
            Assert.Equal(200, r.Code);
            Assert.Equal(900_000m, ApiTestFixture.Data(r)?["fromWalletBalance"]?.GetValue<decimal>());
            Assert.Equal(600_000m, ApiTestFixture.Data(r)?["toWalletBalance"]?.GetValue<decimal>());
        }
        finally { await DeleteWalletAsync(a); await DeleteWalletAsync(b); }
    }

    // TC-TRF-02 — same source/destination → 400
    [SkippableFact]
    public async Task Transfer_SameWallet_Returns400()
    {
        RequireServer();
        string? a = null;
        try
        {
            a = await CreateWalletAsync(Unique("TEST-S"), "basic", 1_000_000);
            var r = await Fx.SendAsync(HttpMethod.Post, "/api/wallets/transfer", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { fromWalletId = a, toWalletId = a, amount = 50_000 });
            Assert.Equal(400, r.Code);
        }
        finally { await DeleteWalletAsync(a); }
    }

    // TC-TRF-03 — transfer exceeding source balance → 400
    [SkippableFact]
    public async Task Transfer_ExceedingBalance_Returns400()
    {
        RequireServer();
        string? a = null, b = null;
        try
        {
            a = await CreateWalletAsync(Unique("TEST-X"), "basic", 10_000);
            b = await CreateWalletAsync(Unique("TEST-Y"), "basic", 0);
            var r = await Fx.SendAsync(HttpMethod.Post, "/api/wallets/transfer", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { fromWalletId = a, toWalletId = b, amount = 999_999_999 });
            Assert.Equal(400, r.Code);
        }
        finally { await DeleteWalletAsync(a); await DeleteWalletAsync(b); }
    }

    // TC-WDR-01 — withdrawal is only allowed from a SePay-linked wallet (BUSINESS_LOGIC §1)
    [SkippableFact]
    public async Task Withdraw_FromBasicWallet_Returns422()
    {
        RequireServer();
        string? a = null;
        try
        {
            a = await CreateWalletAsync(Unique("TEST-W"), "basic", 1_000_000);
            var r = await Fx.SendAsync(HttpMethod.Post, "/api/wallets/withdraw", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { fromWalletId = a, amount = 100_000 });
            Assert.Equal(422, r.Code);
        }
        finally { await DeleteWalletAsync(a); }
    }
}
