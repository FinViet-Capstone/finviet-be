using System.Net;
using FinViet.Application.Common.Exceptions;
using FinViet.Infrastructure.ExternalServices.SePay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinViet.Application.UnitTests;

public class SepaySandboxClientTests
{
    [Fact]
    public async Task SandboxCalls_UseIsolatedV2Host_EvenWithProductionBaseAddress()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("userapi-sandbox.sepay.vn", request.RequestUri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            paths.Add(request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"success","data":[]}""")
            };
        })) { BaseAddress = new Uri("https://my.sepay.vn") };
        var client = CreateClient(http);

        Assert.Empty(await client.GetSandboxBankAccountsAsync("test-token"));
        Assert.Empty((await client.GetSandboxTransactionsAsync("test-token", "demo-account-id")).Data);
        Assert.Equal(new[] { "/v2/bank-accounts", "/v2/transactions" }, paths);
    }

    [Fact]
    public async Task InvalidSandboxToken_IsNotRetriedAgainstProduction()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Assert.Equal("userapi-sandbox.sepay.vn", request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"message":"Invalid token"}""")
            };
        })) { BaseAddress = new Uri("https://my.sepay.vn") };

        var error = await Assert.ThrowsAsync<ExternalServiceException>(
            () => CreateClient(http).GetSandboxBankAccountsAsync("test-token"));
        Assert.Equal("sepay_unauthorized", error.Code);
        Assert.Equal(1, calls);
    }

    private static SepayClient CreateClient(HttpClient http) => new(
        http, Options.Create(new SepayOptions()), NullLogger<SepayClient>.Instance);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handle(request));
    }
}
