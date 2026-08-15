using System.Net;
using System.Text;
using System.Text.Json;
using FinViet.Application.DTOs.Notifications;
using FinViet.Application.Interfaces;
using FinViet.Application.UnitTests.Infrastructure;
using FinViet.Infrastructure.ExternalServices.Notification;
using FinViet.Infrastructure.Persistence.Entities;
using FinViet.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinViet.Application.UnitTests;

public class NotificationServiceTests
{
    [Fact]
    public async Task RegisterDeviceAsync_IsIdempotentAndRotatesToken()
    {
        await using var db = TestDbContextFactory.Create();
        var service = CreateService(db);
        var customerId = Guid.NewGuid();

        await service.RegisterDeviceAsync(customerId, new RegisterNotificationDeviceRequest
        {
            Token = "ExponentPushToken[first]",
            Platform = "IOS",
            InstallationId = " installation-1 "
        });
        var originalCreatedAt = (await db.NotificationDevices.SingleAsync()).CreatedAt;

        await service.RegisterDeviceAsync(customerId, new RegisterNotificationDeviceRequest
        {
            Token = "ExponentPushToken[second]",
            Platform = "android",
            InstallationId = "installation-1"
        });

        var device = await db.NotificationDevices.SingleAsync();
        Assert.Equal(customerId, device.CustomerId);
        Assert.Equal("installation-1", device.InstallationId);
        Assert.Equal("ExponentPushToken[second]", device.Token);
        Assert.Equal("android", device.Platform);
        Assert.Equal(originalCreatedAt, device.CreatedAt);
        Assert.True(device.UpdatedAt >= device.CreatedAt);
    }

    [Fact]
    public async Task RegisterDeviceAsync_MovesTokenToTheCurrentCustomerAndInstallation()
    {
        await using var db = TestDbContextFactory.Create();
        var service = CreateService(db);
        var previousCustomerId = Guid.NewGuid();
        var currentCustomerId = Guid.NewGuid();
        const string token = "ExponentPushToken[shared]";

        await service.RegisterDeviceAsync(previousCustomerId, new RegisterNotificationDeviceRequest
        {
            Token = token,
            Platform = "ios",
            InstallationId = "old-installation"
        });
        await service.RegisterDeviceAsync(currentCustomerId, new RegisterNotificationDeviceRequest
        {
            Token = token,
            Platform = "android",
            InstallationId = "new-installation"
        });

        var device = await db.NotificationDevices.SingleAsync();
        Assert.Equal(currentCustomerId, device.CustomerId);
        Assert.Equal("new-installation", device.InstallationId);
        Assert.Equal(token, device.Token);
    }

    [Fact]
    public async Task UnregisterDeviceAsync_IsCustomerScopedAndIdempotent()
    {
        await using var db = TestDbContextFactory.Create();
        var service = CreateService(db);
        var ownerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();

        await service.RegisterDeviceAsync(ownerId, new RegisterNotificationDeviceRequest
        {
            Token = "ExponentPushToken[owner]",
            Platform = "ios",
            InstallationId = "owner-installation"
        });

        Assert.False(await service.UnregisterDeviceAsync(otherCustomerId, "owner-installation"));
        Assert.Single(await db.NotificationDevices.ToListAsync());
        Assert.True(await service.UnregisterDeviceAsync(ownerId, " owner-installation "));
        Assert.Empty(await db.NotificationDevices.ToListAsync());
        Assert.False(await service.UnregisterDeviceAsync(ownerId, "owner-installation"));
    }

