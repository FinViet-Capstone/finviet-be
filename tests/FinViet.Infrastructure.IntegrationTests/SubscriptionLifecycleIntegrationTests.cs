using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Features.Subscriptions.Commands.ProcessPayOSWebhook;
using FinViet.Application.Features.Subscriptions.Commands.ProcessPayOSWebhook;
using FinViet.Infrastructure.IntegrationTests.Support;
using FinViet.Infrastructure.Persistence;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using FinViet.Infrastructure.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinViet.Infrastructure.IntegrationTests;

/// <summary>finviet-be#138 against a real PostgreSQL: the advisory lock, uq_active_subscription,
/// the enum-typed notification row and the subscription_reminders key, none of which the InMemory
/// provider enforces.</summary>
public sealed class SubscriptionLifecycleIntegrationTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    [SkippableFact]
    public async Task TwoPaidOrders_DeliveredConcurrently_BothSucceed_AndStackTwoIntervals()
    {
        await using var database = await CreateDatabaseAsync();
        var (customerId, plan) = await SeedCustomerAndPlanAsync(database.ConnectionString);
        long[] orderCodes = [Today.DayNumber * 10L + 1, Today.DayNumber * 10L + 2];
        await using (var seed = TestDatabase.CreateDbContext(database.ConnectionString))
        {
            foreach (var orderCode in orderCodes)
            {
                seed.Payments.Add(new Payment
                {
                    PaymentId = Guid.NewGuid(), CustomerId = customerId, PlanId = plan.PlanId, Amount = plan.Price,
                    ChargeType = "initial", Status = "pending", OrderCode = orderCode,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
            }
            await seed.SaveChangesAsync();
        }

        // The double-tap / web-and-mobile case: two QR codes paid, webhooks racing each other.
        await Task.WhenAll(orderCodes.Select(orderCode => DeliverPaidWebhookAsync(database.ConnectionString, orderCode)));

        await using var verify = TestDatabase.CreateDbContext(database.ConnectionString);
        Assert.Equal(2, await verify.Payments.CountAsync(p => p.Status == "succeeded"));
        var subscription = await verify.CustomerSubscriptions.SingleAsync(s => s.CustomerId == customerId);
        Assert.Equal("active", subscription.Status);
        Assert.Equal(Today, subscription.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 2), subscription.EndDate);
        Assert.Equal(2, await verify.Payments.CountAsync(p => p.SubscriptionId == subscription.SubscriptionId));
    }

    [SkippableFact]
    public async Task Sweep_ExpiresTheLapsedSubscription_AndNotifiesOnce()
    {
        await using var database = await CreateDatabaseAsync();
        var (customerId, plan) = await SeedCustomerAndPlanAsync(database.ConnectionString);
        await using (var seed = TestDatabase.CreateDbContext(database.ConnectionString))
        {
            seed.CustomerSubscriptions.Add(new CustomerSubscription
            {
                SubscriptionId = Guid.NewGuid(), CustomerId = customerId, PlanId = plan.PlanId, Status = "active",
                StartDate = new DateOnly(2026, 9, 2), EndDate = Today.AddDays(-1), NextBillingDate = Today,
                LockedPrice = plan.Price, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        for (var run = 0; run < 2; run++)
        {
            await using var context = TestDatabase.CreateDbContext(database.ConnectionString);
            var notifications = new NotificationService(context, new NoPushSender(), NullLogger<NotificationService>.Instance);
            await new SubscriptionLifecycleService(context, notifications, new NoonOn(Today),
                    Options.Create(new SubscriptionLifecycleOptions()), NullLogger<SubscriptionLifecycleService>.Instance)
                .RunAsync();
        }

        await using var verify = TestDatabase.CreateDbContext(database.ConnectionString);
        Assert.Equal("expired", (await verify.CustomerSubscriptions.SingleAsync()).Status);
        var notification = await verify.Notifications.SingleAsync(n => n.CustomerId == customerId);
        Assert.Equal("announcement", notification.Type);
        Assert.Equal("system", notification.EntityType);
        Assert.Equal("Gói Premium Monthly đã hết hạn", notification.Title);
        var reminder = await verify.SubscriptionReminders.SingleAsync();
        Assert.Equal(("expired", Today.AddDays(-1)), (reminder.Kind, reminder.PeriodEndDate));
    }

    private static async Task DeliverPaidWebhookAsync(string connectionString, long orderCode)
    {
        await using var db = TestDatabase.CreateDbContext(connectionString);
        var time = new NoonOn(Today);
        var handler = new ProcessPayOSWebhookCommandHandler(
            db,
            new PaidGateway(orderCode),
            new SubscriptionPaymentResultService(db, time, NullLogger<SubscriptionPaymentResultService>.Instance),
            NullLogger<ProcessPayOSWebhookCommandHandler>.Instance);
        await handler.Handle(new ProcessPayOSWebhookCommand("""{"code":"00","success":true}"""), CancellationToken.None);
    }

    private static async Task<TestDatabase.DisposableDatabase> CreateDatabaseAsync()
    {
        var database = await TestDatabase.DisposableDatabase.CreateAsync("finviet_subscription_test");
        Skip.If(database is null, TestDatabase.DisposableDatabase.SkipReason);

        await using var context = TestDatabase.CreateDbContext(database!.ConnectionString);
        await DbInitializer.InitializeAsync(
            database.ConnectionString,
            context,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SeedDemoData"] = "false" })
                .Build(),
            new TestDatabase.TestHostEnvironment(Environments.Development),
            NullLogger.Instance);
        return database;
    }

    private static async Task<(Guid CustomerId, SubscriptionPlan Plan)> SeedCustomerAndPlanAsync(string connectionString)
    {
        await using var seed = TestDatabase.CreateDbContext(connectionString);
        var plan = new SubscriptionPlan
        {
            PlanId = Guid.NewGuid(), Code = "premium_monthly", Name = "Premium Monthly",
            Price = 49000m, BillingIntervalMonths = 1, IsActive = true,
        };
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(), Email = $"subscription-{Guid.NewGuid():N}@test.finviet",
            FullName = "Subscription Lifecycle Test", IsActive = true,
        };
        seed.SubscriptionPlans.Add(plan);
        seed.Customers.Add(customer);
        await seed.SaveChangesAsync();
        return (customer.CustomerId, plan);
    }

    private sealed class NoonOn(DateOnly vietnamDate) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(vietnamDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(7)).ToUniversalTime();
    }

    private sealed class NoPushSender : INotificationPushSender
    {
        public Task<NotificationPushResult> SendAsync(IReadOnlyList<NotificationPushDevice> devices,
            NotificationPushMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No devices are registered in this test.");
    }

    /// <summary>Confirms every order as paid, after a short delay so concurrent deliveries
    /// overlap inside their transactions.</summary>
    private sealed class PaidGateway(long orderCode) : IPaymentGateway
    {
        public Task<CreateOrderResult> CreateOrderAsync(long orderCode, int amount, string description,
            DateTimeOffset expiry, string returnUrl, string cancelUrl, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<WebhookVerificationResult> VerifyWebhookAsync(string webhookBody, CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            return new WebhookVerificationResult(orderCode, 49000, Success: true, $"TXN-{orderCode}");
        }

        public Task<PaymentStatusResult> GetOrderStatusAsync(long orderCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConfirmWebhookResult> ConfirmWebhookAsync(string webhookUrl, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
