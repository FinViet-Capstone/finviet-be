using FinViet.Application.DTOs.Notifications;
using FinViet.Application.DTOs.Users;
using FinViet.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using FinViet.Application.Features.Subscriptions.Commands.CreatePayment;
using FinViet.Application.Features.Users.Queries.GetUsers;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.ExternalServices.PayOS;
using FinViet.Infrastructure.Features.Analytics.Queries.GetAnalyticsSummary;
using FinViet.Infrastructure.Features.Subscriptions.Commands.CreatePayment;
using FinViet.Infrastructure.Features.Subscriptions.Queries.GetCurrentSubscription;
using FinViet.Infrastructure.Features.Users.Queries.GetUsers;
using FinViet.Infrastructure.Persistence.Context;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using FinViet.Infrastructure.Services.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinViet.Application.UnitTests;

/// <summary>finviet-be#138: a paid subscription ends when its paid period ends, the customer is
/// reminded to resubscribe, and paying again always extends rather than failing.</summary>
public class SubscriptionLifecycleTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    // ── Reproduction ────────────────────────────────────────────

    [Fact]
    public async Task Repro138_AfterPaidPeriodEnds_NotActive_AndCustomerCanPayAgain()
    {
        await using var db = TestDbContextFactory.Create();
        var time = FixedTimeProvider.AtVietnamNoon(Today);
        var customerId = Guid.NewGuid();
        var plan = AddPlan(db);
        var payment = AddPendingPayment(db, customerId, plan, orderCode: 1001);
        await db.SaveChangesAsync();

        await ResultService(db, time).ApplyResultAsync(payment, success: true, amount: 49000, "TXN-1", """{"code":"00"}""");

        // The paid month is over (2026-10-03 .. 2026-11-02).
        time.UtcNow = FixedTimeProvider.AtVietnamNoon(new DateOnly(2026, 11, 3)).UtcNow;

        Assert.Null(await new GetCurrentSubscriptionQueryHandler(db, time).Handle(new(customerId), default));
        var order = await CreatePaymentHandler(db).Handle(new CreatePaymentCommand(customerId, plan.PlanId, null), default);
        Assert.Equal("QR", order.QrCode);
    }

    // ── Paying: each payment buys exactly one interval ──────────

    [Fact]
    public async Task FirstPayment_CoversExactlyOneInterval_FromToday()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = AddPlan(db);
        var payment = AddPendingPayment(db, Guid.NewGuid(), plan, orderCode: 1);
        await db.SaveChangesAsync();

        await ResultService(db, FixedTimeProvider.AtVietnamNoon(Today))
            .ApplyResultAsync(payment, success: true, amount: 49000, "TXN", "{}");

        var subscription = db.CustomerSubscriptions.Single();
        Assert.Equal("active", subscription.Status);
        Assert.Equal(Today, subscription.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 2), subscription.EndDate);
        Assert.Equal(new DateOnly(2026, 11, 3), subscription.NextBillingDate);
        Assert.Equal("initial", payment.ChargeType);
        Assert.Equal(subscription.SubscriptionId, payment.SubscriptionId);
    }

    [Fact]
    public async Task PayingWhileActive_ExtendsFromCurrentEndDate_SoNoDaysAreLost()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var plan = AddPlan(db);
        var existing = AddSubscription(db, customerId, plan, "active", start: new DateOnly(2026, 9, 11), end: new DateOnly(2026, 10, 10));
        var payment = AddPendingPayment(db, customerId, plan, orderCode: 2);
        await db.SaveChangesAsync();

        await ResultService(db, FixedTimeProvider.AtVietnamNoon(Today))
            .ApplyResultAsync(payment, success: true, amount: 49000, "TXN", "{}");

        var subscription = db.CustomerSubscriptions.Single();
        Assert.Equal(existing.SubscriptionId, subscription.SubscriptionId);
        Assert.Equal(new DateOnly(2026, 9, 11), subscription.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 10), subscription.EndDate);
        Assert.Equal(new DateOnly(2026, 11, 11), subscription.NextBillingDate);
        Assert.Equal("renewal", payment.ChargeType);
    }

    [Fact]
    public async Task PayingAfterExpiry_ReactivatesTheSameRow_FromToday()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var plan = AddPlan(db);
        var lapsed = AddSubscription(db, customerId, plan, "expired", start: new DateOnly(2026, 8, 21), end: new DateOnly(2026, 9, 20));
        var payment = AddPendingPayment(db, customerId, plan, orderCode: 3);
        await db.SaveChangesAsync();

        await ResultService(db, FixedTimeProvider.AtVietnamNoon(Today))
            .ApplyResultAsync(payment, success: true, amount: 49000, "TXN", "{}");

        var subscription = db.CustomerSubscriptions.Single();
        Assert.Equal(lapsed.SubscriptionId, subscription.SubscriptionId);
        Assert.Equal("active", subscription.Status);
        Assert.Equal(Today, subscription.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 2), subscription.EndDate);
        Assert.Equal("renewal", payment.ChargeType);
    }

    [Fact]
    public async Task ActiveRowWhoseEndHasPassed_IsTreatedAsLapsed_EvenBeforeTheJobExpiresIt()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var plan = AddPlan(db);
        AddSubscription(db, customerId, plan, "active", start: new DateOnly(2026, 8, 21), end: new DateOnly(2026, 9, 20));
        var payment = AddPendingPayment(db, customerId, plan, orderCode: 4);
        await db.SaveChangesAsync();

        await ResultService(db, FixedTimeProvider.AtVietnamNoon(Today))
            .ApplyResultAsync(payment, success: true, amount: 49000, "TXN", "{}");

        var subscription = db.CustomerSubscriptions.Single();
        Assert.Equal(Today, subscription.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 2), subscription.EndDate);
    }

    [Fact]
    public async Task TwoPaidOrders_ForTheSamePeriod_BothSucceed_AndStackTwoIntervals()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var plan = AddPlan(db);
        var first = AddPendingPayment(db, customerId, plan, orderCode: 5);
        var second = AddPendingPayment(db, customerId, plan, orderCode: 6);
        await db.SaveChangesAsync();
        var service = ResultService(db, FixedTimeProvider.AtVietnamNoon(Today));

        await service.ApplyResultAsync(first, success: true, amount: 49000, "TXN-A", "{}");
        await service.ApplyResultAsync(second, success: true, amount: 49000, "TXN-B", "{}");

        Assert.Equal("succeeded", first.Status);
        Assert.Equal("succeeded", second.Status);
        var subscription = db.CustomerSubscriptions.Single();
        Assert.Equal(new DateOnly(2026, 12, 2), subscription.EndDate);
        Assert.Equal(subscription.SubscriptionId, second.SubscriptionId);
    }

    [Fact]
    public async Task PayingForADifferentPlan_ExtendsByThatPlansInterval_AndLocksItsPrice()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var monthly = AddPlan(db);
        var yearly = AddPlan(db, price: 490000m, months: 12, code: "premium_yearly");
        AddSubscription(db, customerId, monthly, "active", start: new DateOnly(2026, 9, 11), end: new DateOnly(2026, 10, 10));
        var payment = AddPendingPayment(db, customerId, yearly, orderCode: 7);
        await db.SaveChangesAsync();

        await ResultService(db, FixedTimeProvider.AtVietnamNoon(Today))
            .ApplyResultAsync(payment, success: true, amount: 490000, "TXN", "{}");

        var subscription = db.CustomerSubscriptions.Single();
        Assert.Equal(new DateOnly(2027, 10, 10), subscription.EndDate);
        Assert.Equal(yearly.PlanId, subscription.PlanId);
        Assert.Equal(490000m, subscription.LockedPrice);
    }

    // ── Readers agree on "premium right now" ───────────────────

    [Fact]
    public async Task CurrentSubscription_CoversTheLastPaidDay_ThenStops()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var plan = AddPlan(db);
        var subscription = AddSubscription(db, customerId, plan, "active", start: new DateOnly(2026, 9, 3), end: Today);
        await db.SaveChangesAsync();

        var onLastDay = await new GetCurrentSubscriptionQueryHandler(db, FixedTimeProvider.AtVietnamNoon(Today))
            .Handle(new(customerId), default);
        Assert.Equal(subscription.SubscriptionId, onLastDay!.SubscriptionId);
        Assert.Equal(Today, onLastDay.ExpiresAt);
        Assert.Equal(Today.AddDays(1), onLastDay.NextBillingDate);

        var dayAfter = await new GetCurrentSubscriptionQueryHandler(db, FixedTimeProvider.AtVietnamNoon(Today.AddDays(1)))
            .Handle(new(customerId), default);
        Assert.Null(dayAfter);
    }

    [Fact]
    public async Task AdminAnalyticsAndUserList_StopCountingALapsedSubscriberAsPremium()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = AddPlan(db);
        var covered = AddCustomer(db, "covered@example.com");
        var lapsed = AddCustomer(db, "lapsed@example.com");
        AddSubscription(db, covered.CustomerId, plan, "active", start: new DateOnly(2026, 9, 20), end: new DateOnly(2026, 10, 19));
        AddSubscription(db, lapsed.CustomerId, plan, "active", start: new DateOnly(2026, 9, 1), end: new DateOnly(2026, 9, 30));
        await db.SaveChangesAsync();
        var time = FixedTimeProvider.AtVietnamNoon(Today);

        var summary = await new GetAnalyticsSummaryQueryHandler(db, time).Handle(new GetAnalyticsSummaryQuery(), default);
        Assert.Equal(1, summary.PremiumSubscriptions);
        Assert.Equal(1, summary.FreeSubscriptions);

        var users = await new GetUsersQueryHandler(db, time).Handle(new GetUsersQuery(new UserQueryDto()), default);
        Assert.Equal("premium_monthly", users.Items.Single(u => u.Email == "covered@example.com").SubscriptionPlanCode);
        Assert.Equal("free", users.Items.Single(u => u.Email == "lapsed@example.com").SubscriptionPlanCode);
    }

    // ── Lifecycle job: expiry + reminders ──────────────────────

    [Fact]
    public async Task Sweep_ExpiresLapsedSubscriptions_AndLeavesCoveredOnesActive()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = AddPlan(db);
        var lapsed = AddSubscription(db, AddCustomer(db).CustomerId, plan, "active", start: new DateOnly(2026, 9, 2), end: Today.AddDays(-1));
        var lastDay = AddSubscription(db, AddCustomer(db).CustomerId, plan, "active", start: new DateOnly(2026, 9, 4), end: Today);
        await db.SaveChangesAsync();

        await Lifecycle(db, FixedTimeProvider.AtVietnamNoon(Today), new RecordingNotifications(db)).RunAsync();

        Assert.Equal("expired", db.CustomerSubscriptions.Single(s => s.SubscriptionId == lapsed.SubscriptionId).Status);
        Assert.Equal("active", db.CustomerSubscriptions.Single(s => s.SubscriptionId == lastDay.SubscriptionId).Status);
    }

    [Fact]
    public async Task Sweep_RemindsBeforeOnAndAfterTheEndDate_EachExactlyOncePerPeriod()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = AddCustomer(db);
        var plan = AddPlan(db);
        var end = new DateOnly(2026, 10, 10);
        AddSubscription(db, customer.CustomerId, plan, "active", start: new DateOnly(2026, 9, 11), end: end);
        await db.SaveChangesAsync();
        var notifications = new RecordingNotifications(db);

        // Twice a day from a week before the end until a week after it.
        for (var day = end.AddDays(-7); day <= end.AddDays(7); day = day.AddDays(1))
        {
            var time = FixedTimeProvider.AtVietnamNoon(day);
            await Lifecycle(db, time, notifications).RunAsync();
            await Lifecycle(db, time, notifications).RunAsync();
        }

        Assert.Collection(notifications.Sent,
            n => Assert.Equal(("Gói Premium Monthly sắp hết hạn", new DateOnly(2026, 10, 7)), (n.Title, n.Day)),
            n => Assert.Equal(("Gói Premium Monthly hết hạn hôm nay", end), (n.Title, n.Day)),
            n => Assert.Equal(("Gói Premium Monthly đã hết hạn", end.AddDays(1)), (n.Title, n.Day)));
        Assert.All(notifications.Sent, n =>
        {
            Assert.Equal(customer.CustomerId, n.CustomerId);
            Assert.Equal("announcement", n.Type);
            Assert.Equal("system", n.EntityType);
        });
        Assert.Contains("10/10/2026", notifications.Sent[0].Message);
    }

    [Fact]
    public async Task Sweep_AfterARenewal_RemindsAgainForTheNewPeriod()
    {
        await using var db = TestDbContextFactory.Create();
        var customer = AddCustomer(db);
        var plan = AddPlan(db);
        var subscription = AddSubscription(db, customer.CustomerId, plan, "active", start: new DateOnly(2026, 9, 6), end: new DateOnly(2026, 10, 5));
        await db.SaveChangesAsync();
        var notifications = new RecordingNotifications(db);

        await Lifecycle(db, FixedTimeProvider.AtVietnamNoon(Today), notifications).RunAsync();

        var tracked = db.CustomerSubscriptions.Single();
        tracked.EndDate = new DateOnly(2026, 11, 5);
        tracked.NextBillingDate = new DateOnly(2026, 11, 6);
        await db.SaveChangesAsync();
        await Lifecycle(db, FixedTimeProvider.AtVietnamNoon(new DateOnly(2026, 11, 3)), notifications).RunAsync();

        Assert.Equal(2, notifications.Sent.Count(n => n.Title.EndsWith("sắp hết hạn")));
        Assert.Equal(subscription.SubscriptionId, notifications.Sent[1].EntityId);
    }

    [Fact]
    public async Task Sweep_AtNight_ExpiresButHoldsRemindersUntilMorning()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = AddPlan(db);
        AddSubscription(db, AddCustomer(db).CustomerId, plan, "active", start: new DateOnly(2026, 9, 2), end: Today.AddDays(-1));
        await db.SaveChangesAsync();
        var notifications = new RecordingNotifications(db);
        var twoAm = new FixedTimeProvider(new DateTimeOffset(Today.ToDateTime(new TimeOnly(2, 0)), TimeSpan.FromHours(7)));

        await Lifecycle(db, twoAm, notifications).RunAsync();

        Assert.Equal("expired", db.CustomerSubscriptions.Single().Status);
        Assert.Empty(notifications.Sent);

        await Lifecycle(db, FixedTimeProvider.AtVietnamNoon(Today), notifications).RunAsync();
        Assert.Equal("Gói Premium Monthly đã hết hạn", Assert.Single(notifications.Sent).Title);
    }

    [Fact]
    public async Task Sweep_SkipsLockedAccounts_AndLapsesOlderThanTheReminderWindow()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = AddPlan(db);
        var locked = AddCustomer(db, "locked@example.com", isActive: false);
        AddSubscription(db, locked.CustomerId, plan, "active", start: new DateOnly(2026, 9, 5), end: Today.AddDays(1));
        AddSubscription(db, AddCustomer(db).CustomerId, plan, "expired", start: new DateOnly(2026, 7, 1), end: new DateOnly(2026, 7, 31));
        await db.SaveChangesAsync();
        var notifications = new RecordingNotifications(db);

        await Lifecycle(db, FixedTimeProvider.AtVietnamNoon(Today), notifications).RunAsync();

        Assert.Empty(notifications.Sent);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static SubscriptionPaymentResultService ResultService(FinVietDbContext db, TimeProvider time) =>
        new(db, time, NullLogger<SubscriptionPaymentResultService>.Instance);

    private static CreatePaymentCommandHandler CreatePaymentHandler(FinVietDbContext db) =>
        new(db, new FakePaymentGateway("QR", "https://checkout"),
            Options.Create(new PayOSOptions { ReturnUrl = "https://example.com/r", CancelUrl = "https://example.com/c" }),
            NullLogger<CreatePaymentCommandHandler>.Instance);

    private static SubscriptionLifecycleService Lifecycle(
        FinVietDbContext db, FixedTimeProvider time, RecordingNotifications notifications)
    {
        notifications.Time = time;
        return new SubscriptionLifecycleService(db, notifications, time,
            Options.Create(new SubscriptionLifecycleOptions()), NullLogger<SubscriptionLifecycleService>.Instance);
    }

    private static SubscriptionPlan AddPlan(
        FinVietDbContext db, decimal price = 49000m, short months = 1, string code = "premium_monthly")
    {
        var plan = new SubscriptionPlan
        {
            PlanId = Guid.NewGuid(),
            Code = code,
            Name = months == 12 ? "Premium Yearly" : "Premium Monthly",
            Price = price,
            BillingIntervalMonths = months,
            IsActive = true,
        };
        db.SubscriptionPlans.Add(plan);
        return plan;
    }

    private static Customer AddCustomer(FinVietDbContext db, string email = "customer@example.com", bool isActive = true)
    {
        var customer = TestData.Customer(email: email, isActive: isActive);
        db.Customers.Add(customer);
        return customer;
    }

    private static CustomerSubscription AddSubscription(
        FinVietDbContext db, Guid customerId, SubscriptionPlan plan, string status, DateOnly start, DateOnly end)
    {
        var subscription = new CustomerSubscription
        {
            SubscriptionId = Guid.NewGuid(),
            CustomerId = customerId,
            PlanId = plan.PlanId,
            Plan = plan,
            Status = status,
            StartDate = start,
            EndDate = end,
            NextBillingDate = end.AddDays(1),
            LockedPrice = plan.Price,
            CreatedAt = DateTime.UtcNow,
        };
        db.CustomerSubscriptions.Add(subscription);
        return subscription;
    }

    private static Payment AddPendingPayment(FinVietDbContext db, Guid customerId, SubscriptionPlan plan, long orderCode)
    {
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            CustomerId = customerId,
            PlanId = plan.PlanId,
            Amount = plan.Price,
            ChargeType = "initial",
            Status = "pending",
            OrderCode = orderCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Payments.Add(payment);
        return payment;
    }

    /// <summary>Records reminders and, like the real service, saves the context (which is what
    /// commits the reminder claim the lifecycle service added just before).</summary>
    private sealed class RecordingNotifications(FinVietDbContext db) : INotificationService
    {
        public FixedTimeProvider? Time { get; set; }

        public List<(Guid CustomerId, string Type, string Title, string Message, string? EntityType, Guid? EntityId, DateOnly Day)> Sent { get; } = [];

        public async Task<NotificationResponse> NotifyAsync(Guid customerId, string type, string title, string message,
            string? entityType = null, Guid? entityId = null, CancellationToken cancellationToken = default)
        {
            await db.SaveChangesAsync(cancellationToken);
            Sent.Add((customerId, type, title, message, entityType, entityId, VietnamClock.Today(Time!)));
            return new NotificationResponse();
        }

        public Task RegisterDeviceAsync(Guid customerId, RegisterNotificationDeviceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UnregisterDeviceAsync(Guid customerId, string installationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<NotificationResponse>> GetNotificationsAsync(Guid customerId, bool unreadOnly = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkAsReadAsync(Guid customerId, Guid notificationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> MarkAllAsReadAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
