using System.Collections.Generic;
using FinViet.Api.IntegrationTests.Infrastructure;

namespace FinViet.Api.IntegrationTests.Tests;

public class SavingGoalTests : ApiTestBase
{
    public SavingGoalTests(ApiTestFixture fx) : base(fx) { }

    // TC-GOL-01 — full lifecycle: create → get → update → contribute → withdraw → archive
    [SkippableFact]
    public async Task SavingGoal_Lifecycle_Works()
    {
        RequireServer();
        string? wid = null, gid = null;
        string[] transactionIds = Array.Empty<string>();
        try
        {
            wid = await CreateWalletAsync(Unique("TEST-gw"), "basic", 1_000_000);

            var create = await Fx.SendAsync(HttpMethod.Post, "/api/saving-goals", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { goalName = "Mua laptop", iconEmoji = "💻", targetAmount = 20_000_000, deadline = "2026-12-31", fundingWalletId = wid });
            Assert.Equal(201, create.Code);
            var createdGoal = ApiTestFixture.Data(create);
            gid = createdGoal?["goalId"]?.GetValue<string>();
            Assert.False(string.IsNullOrEmpty(gid));
            Assert.Equal("💻", createdGoal?["iconEmoji"]?.GetValue<string>());
            Assert.NotNull(createdGoal?["createdAt"]);

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

            var rejectedArchive = await Fx.SendAsync(HttpMethod.Delete, $"/api/saving-goals/{gid}", token: Cust);
            Assert.Equal(422, rejectedArchive.Code);
            Assert.Equal("goal_balance_must_be_withdrawn", rejectedArchive.Json?["code"]?.GetValue<string>());

            var withdraw = await Fx.SendAsync(HttpMethod.Post, $"/api/saving-goals/{gid}/withdraw", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { amount = 200_000, walletId = wid });
            Assert.Equal(200, withdraw.Code);
            Assert.Equal(0m, ApiTestFixture.Data(withdraw)?["currentAmount"]?.GetValue<decimal>());

            var walletBeforeArchive = await CustGet($"/api/wallets/{wid}");
            var balanceBeforeArchive = ApiTestFixture.Data(walletBeforeArchive)?["balance"]?.GetValue<decimal>();
            var ledgerBeforeArchive = await CustGet($"/api/saving-goals/{gid}/contributions");
            var ledgerBeforeArchiveRows = ApiTestFixture.Data(ledgerBeforeArchive)?.AsArray();
            transactionIds = ledgerBeforeArchiveRows!
                .Select(node => node?["transactionId"]?.GetValue<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray();
            Assert.Equal(2, transactionIds.Length);

            var now = DateTime.UtcNow;
            var summaryBeforeArchive = await CustGet(
                $"/api/transactions/summary?year={now.Year}&month={now.Month}");
            Assert.Equal(200, summaryBeforeArchive.Code);
            var incomeBeforeArchive = ApiTestFixture.Data(summaryBeforeArchive)?["income"]?.GetValue<decimal>();
            var expenseBeforeArchive = ApiTestFixture.Data(summaryBeforeArchive)?["expense"]?.GetValue<decimal>();

            var archive = await Fx.SendAsync(HttpMethod.Delete, $"/api/saving-goals/{gid}", token: Cust);
            Assert.Equal(200, archive.Code);

            var activeList = await CustGet("/api/saving-goals");
            Assert.DoesNotContain(
                ApiTestFixture.Data(activeList)!.AsArray(),
                node => node?["goalId"]?.GetValue<string>() == gid);

            var archivedList = await CustGet("/api/saving-goals?archived=true");
            var archivedGoals = ApiTestFixture.Data(archivedList)?.AsArray();
            Assert.Contains(archivedGoals!, node => node?["goalId"]?.GetValue<string>() == gid);

            var archivedDetail = await CustGet($"/api/saving-goals/{gid}");
            Assert.Equal(200, archivedDetail.Code);
            Assert.True(ApiTestFixture.Data(archivedDetail)?["isDeleted"]?.GetValue<bool>());

            var ledger = await CustGet($"/api/saving-goals/{gid}/contributions");
            Assert.Equal(200, ledger.Code);
            Assert.Equal(2, ArrayLen(ApiTestFixture.Data(ledger)));

            var walletAfterArchive = await CustGet($"/api/wallets/{wid}");
            Assert.Equal(
                balanceBeforeArchive,
                ApiTestFixture.Data(walletAfterArchive)?["balance"]?.GetValue<decimal>());
            foreach (var transactionId in transactionIds)
            {
                var transaction = await CustGet($"/api/transactions/{transactionId}");
                Assert.Equal(200, transaction.Code);
            }

            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var transactionList = await CustGet(
                $"/api/transactions?page=1&pageSize=100&from={date}&to={date}");
            Assert.Equal(200, transactionList.Code);
            var archivedTransactionRows = ApiTestFixture.Data(transactionList)?["items"]?.AsArray()
                .Where(node => transactionIds.Contains(node?["transactionId"]?.GetValue<string>()))
                .ToArray();
            Assert.NotNull(archivedTransactionRows);
            Assert.Equal(2, archivedTransactionRows!.Length);
            Assert.Contains(archivedTransactionRows, node =>
                node?["transactionType"]?.GetValue<string>() == "expense"
                && node?["categoryId"]?.GetValue<string>() == "cat_savings_goal"
                && node?["description"]?.GetValue<string>().StartsWith("Nạp mục tiêu:") == true);
            Assert.Contains(archivedTransactionRows, node =>
                node?["transactionType"]?.GetValue<string>() == "income"
                && node?["categoryId"]?.GetValue<string>() == "cat_savings_goal"
                && node?["description"]?.GetValue<string>().StartsWith("Rút mục tiêu:") == true);

            var summaryAfterArchive = await CustGet(
                $"/api/transactions/summary?year={now.Year}&month={now.Month}");
            Assert.Equal(200, summaryAfterArchive.Code);
            Assert.Equal(
                incomeBeforeArchive,
                ApiTestFixture.Data(summaryAfterArchive)?["income"]?.GetValue<decimal>());
            Assert.Equal(
                expenseBeforeArchive,
                ApiTestFixture.Data(summaryAfterArchive)?["expense"]?.GetValue<decimal>());

            var archivedPatch = await Fx.SendAsync(HttpMethod.Patch, $"/api/saving-goals/{gid}", token: Cust,
                body: new { targetAmount = 30_000_000 });
            Assert.Equal(404, archivedPatch.Code);

            var archivedContribution = await Fx.SendAsync(HttpMethod.Post, $"/api/saving-goals/{gid}/contribute", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { amount = 1 });
            Assert.Equal(404, archivedContribution.Code);

            var archivedWithdrawal = await Fx.SendAsync(HttpMethod.Post, $"/api/saving-goals/{gid}/withdraw", token: Cust,
                headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                body: new { amount = 1, walletId = wid });
            Assert.Equal(404, archivedWithdrawal.Code);

            var repeatedArchive = await Fx.SendAsync(HttpMethod.Delete, $"/api/saving-goals/{gid}", token: Cust);
            Assert.Equal(404, repeatedArchive.Code);
            gid = null;
        }
        finally
        {
            if (gid is not null)
            {
                var detail = await CustGet($"/api/saving-goals/{gid}");
                var remaining = ApiTestFixture.Data(detail)?["currentAmount"]?.GetValue<decimal>() ?? 0m;
                if (remaining > 0 && wid is not null)
                {
                    await Fx.SendAsync(HttpMethod.Post, $"/api/saving-goals/{gid}/withdraw", token: Cust,
                        headers: new Dictionary<string, string> { ["Idempotency-Key"] = Idem() },
                        body: new { amount = remaining, walletId = wid });
                }
                await Fx.SendAsync(HttpMethod.Delete, $"/api/saving-goals/{gid}", token: Cust);
            }
            foreach (var transactionId in transactionIds)
                await Fx.SendAsync(HttpMethod.Delete, $"/api/transactions/{transactionId}", token: Cust);
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
