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

    [Fact]
    public async Task SandboxValidationFailure_MapsHttp422ToStableValidationCode()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(
            HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"status":"error","message":"Invalid filter"}""")
        })) { BaseAddress = new Uri("https://my.sepay.vn") };

        var error = await Assert.ThrowsAsync<ExternalServiceException>(
            () => CreateClient(http).GetSandboxBankAccountsAsync("test-token"));

        Assert.Equal("sepay_validation_error", error.Code);
    }

    [Fact]
    public async Task SandboxCalls_AcceptLegacyNumericIdsReturnedByTestMode()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/v2/bank-accounts" => """
                    {"status":"success","data":[{
                      "id":15750,
                      "account_holder_name":"DUONG DUC F",
                      "account_number":"0000000001",
                      "accumulated":10000,
                      "label":"Demo",
                      "active":1,
                      "bank_short_name":"Sacombank",
                      "bank_code":"STB"
                    }]}
                    """,
                "/v2/transactions" => """
                    {"status":"success","data":[{
                      "id":32777,
                      "bank_account_id":15750,
                      "transaction_date":"2026-09-19 21:52:44",
                      "amount_out":0,
                      "amount_in":10000,
                      "accumulated":10000,
                      "transaction_content":"Giao dich thu nghiem"
                    }],"meta":{"pagination":{"current_page":1,"last_page":1,"has_more":false}}}
                    """,
                _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
        })) { BaseAddress = new Uri("https://my.sepay.vn") };
        var client = CreateClient(http);

        var account = Assert.Single(await client.GetSandboxBankAccountsAsync("test-token"));
        Assert.Equal("15750", account.Id);

        var transaction = Assert.Single(
            (await client.GetSandboxTransactionsAsync("test-token", account.Id)).Data);
        Assert.Equal("32777", transaction.Id);
        Assert.Equal("15750", transaction.BankAccountId);
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
