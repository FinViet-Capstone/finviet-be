using System.Text.Json.Nodes;

namespace FinViet.Api.IntegrationTests.Infrastructure;

/// <summary>Base for all API test classes: server gate, token shortcuts, small CRUD helpers.</summary>
[Collection("api")]
public abstract class ApiTestBase
{
    protected readonly ApiTestFixture Fx;
    protected string? Cust => Fx.CustomerToken;
    protected string? Admin => Fx.AdminToken;

    protected ApiTestBase(ApiTestFixture fx) => Fx = fx;

    /// <summary>Skips the current test (rather than failing it) when the API is not running.</summary>
    protected void RequireServer() => Skip.IfNot(Fx.ServerUp, Fx.DownReason ?? "API server is not running.");

    protected static string Idem() => Guid.NewGuid().ToString();
    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}".Substring(0, prefix.Length + 9);

    protected Task<ApiResult> CustGet(string path)              => Fx.SendAsync(HttpMethod.Get, path, token: Cust);
    protected Task<ApiResult> AdminGet(string path)             => Fx.SendAsync(HttpMethod.Get, path, token: Admin);

    /// <summary>Creates a basic/sepay_linked wallet and returns its id.</summary>
    protected async Task<string> CreateWalletAsync(string name, string type = "basic", decimal balance = 0)
    {
        var r = await Fx.SendAsync(HttpMethod.Post, "/api/wallets", token: Cust,
            body: new { walletName = name, walletType = type, initialBalance = balance });
        Assert.Equal(201, r.Code);
        return ApiTestFixture.Data(r)!["walletId"]!.GetValue<string>();
    }

    protected async Task DeleteWalletAsync(string? walletId)
    {
        if (string.IsNullOrEmpty(walletId)) return;
        await Fx.SendAsync(HttpMethod.Delete, $"/api/wallets/{walletId}", token: Cust);
    }

    protected static int ArrayLen(JsonNode? node) => node as JsonArray is { } a ? a.Count : -1;
}
