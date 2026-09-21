using FinViet.Application.Common.Exceptions;
using FinViet.Application.DTOs.Subscriptions;
using FinViet.Application.Features.Subscriptions.Commands.ConfirmPayOSWebhook;
using FinViet.Application.Features.Subscriptions.Commands.CreatePayment;
using FinViet.Application.Features.Subscriptions.Queries.GetPaymentStatus;
using FinViet.Application.Features.Subscriptions.Queries.GetCurrentSubscription;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.ExternalServices.PayOS;
using FinViet.Infrastructure.Features.Subscriptions.Commands.ConfirmPayOSWebhook;
using FinViet.Infrastructure.Features.Subscriptions.Commands.CreatePayment;
using FinViet.Infrastructure.Features.Subscriptions.Commands.ProcessPayOSWebhook;
using FinViet.Infrastructure.Features.Subscriptions.Queries.GetPaymentStatus;
using FinViet.Infrastructure.Features.Subscriptions.Queries.GetCurrentSubscription;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinViet.Application.UnitTests;

public class PayOSSubscriptionTests
{
    private static SubscriptionPlan SeedPlan(decimal price = 49000m) => new()
    {
        PlanId = Guid.NewGuid(),
        Code = "premium_monthly",
        Name = "Premium Monthly",
        Price = price,
        BillingIntervalMonths = 1,
        IsActive = true,
    };

    // ── CreatePayment handler ───────────────────────────────────

    [Fact]
    public async Task CreatePayment_CreatesPaymentRow_ReturnsQrCode()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan();
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway("FAKE_QR_DATA", "https://checkout.payos.vn/fake");
        var handler = new CreatePaymentCommandHandler(db, gateway,
            NullLogger<CreatePaymentCommandHandler>.Instance);

        var result = await handler.Handle(
            new CreatePaymentCommand(Guid.NewGuid(), plan.PlanId, null), default);

        Assert.Equal("FAKE_QR_DATA", result.QrCode);
        Assert.Equal(49000m, result.Amount);
        Assert.True(result.OrderCode > 0);

