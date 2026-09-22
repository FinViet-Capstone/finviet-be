using FinViet.Application.Features.Subscriptions.Commands.ProcessPayOSWebhook;
using FinViet.Application.Features.Subscriptions.Queries.GetPaymentStatus;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Features.Subscriptions.Commands.ProcessPayOSWebhook;
using FinViet.Infrastructure.Features.Subscriptions.Queries.GetPaymentStatus;
using FinViet.Infrastructure.IntegrationTests.Support;
using FinViet.Infrastructure.Persistence;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinViet.Infrastructure.IntegrationTests;

/// <summary>
/// Exercises the row-lock fix for finviet-be#125 against a real PostgreSQL database - the
/// InMemory provider used by FinViet.Application.UnitTests can't execute the FOR UPDATE raw
/// SQL that PaymentLocking relies on, and the race only manifests under real transaction
/// isolation.
/// </summary>
public sealed class PaymentLockingConcurrencyTests
{
    [SkippableFact]
    public async Task ConcurrentWebhookDeliveries_ForSameOrderCode_ApplyResultExactlyOnce()
    {
        var (database, orderCode, customerId) = await SeedPendingPaymentAsync();
        if (database is null)
            return;
        await using var owned = database;

        async Task<bool> RunWebhookDeliveryAsync()
        {
            await using var db = TestDatabase.CreateDbContext(database.ConnectionString);
            var resultService = new SubscriptionPaymentResultService(
                db, NullLogger<SubscriptionPaymentResultService>.Instance);
            var handler = new ProcessPayOSWebhookCommandHandler(
                db,
                new FakeGateway(orderCode),
                resultService,
                NullLogger<ProcessPayOSWebhookCommandHandler>.Instance);

            return await handler.Handle(new ProcessPayOSWebhookCommand("raw-body"), CancellationToken.None);
        }

        // Two concurrent webhook deliveries for the same orderCode (a PayOS retry racing the
        // original delivery) must serialize on the Payment row so only one of them applies the
        // result - without the FOR UPDATE lock, both can read status="pending" and both create
        // a CustomerSubscription.
        await Task.WhenAll(RunWebhookDeliveryAsync(), RunWebhookDeliveryAsync());

        await AssertAppliedExactlyOnceAsync(database.ConnectionString, orderCode, customerId);
    }

    [SkippableFact]
    public async Task ConcurrentWebhookAndReconciliation_ForSameOrderCode_ApplyResultExactlyOnce()
    {
        var (database, orderCode, customerId) = await SeedPendingPaymentAsync();
        if (database is null)
            return;
        await using var owned = database;

        async Task<bool> RunWebhookDeliveryAsync()
        {
            await using var db = TestDatabase.CreateDbContext(database.ConnectionString);
            var resultService = new SubscriptionPaymentResultService(
                db, NullLogger<SubscriptionPaymentResultService>.Instance);
            var handler = new ProcessPayOSWebhookCommandHandler(
                db,
                new FakeGateway(orderCode),
                resultService,
                NullLogger<ProcessPayOSWebhookCommandHandler>.Instance);

            return await handler.Handle(new ProcessPayOSWebhookCommand("raw-body"), CancellationToken.None);
        }

        async Task RunReconciliationAsync()
        {
            await using var db = TestDatabase.CreateDbContext(database.ConnectionString);
            var resultService = new SubscriptionPaymentResultService(
                db, NullLogger<SubscriptionPaymentResultService>.Instance);
            var handler = new GetPaymentStatusQueryHandler(
                db,
                new FakeGateway(orderCode),
                resultService,
                NullLogger<GetPaymentStatusQueryHandler>.Instance);

            await handler.Handle(new GetPaymentStatusQuery(customerId, orderCode), CancellationToken.None);
        }

        // The scenario finviet-be#125 calls out explicitly: a PayOS webhook delivery racing the
        // GetPaymentStatus reconciliation fallback (finviet-be#121) for the same orderCode. Both
        // entry points call the same ApplyResultAsync, so they must serialize on the same locked
        // Payment row rather than each reading status="pending" independently.
        await Task.WhenAll(RunWebhookDeliveryAsync(), RunReconciliationAsync());

        await AssertAppliedExactlyOnceAsync(database.ConnectionString, orderCode, customerId);
    }

