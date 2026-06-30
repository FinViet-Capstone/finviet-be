using System.Collections.Generic;
using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class SavingGoalTests : ApiTestBase
{
    public SavingGoalTests(ApiTestFixture fx) : base(fx) { }

    // TC-GOL-01 — full lifecycle: create → get → update → contribute → delete
    [SkippableFact]
    public async Task SavingGoal_Lifecycle_Works()
    {
        RequireServer();
        string? wid = null, gid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-gw"), "basic", 1_000_000);

            var create = await Fx.SendAsync(HttpMethod.Post, "/api/saving-goals", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { goalName = "Mua laptop", targetAmount = 20_000_000, deadline = "2026-12-31", fundingWalletId = wid });
            Assert.Equal(201, create.Code);
            gid = ApiTestFixture.Data(create)?["goalId"]?.GetValue<string>();
            Assert.False(string.IsNullOrEmpty(gid));

            var get = await CustGet($"/api/saving-goals/{gid}");
            Assert.Equal(200, get.Code);

            var patch = await Fx.SendAsync(HttpMethod.Patch, $"/api/saving-goals/{gid}", token: Cust,
                body: new { targetAmount = 25_000_000 });
            Assert.Equal(200, patch.Code);

            var contribute = await Fx.SendAsync(HttpMethod.Post, $"/api/saving-goals/{gid}/contribute", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { amount = 200_000 });
            Assert.Equal(200, contribute.Code);
            Assert.Equal(200_000m, ApiTestFixture.Data(contribute)?["currentAmount"]?.GetValue<decimal>());
        }
        finally
        {
            if (gid is not null) await Fx.SendAsync(HttpMethod.Delete, $"/api/saving-goals/{gid}", token: Cust);
            await DeleteWalletAsync(wid);
        }
    }

    // TC-GOL-02 — contribution exceeding remaining goal amount → 422 (BUSINESS_LOGIC §10)
    [SkippableFact]
    public async Task Contribute_ExceedingRemaining_Returns422()
    {
        RequireServer();
        string? wid = null, gid = null;
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-ge"), "basic", 1_000_000);
            var create = await Fx.SendAsync(HttpMethod.Post, "/api/saving-goals", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { goalName = "Goal small", targetAmount = 500_000, deadline = "2026-12-31", fundingWalletId = wid });
            Assert.Equal(201, create.Code);
            gid = ApiTestFixture.Data(create)?["goalId"]?.GetValue<string>();

            var contribute = await Fx.SendAsync(HttpMethod.Post, $"/api/saving-goals/{gid}/contribute", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { amount = 999_999_999 });
            Assert.Equal(422, contribute.Code);
        }
        finally
        {
            if (gid is not null) await Fx.SendAsync(HttpMethod.Delete, $"/api/saving-goals/{gid}", token: Cust);
            await DeleteWalletAsync(wid);
        }
    }
}