        var payment = db.Payments.Single();
        Assert.Equal("pending", payment.Status);
        Assert.Equal(result.OrderCode, payment.OrderCode);
        Assert.Equal("initial", payment.ChargeType);
    }

    [Fact]
    public async Task CreatePayment_AlreadySubscribed_Throws422()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var plan = SeedPlan();
        db.SubscriptionPlans.Add(plan);
        db.CustomerSubscriptions.Add(new CustomerSubscription
        {
            SubscriptionId = Guid.NewGuid(),
            CustomerId = customerId,
            Status = "active",
            LockedPrice = 49000m,
        });
        await db.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(db,
            new FakePaymentGateway("QR", "URL"),
            NullLogger<CreatePaymentCommandHandler>.Instance);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new CreatePaymentCommand(customerId, plan.PlanId, null), default));
        Assert.Equal("already_subscribed", ex.Code);
    }

    [Fact]
    public async Task CreatePayment_UsesOrderCodeFactory_ForGeneratedCode()
    {
        // Proves the retry loop reads its order code from the injectable factory rather than
        // a locally captured value, which is what lets it draw a fresh code on each attempt.
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan();
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway("FAKE_QR_DATA", "https://checkout.payos.vn/fake");
        var handler = new CreatePaymentCommandHandler(db, gateway,
            NullLogger<CreatePaymentCommandHandler>.Instance)
        {
            OrderCodeFactory = () => 111L,
        };

        var result = await handler.Handle(
            new CreatePaymentCommand(Guid.NewGuid(), plan.PlanId, null), default);

        Assert.Equal(111L, result.OrderCode);
        Assert.Equal(111L, db.Payments.Single().OrderCode);
    }

    [Theory]
    [InlineData(
        "ERROR: 23505: duplicate key value violates unique constraint \"uq_payments_order_code\"",
        true)]
    [InlineData(
        "ERROR: 23505: duplicate key value violates unique constraint \"uq_payments_idempotency_key\"",
        false)]
    [InlineData(
        "ERROR: 23503: insert or update on table \"payments\" violates foreign key constraint",
        false)]
    public void IsOrderCodeCollision_ScopedToOrderCodeConstraint(string innerMessage, bool expected)
    {
        var ex = new Microsoft.EntityFrameworkCore.DbUpdateException(
            "Save failed", new Exception(innerMessage));

        Assert.Equal(expected, CreatePaymentCommandHandler.IsOrderCodeCollision(ex));
    }

    [Fact]
    public async Task CreatePayment_DiscontinuedPlan_Throws422()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan();
        plan.IsActive = false;
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

        var handler = new CreatePaymentCommandHandler(db,
            new FakePaymentGateway("QR", "URL"),
            NullLogger<CreatePaymentCommandHandler>.Instance);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new CreatePaymentCommand(Guid.NewGuid(), plan.PlanId, null), default));
        Assert.Equal("plan_discontinued", ex.Code);
    }

    // ── Webhook handler ─────────────────────────────────────────

    [Fact]
    public async Task Webhook_ValidSuccess_ResolvesPaymentAndCreatesSubscription()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan(59000m);
        var orderCode = 1234567890L;
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            PlanId = plan.PlanId,
            Amount = 59000m,
            ChargeType = "initial",
            Status = "pending",
            OrderCode = orderCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway(webhookResult: new WebhookVerificationResult(
            orderCode, 59000, Success: true, TransactionId: "TXN-001"));
        var resultService = new SubscriptionPaymentResultService(db,
            NullLogger<SubscriptionPaymentResultService>.Instance);
        var handler = new ProcessPayOSWebhookCommandHandler(db, gateway, resultService,
            NullLogger<ProcessPayOSWebhookCommandHandler>.Instance);

        var ok = await handler.Handle(new("""{"code":"00"}"""), default);

        Assert.True(ok);
        Assert.Equal("succeeded", payment.Status);
        Assert.NotNull(payment.SubscriptionId);

        var sub = db.CustomerSubscriptions.Single();
        Assert.Equal("active", sub.Status);
        Assert.Equal(59000m, sub.LockedPrice);
    }

    [Fact]
    public async Task Webhook_DuplicateForResolvedPayment_IsNoOp()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan();
        var orderCode = 9999999999L;
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            PlanId = plan.PlanId,
            Amount = 49000m,
            ChargeType = "initial",
            Status = "succeeded",
            OrderCode = orderCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway(webhookResult: new WebhookVerificationResult(
            orderCode, 49000, Success: true, TransactionId: "TXN-DUP"));
        var resultService = new SubscriptionPaymentResultService(db,
            NullLogger<SubscriptionPaymentResultService>.Instance);
        var handler = new ProcessPayOSWebhookCommandHandler(db, gateway, resultService,
            NullLogger<ProcessPayOSWebhookCommandHandler>.Instance);

        var ok = await handler.Handle(new("""{"code":"00"}"""), default);

        Assert.True(ok);
        Assert.Empty(db.CustomerSubscriptions);
    }

    [Fact]
    public async Task Webhook_UnknownOrderCode_ReturnsTrue()
    {
        await using var db = TestDbContextFactory.Create();

        var gateway = new FakePaymentGateway(webhookResult: new WebhookVerificationResult(
            777777L, 10000, Success: true, TransactionId: null));
        var resultService = new SubscriptionPaymentResultService(db,
            NullLogger<SubscriptionPaymentResultService>.Instance);
        var handler = new ProcessPayOSWebhookCommandHandler(db, gateway, resultService,
            NullLogger<ProcessPayOSWebhookCommandHandler>.Instance);

        var ok = await handler.Handle(new("""{"code":"00"}"""), default);
        Assert.True(ok);
    }

    // ── Payment status poll ─────────────────────────────────────

    private static Payment SeedPendingPayment(Guid customerId, Guid planId, long orderCode, decimal amount = 49000m) => new()
    {
        PaymentId = Guid.NewGuid(),
        CustomerId = customerId,
        PlanId = planId,
        Amount = amount,
        ChargeType = "initial",
        Status = "pending",
        OrderCode = orderCode,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static GetPaymentStatusQueryHandler CreateStatusHandler(
        FinViet.Infrastructure.Persistence.Context.FinVietDbContext db, IPaymentGateway gateway) =>
        new(db, gateway,
            new SubscriptionPaymentResultService(db, NullLogger<SubscriptionPaymentResultService>.Instance),
            NullLogger<GetPaymentStatusQueryHandler>.Instance);

    [Fact]
    public async Task PaymentStatus_OnlyOwnerCanRead()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var orderCode = 5555555555L;
        db.Payments.Add(SeedPendingPayment(customerId, Guid.NewGuid(), orderCode));
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway(orderStatusResult: new PaymentStatusResult(PaymentGatewayStatus.Pending, null));
        var handler = CreateStatusHandler(db, gateway);
        var result = await handler.Handle(new(customerId, orderCode), default);
        Assert.Equal("pending", result.Status);
        Assert.Equal(orderCode, result.OrderCode);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new(Guid.NewGuid(), orderCode), default));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new(customerId, 0L), default));
    }

    [Fact]
    public async Task PaymentStatus_AlreadyTerminal_DoesNotCallGateway()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var orderCode = 6666666666L;
        var payment = SeedPendingPayment(customerId, Guid.NewGuid(), orderCode);
        payment.Status = "succeeded";
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway(orderStatusException: new InvalidOperationException("should not be called"));
        var handler = CreateStatusHandler(db, gateway);

        var result = await handler.Handle(new(customerId, orderCode), default);
        Assert.Equal("succeeded", result.Status);
    }

    [Fact]
    public async Task PaymentStatus_PendingLocally_PaidAtGateway_ResolvesSucceededAndCreatesSubscription()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan(59000m);
        var customerId = Guid.NewGuid();
        var orderCode = 7777777777L;
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(SeedPendingPayment(customerId, plan.PlanId, orderCode, 59000m));
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway(
            orderStatusResult: new PaymentStatusResult(PaymentGatewayStatus.Succeeded, "TXN-RECONCILE"));
        var handler = CreateStatusHandler(db, gateway);

        var result = await handler.Handle(new(customerId, orderCode), default);

        Assert.Equal("succeeded", result.Status);
        Assert.NotNull(result.SubscriptionId);
        Assert.Single(db.CustomerSubscriptions);
    }

    [Theory]
    [InlineData(PaymentGatewayStatus.Failed)]
    public async Task PaymentStatus_PendingLocally_CancelledOrExpiredAtGateway_ResolvesFailed(PaymentGatewayStatus gatewayStatus)
    {
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan();
        var customerId = Guid.NewGuid();
        var orderCode = 8888888888L;
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(SeedPendingPayment(customerId, plan.PlanId, orderCode));
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway(
            orderStatusResult: new PaymentStatusResult(gatewayStatus, null));
        var handler = CreateStatusHandler(db, gateway);

        var result = await handler.Handle(new(customerId, orderCode), default);

        Assert.Equal("failed", result.Status);
        Assert.Empty(db.CustomerSubscriptions);
    }

    [Fact]
    public async Task PaymentStatus_GatewayUnavailable_LeavesPaymentPending()
    {
        await using var db = TestDbContextFactory.Create();
        var plan = SeedPlan();
        var customerId = Guid.NewGuid();
        var orderCode = 9998888888L;
        db.SubscriptionPlans.Add(plan);
        db.Payments.Add(SeedPendingPayment(customerId, plan.PlanId, orderCode));
        await db.SaveChangesAsync();

        var gateway = new FakePaymentGateway(
            orderStatusException: new ExternalServiceException("down", "payos_get_status_failed"));
        var handler = CreateStatusHandler(db, gateway);

        var result = await handler.Handle(new(customerId, orderCode), default);

        Assert.Equal("pending", result.Status);
    }

    // ── Current subscription (unchanged, carried forward) ───────

    [Fact]
    public async Task CurrentSubscription_ReturnsOnlyCustomersActivePlan()
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
            SubscriptionId = Guid.NewGuid(),
            CustomerId = customerId,
            Status = "active",
            LockedPrice = 49000,
        };
        db.CustomerSubscriptions.Add(active);
        await db.SaveChangesAsync();

        var result = await handler.Handle(new(customerId), default);
        Assert.Equal(active.SubscriptionId, result!.SubscriptionId);
        Assert.Equal(49000, result.LockedPrice);
    }

    // ── Validator ────────────────────────────────────────────────

    [Fact]
    public void CreatePayment_ValidatesNonEmptyPlanId()
    {
        var validator = new CreatePaymentCommandValidator();
        Assert.False(validator.Validate(new CreatePaymentCommand(Guid.NewGuid(), Guid.Empty, "key-1")).IsValid);
        Assert.True(validator.Validate(new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), "key-1")).IsValid);
    }

    [Fact]
    public void CreatePayment_ValidatesIdempotencyKeyRequired()
    {
        var validator = new CreatePaymentCommandValidator();
        Assert.False(validator.Validate(new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), null)).IsValid);
        Assert.False(validator.Validate(new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), "")).IsValid);
        Assert.True(validator.Validate(new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), "key-1")).IsValid);
    }

    // ── Confirm webhook (admin) ─────────────────────────────────

    private static ConfirmPayOSWebhookCommandHandler CreateConfirmWebhookHandler(
        IPaymentGateway gateway, string? configuredWebhookUrl) =>
        new(gateway,
            Microsoft.Extensions.Options.Options.Create(new PayOSOptions { WebhookUrl = configuredWebhookUrl }),
            NullLogger<ConfirmPayOSWebhookCommandHandler>.Instance);

    [Fact]
    public async Task ConfirmWebhook_UsesRequestUrl_WhenProvided()
    {
        var gateway = new FakePaymentGateway(confirmWebhookResult: new ConfirmWebhookResult(
            "https://api.finviet.app/api/webhooks/payos", "FINVIET JSC", "0123456789"));
        var handler = CreateConfirmWebhookHandler(gateway, configuredWebhookUrl: "https://configured.example/webhook");

        var result = await handler.Handle(
            new ConfirmPayOSWebhookCommand("https://api.finviet.app/api/webhooks/payos"), default);

        Assert.Equal("https://api.finviet.app/api/webhooks/payos", result.WebhookUrl);
        Assert.Equal("FINVIET JSC", result.AccountName);
    }

    [Fact]
    public async Task ConfirmWebhook_FallsBackToConfiguredUrl_WhenRequestOmitsOne()
    {
        var gateway = new FakePaymentGateway(confirmWebhookResult: new ConfirmWebhookResult(
            "https://configured.example/webhook", "FINVIET JSC", "0123456789"));
        var handler = CreateConfirmWebhookHandler(gateway, configuredWebhookUrl: "https://configured.example/webhook");

        var result = await handler.Handle(new ConfirmPayOSWebhookCommand(null), default);

        Assert.Equal("https://configured.example/webhook", result.WebhookUrl);
    }

    [Fact]
    public async Task ConfirmWebhook_NoUrlAnywhere_Throws400()
    {
        var gateway = new FakePaymentGateway();
        var handler = CreateConfirmWebhookHandler(gateway, configuredWebhookUrl: null);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            handler.Handle(new ConfirmPayOSWebhookCommand(null), default));
    }

    // ── Fake gateway ────────────────────────────────────────────

    private sealed class FakePaymentGateway : IPaymentGateway
    {
        private readonly string _qrCode;
        private readonly string _checkoutUrl;
        private readonly WebhookVerificationResult? _webhookResult;
        private readonly PaymentStatusResult? _orderStatusResult;
        private readonly Exception? _orderStatusException;
        private readonly ConfirmWebhookResult? _confirmWebhookResult;
        private readonly Exception? _confirmWebhookException;

        public FakePaymentGateway(string qrCode = "", string checkoutUrl = "",
            WebhookVerificationResult? webhookResult = null,
            PaymentStatusResult? orderStatusResult = null,
            Exception? orderStatusException = null,
            ConfirmWebhookResult? confirmWebhookResult = null,
            Exception? confirmWebhookException = null)
        {
            _qrCode = qrCode;
            _checkoutUrl = checkoutUrl;
            _webhookResult = webhookResult;
            _orderStatusResult = orderStatusResult;
            _orderStatusException = orderStatusException;
            _confirmWebhookResult = confirmWebhookResult;
            _confirmWebhookException = confirmWebhookException;
        }

        public Task<CreateOrderResult> CreateOrderAsync(
            long orderCode, int amount, string description, DateTimeOffset expiry,
            string returnUrl, string cancelUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CreateOrderResult(_qrCode, _checkoutUrl));

        public Task<WebhookVerificationResult> VerifyWebhookAsync(
            string webhookBody, CancellationToken cancellationToken = default) =>
            Task.FromResult(_webhookResult ?? throw new BadRequestException("No webhook result configured."));

        public Task<PaymentStatusResult> GetOrderStatusAsync(
            long orderCode, CancellationToken cancellationToken = default) =>
            _orderStatusException is not null
                ? Task.FromException<PaymentStatusResult>(_orderStatusException)
                : Task.FromResult(_orderStatusResult ?? throw new BadRequestException("No order status configured."));

        public Task<ConfirmWebhookResult> ConfirmWebhookAsync(
            string webhookUrl, CancellationToken cancellationToken = default) =>
            _confirmWebhookException is not null
                ? Task.FromException<ConfirmWebhookResult>(_confirmWebhookException)
                : Task.FromResult(_confirmWebhookResult ?? throw new BadRequestException("No confirm-webhook result configured."));
    }
}
