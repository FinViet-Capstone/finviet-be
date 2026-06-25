using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace FinViet.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shared fixture: holds a single <see cref="HttpClient"/>, logs in once as the demo
/// customer and the seeded admin, and exposes their bearer tokens to every test in the
/// collection. If the API is not reachable, <see cref="ServerUp"/> is false and tests
/// skip themselves via <c>Skip.IfNot(Fx.ServerUp, ...)</c>.
/// </summary>
public sealed class ApiTestFixture : IAsyncLifetime
{
    public HttpClient Client { get; }
    public string BaseUrl { get; }
    public bool ServerUp { get; private set; }
    public string? DownReason { get; private set; }

    public string? CustomerToken { get; private set; }
    public string? CustomerRefreshToken { get; private set; }
    public string? AdminToken { get; private set; }

    public string CustomerEmail { get; }
    public string CustomerPassword { get; }
    public string AdminUsername { get; }
    public string AdminPassword { get; }

    public ApiTestFixture()
    {
        BaseUrl          = Env("FINVIET_TEST_BASEURL", "http://localhost:5122").TrimEnd('/');
        CustomerEmail    = Env("FINVIET_TEST_CUST_EMAIL", "tkv2003@gmail.com");
        CustomerPassword = Env("FINVIET_TEST_CUST_PASSWORD", "Tkl123tkl123");
        AdminUsername    = Env("FINVIET_TEST_ADMIN_USER", "admin");
        AdminPassword    = Env("FINVIET_TEST_ADMIN_PASSWORD", "Admin@123");

        Client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task InitializeAsync()
    {
        // Customer login doubles as a reachability + credentials check.
        try
        {
            var login = await this.SendAsync(HttpMethod.Post, "/api/auth/login",
                body: new { email = CustomerEmail, password = CustomerPassword });

            if ((int)login.Status == 200)
            {
                CustomerToken        = Data(login)?["accessToken"]?.GetValue<string>();
                CustomerRefreshToken = Data(login)?["refreshToken"]?.GetValue<string>();
            }

            if (string.IsNullOrEmpty(CustomerToken))
            {
                DownReason = $"Customer login failed (HTTP {(int)login.Status}). " +
                             $"Check the seeded account exists: {CustomerEmail}.";
                return;
            }

            var admin = await this.SendAsync(HttpMethod.Post, "/api/auth/admin-login",
                body: new { username = AdminUsername, password = AdminPassword });
            if ((int)admin.Status == 200)
                AdminToken = Data(admin)?["accessToken"]?.GetValue<string>();

            ServerUp = true;
        }
        catch (Exception ex)
        {
            DownReason = $"API not reachable at {BaseUrl}: {ex.Message}. " +
                         "Start it with `dotnet run --project src/FinViet.Api` before running integration tests.";
            ServerUp = false;
        }
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Returns the <c>data</c> envelope when present (ApiResponse&lt;T&gt;),
    /// otherwise the root node (raw-DTO controllers such as Transactions).</summary>
    public static JsonNode? Data(ApiResult r) => r.Json?["data"] ?? r.Json;

    private static string Env(string key, string fallback)
        => Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiTestFixture> { }
