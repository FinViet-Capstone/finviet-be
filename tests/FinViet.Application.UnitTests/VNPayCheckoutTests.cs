using FinViet.Application.Common.Exceptions;
using FinViet.Application.Features.Subscriptions.Queries.GetSubscriptionPayment;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.Features.Subscriptions.Queries.GetSubscriptionPayment;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Application.Features.Subscriptions.Queries.GetCurrentSubscription;
using FinViet.Infrastructure.Features.Subscriptions.Queries.GetCurrentSubscription;
using FinViet.Application.Features.Subscriptions.Commands.SubscribeToPlan;
using FinViet.Infrastructure.ExternalServices.VNPay;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinViet.Application.UnitTests;

public class VNPayCheckoutTests
{
    [Fact]
    public async Task CurrentSubscription_ReturnsOnlyCustomersCurrentPlan()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.CustomerSubscriptions.AddRange(
            new CustomerSubscription { SubscriptionId = Guid.NewGuid(), CustomerId = customerId, Status = "expired" },
            new CustomerSubscription { SubscriptionId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = "active" });
        await db.SaveChangesAsync();
        var handler = new GetCurrentSubscriptionQueryHandler(db);
        Assert.Null(await handler.Handle(new(customerId), default));
        var active = new CustomerSubscription
        {
            SubscriptionId = Guid.NewGuid(), CustomerId = customerId, Status = "active", LockedPrice = 49000,
        };
        db.CustomerSubscriptions.Add(active);
        await db.SaveChangesAsync();
        var result = await handler.Handle(new(customerId), default);
        Assert.Equal(active.SubscriptionId, result!.SubscriptionId);
        Assert.Equal(49000, result.LockedPrice);
    }

    [Fact]
    public async Task PaymentStatus_OnlyOwnerCanRead_AndPollingDoesNotActivateSubscription()
    {
        await using var db = TestDbContextFactory.Create();
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), PlanId = Guid.NewGuid(),
            Amount = 49000, ChargeType = "initial", Status = "pending", VnpTxnRef = "SUB123",
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        var handler = new GetSubscriptionPaymentQueryHandler(db);
        var result = await handler.Handle(new(payment.CustomerId, payment.PaymentId), default);
        Assert.Equal("pending", result.Status);
        Assert.Null(result.SubscriptionId);
        Assert.Empty(db.CustomerSubscriptions);
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new(Guid.NewGuid(), payment.PaymentId), default));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new(payment.CustomerId, Guid.NewGuid()), default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("VNPAYQR")]
    [InlineData("VNBANK")]
    [InlineData("INTCARD")]
    public void Checkout_SignsMethodAmountAndVietnamExpiry(string? bankCode)
    {
        using var http = new HttpClient();
        var client = new VNPayClient(http, Options.Create(new VNPayOptions
        {
            TmnCode = "TESTCODE", HashSecret = "test-secret", ReturnUrl = "https://example.com/return"
        }), NullLogger<VNPayClient>.Instance);
        var expiry = new DateTimeOffset(2026, 9, 17, 12, 30, 0, TimeSpan.Zero);
        var url = client.BuildPaymentUrl(new VNPayPaymentRequest(49000, "SUB123", "FinViet Premium",
            "127.0.0.1", BankCode: bankCode, ExpiresAt: expiry));
        var fields = QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(x => x.Key, x => x.Value.ToString());
        Assert.Equal("4900000", fields["vnp_Amount"]);
        Assert.Equal("20260917193000", fields["vnp_ExpireDate"]);
        Assert.Equal(bankCode, fields.GetValueOrDefault("vnp_BankCode"));
        Assert.True(client.VerifySecureHash(fields));
        fields["vnp_BankCode"] = "TAMPERED";
        Assert.False(client.VerifySecureHash(fields));
    }

    [Theory]
    [InlineData("VNPAYQR", true)]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("untrusted", false)]
    public void Subscribe_ValidatesPaymentMethod(string? bankCode, bool valid)
    {
        var command = new SubscribeToPlanCommand(Guid.NewGuid(), Guid.NewGuid(), "https://example.com/return",
            "127.0.0.1", "test-key", bankCode);
        Assert.Equal(valid, new SubscribeToPlanCommandValidator().Validate(command).IsValid);
    }
}