    private static async Task<(TestDatabase.DisposableDatabase? Database, long OrderCode, Guid CustomerId)> SeedPendingPaymentAsync()
    {
        var database = await TestDatabase.DisposableDatabase.CreateAsync("finviet_payment_lock_test");
        Skip.If(database is null, TestDatabase.DisposableDatabase.SkipReason);
        if (database is null)
            return (null, 0, Guid.Empty);

        await using (var initContext = TestDatabase.CreateDbContext(database.ConnectionString))
        {
            await DbInitializer.InitializeAsync(
                database.ConnectionString,
                initContext,
                BuildConfiguration(),
                new TestDatabase.TestHostEnvironment(Environments.Development),
                NullLogger.Instance);
        }

        var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Guid customerId;

        await using (var seedContext = TestDatabase.CreateDbContext(database.ConnectionString))
        {
            var plan = new SubscriptionPlan
            {
                PlanId = Guid.NewGuid(),
                Code = "premium_monthly",
                Name = "Premium Monthly",
                Price = 49000m,
                BillingIntervalMonths = 1,
                IsActive = true,
            };
            var customer = new Customer
            {
                CustomerId = Guid.NewGuid(),
                Email = $"payment-lock-{Guid.NewGuid():N}@test.finviet",
                FullName = "Payment Locking Test",
                IsActive = true,
            };
            var payment = new Payment
            {
                PaymentId = Guid.NewGuid(),
                CustomerId = customer.CustomerId,
                PlanId = plan.PlanId,
                Amount = plan.Price,
                ChargeType = "initial",
                Status = "pending",
                OrderCode = orderCode,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            seedContext.SubscriptionPlans.Add(plan);
            seedContext.Customers.Add(customer);
            seedContext.Payments.Add(payment);
            await seedContext.SaveChangesAsync();

            customerId = customer.CustomerId;
        }

        return (database, orderCode, customerId);
    }

    private static async Task AssertAppliedExactlyOnceAsync(string connectionString, long orderCode, Guid customerId)
    {
        await using var verifyContext = TestDatabase.CreateDbContext(connectionString);
        var succeededPaymentCount = await verifyContext.Payments
            .CountAsync(p => p.OrderCode == orderCode && p.Status == "succeeded");
        var subscriptionCount = await verifyContext.CustomerSubscriptions
            .CountAsync(s => s.CustomerId == customerId);

        Assert.Equal(1, succeededPaymentCount);
        Assert.Equal(1, subscriptionCount);
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SeedDemoData"] = "false" })
            .Build();

    /// <summary>Answers both VerifyWebhookAsync (webhook path) and GetOrderStatusAsync
    /// (reconciliation path) as a success for the same orderCode, with a short delay so
    /// concurrent callers overlap before either reaches BeginTransactionAsync.</summary>
    private sealed class FakeGateway(long orderCode) : IPaymentGateway
    {
        public Task<CreateOrderResult> CreateOrderAsync(
            long orderCode, int amount, string description, DateTimeOffset expiry,
            string returnUrl, string cancelUrl, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<WebhookVerificationResult> VerifyWebhookAsync(
            string webhookBody, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            return new WebhookVerificationResult(orderCode, 49000, Success: true, "TXN-CONCURRENCY-TEST");
        }

        public async Task<PaymentStatusResult> GetOrderStatusAsync(
            long orderCode, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            return new PaymentStatusResult(PaymentGatewayStatus.Succeeded, "TXN-CONCURRENCY-TEST", 49000);
        }

        public Task<ConfirmWebhookResult> ConfirmWebhookAsync(
            string webhookUrl, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