    [Fact]
    public async Task NotifyAsync_PersistsCanonicalRowAndSendsMatchingPayload()
    {
        await using var db = TestDbContextFactory.Create();
        var pushSender = new Mock<INotificationPushSender>();
        pushSender
            .Setup(sender => sender.SendAsync(
                It.IsAny<IReadOnlyList<NotificationPushDevice>>(),
                It.IsAny<NotificationPushMessage>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationPushResult.Empty);
        var service = CreateService(db, pushSender.Object);
        var customerId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        db.NotificationDevices.Add(new NotificationDevice
        {
            DeviceId = deviceId,
            CustomerId = customerId,
            Token = "ExponentPushToken[device]",
            Platform = "android",
            InstallationId = "installation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await service.NotifyAsync(
            customerId,
            "budget_alert",
            "Budget title",
            "Budget body",
            "budget",
            budgetId);

        var stored = await db.Notifications.SingleAsync();
        Assert.Equal(response.NotificationId, stored.NotificationId);
        Assert.Equal(customerId, stored.CustomerId);
        Assert.Equal("budget_alert", stored.Type);
        Assert.Equal("Budget title", stored.Title);
        Assert.Equal("Budget body", stored.Message);
        Assert.Equal("budget", stored.EntityType);
        Assert.Equal(budgetId, stored.EntityId);
        Assert.False(stored.IsRead);
        pushSender.Verify(sender => sender.SendAsync(
            It.Is<IReadOnlyList<NotificationPushDevice>>(devices =>
                devices.Count == 1 && devices[0].DeviceId == deviceId),
            It.Is<NotificationPushMessage>(message =>
                message.NotificationId == stored.NotificationId
                && message.Type == "budget_alert"
                && message.Title == "Budget title"
                && message.Message == "Budget body"
                && message.EntityType == "budget"
                && message.EntityId == budgetId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyAsync_KeepsCanonicalRowWhenPushFails()
    {
        await using var db = TestDbContextFactory.Create();
        var pushSender = new Mock<INotificationPushSender>();
        pushSender
            .Setup(sender => sender.SendAsync(
                It.IsAny<IReadOnlyList<NotificationPushDevice>>(),
                It.IsAny<NotificationPushMessage>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider unavailable"));
        var service = CreateService(db, pushSender.Object);
        var customerId = Guid.NewGuid();
        db.NotificationDevices.Add(CreateDevice(customerId, "failure"));
        await db.SaveChangesAsync();

        var response = await service.NotifyAsync(
            customerId,
            "announcement",
            "Title",
            "Body");

        var stored = await db.Notifications.SingleAsync();
        Assert.Equal(response.NotificationId, stored.NotificationId);
        Assert.False(stored.IsRead);
    }

    [Fact]
    public async Task NotifyAsync_RemovesOnlyInvalidDevicesReportedByProvider()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var invalid = CreateDevice(customerId, "invalid");
        var valid = CreateDevice(customerId, "valid");
        db.NotificationDevices.AddRange(invalid, valid);
        await db.SaveChangesAsync();
        var pushSender = new Mock<INotificationPushSender>();
        pushSender
            .Setup(sender => sender.SendAsync(
                It.IsAny<IReadOnlyList<NotificationPushDevice>>(),
                It.IsAny<NotificationPushMessage>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPushResult(new[] { invalid.DeviceId }));
        var service = CreateService(db, pushSender.Object);

        await service.NotifyAsync(customerId, "announcement", "Title", "Body");

        var remaining = await db.NotificationDevices.SingleAsync();
        Assert.Equal(valid.DeviceId, remaining.DeviceId);
    }

    [Fact]
    public async Task GetAndMarkOperationsRemainCustomerScoped()
    {
        await using var db = TestDbContextFactory.Create();
        var service = CreateService(db);
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var ownedUnread = CreateNotification(customerId, false, DateTime.UtcNow);
        var ownedRead = CreateNotification(customerId, true, DateTime.UtcNow.AddMinutes(-1));
        var foreignUnread = CreateNotification(otherCustomerId, false, DateTime.UtcNow.AddMinutes(1));
        db.Notifications.AddRange(ownedUnread, ownedRead, foreignUnread);
        await db.SaveChangesAsync();

        var unread = await service.GetNotificationsAsync(customerId, unreadOnly: true);
        Assert.Equal(ownedUnread.NotificationId, Assert.Single(unread).NotificationId);
        Assert.False(await service.MarkAsReadAsync(customerId, foreignUnread.NotificationId));
        Assert.True(await service.MarkAsReadAsync(customerId, ownedUnread.NotificationId));
        Assert.Equal(0, await service.MarkAllAsReadAsync(customerId));
        Assert.False((await db.Notifications.FindAsync(foreignUnread.NotificationId))!.IsRead);
    }

    [Fact]
    public async Task PreferenceNotifiersSuppressCanonicalRowsWhenDisabled()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.CustomerSettings.Add(new CustomerSetting
        {
            CustomerId = customerId,
            NotifBudget = false,
            NotifReport = false,
            NotifGoals = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var notificationService = new Mock<INotificationService>();
        var budgetNotifier = new NotificationBudgetAlertNotifier(db, notificationService.Object);
        var reportNotifier = new NotificationAiReportNotifier(db, notificationService.Object);

        await budgetNotifier.SendBudgetAlertAsync(customerId, Guid.NewGuid(), "Budget", "Body");
        await reportNotifier.SendReportReadyAsync(customerId, Guid.NewGuid(), "Report", "Body");

        notificationService.Verify(service => service.NotifyAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreferenceNotifiersPersistCanonicalMetadataWhenEnabledByDefault()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var notificationService = new Mock<INotificationService>();
        notificationService
            .Setup(service => service.NotifyAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationResponse());
        var budgetNotifier = new NotificationBudgetAlertNotifier(db, notificationService.Object);
        var reportNotifier = new NotificationAiReportNotifier(db, notificationService.Object);

        await budgetNotifier.SendBudgetAlertAsync(customerId, budgetId, "Budget", "Budget body");
        await reportNotifier.SendReportReadyAsync(customerId, reportId, "Report", "Report body");

        notificationService.Verify(service => service.NotifyAsync(
            customerId,
            "budget_alert",
            "Budget",
            "Budget body",
            "budget",
            budgetId,
            It.IsAny<CancellationToken>()), Times.Once);
        notificationService.Verify(service => service.NotifyAsync(
            customerId,
            "weekly_report",
            "Report",
            "Report body",
            "report",
            reportId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExpoSender_IncludesCanonicalDataAndReturnsInvalidDeviceIds()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK, """
            {
              "data": [
                { "status": "ok", "id": "ticket-1" },
                { "status": "error", "details": { "error": "DeviceNotRegistered" } }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://exp.host/--/api/v2/")
        };
        var sender = new ExpoNotificationPushSender(
            httpClient,
            NullLogger<ExpoNotificationPushSender>.Instance);
        var validId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var result = await sender.SendAsync(
            new[]
            {
                new NotificationPushDevice(validId, "ExponentPushToken[valid]"),
                new NotificationPushDevice(invalidId, "ExponentPushToken[invalid]")
            },
            new NotificationPushMessage(
                notificationId,
                "goal_milestone",
                "Goal title",
                "Goal body",
                "goal",
                entityId));

        Assert.Equal(invalidId, Assert.Single(result.InvalidDeviceIds));
        Assert.Equal("https://exp.host/--/api/v2/push/send", handler.RequestUri?.ToString());
        using var document = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var payload = document.RootElement;
        Assert.Equal(2, payload.GetArrayLength());
        var first = payload[0];
        Assert.Equal("ExponentPushToken[valid]", first.GetProperty("to").GetString());
        Assert.Equal("Goal title", first.GetProperty("title").GetString());
        Assert.Equal("Goal body", first.GetProperty("body").GetString());
        var data = first.GetProperty("data");
        Assert.Equal(notificationId.ToString(), data.GetProperty("notificationId").GetString());
        Assert.Equal("goal_milestone", data.GetProperty("type").GetString());
        Assert.Equal("goal", data.GetProperty("entityType").GetString());
        Assert.Equal(entityId.ToString(), data.GetProperty("entityId").GetString());
    }

    private static NotificationService CreateService(
        FinViet.Infrastructure.Persistence.Context.FinVietDbContext db,
        INotificationPushSender? pushSender = null)
    {
        pushSender ??= Mock.Of<INotificationPushSender>(sender =>
            sender.SendAsync(
                It.IsAny<IReadOnlyList<NotificationPushDevice>>(),
                It.IsAny<NotificationPushMessage>(),
                It.IsAny<CancellationToken>()) == Task.FromResult(NotificationPushResult.Empty));

        return new NotificationService(
            db,
            pushSender,
            NullLogger<NotificationService>.Instance);
    }

    private static NotificationDevice CreateDevice(Guid customerId, string suffix)
        => new()
        {
            DeviceId = Guid.NewGuid(),
            CustomerId = customerId,
            Token = $"ExponentPushToken[{suffix}]",
            Platform = "android",
            InstallationId = $"installation-{suffix}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static Notification CreateNotification(Guid customerId, bool isRead, DateTime sentAt)
        => new()
        {
            NotificationId = Guid.NewGuid(),
            CustomerId = customerId,
            Type = "announcement",
            Title = "Title",
            Message = "Body",
            IsRead = isRead,
            CreatedAt = sentAt
        };

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public RecordingHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
