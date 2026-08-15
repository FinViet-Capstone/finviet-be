using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FinViet.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinViet.Infrastructure.ExternalServices.Notification;

public class ExpoNotificationPushSender : INotificationPushSender
{
    private const int MaxBatchSize = 100;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExpoNotificationPushSender> _logger;

    public ExpoNotificationPushSender(
        HttpClient httpClient,
        ILogger<ExpoNotificationPushSender> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<NotificationPushResult> SendAsync(
        IReadOnlyList<NotificationPushDevice> devices,
        NotificationPushMessage message,
        CancellationToken cancellationToken = default)
    {
        if (devices.Count == 0)
            return NotificationPushResult.Empty;

        var invalidDeviceIds = new HashSet<Guid>();

        foreach (var batch in devices.Chunk(MaxBatchSize))
        {
            var payload = batch.Select(device => new ExpoPushRequest
            {
                To = device.Token,
                Title = message.Title,
                Body = message.Message,
                Sound = "default",
                Data = new Dictionary<string, string?>
                {
                    ["notificationId"] = message.NotificationId.ToString(),
                    ["type"] = message.Type,
                    ["entityType"] = message.EntityType,
                    ["entityId"] = message.EntityId?.ToString()
                }
            }).ToArray();

            try
            {
                using var response = await _httpClient.PostAsJsonAsync("push/send", payload, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Expo push request failed with status {StatusCode} for {DeviceCount} devices.",
                        (int)response.StatusCode,
                        batch.Length);
                    continue;
                }

                var result = await response.Content.ReadFromJsonAsync<ExpoPushResponse>(
                    cancellationToken: cancellationToken);
                if (result?.Data is null)
                    continue;

                for (var index = 0; index < Math.Min(batch.Length, result.Data.Count); index++)
                {
                    var ticket = result.Data[index];
                    if (string.Equals(ticket.Status, "error", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(ticket.Details?.Error, "DeviceNotRegistered", StringComparison.Ordinal))
                    {
                        invalidDeviceIds.Add(batch[index].DeviceId);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Expo push request failed for {DeviceCount} devices.", batch.Length);
            }
        }

        return new NotificationPushResult(invalidDeviceIds);
    }

    private sealed class ExpoPushRequest
    {
        [JsonPropertyName("to")]
        public string To { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; init; } = string.Empty;

        [JsonPropertyName("sound")]
        public string Sound { get; init; } = "default";

        [JsonPropertyName("data")]
        public Dictionary<string, string?> Data { get; init; } = new();
    }

    private sealed class ExpoPushResponse
    {
        [JsonPropertyName("data")]
        public List<ExpoPushTicket>? Data { get; init; }
    }

    private sealed class ExpoPushTicket
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("details")]
        public ExpoPushDetails? Details { get; init; }
    }

    private sealed class ExpoPushDetails
    {
        [JsonPropertyName("error")]
        public string? Error { get; init; }
    }
}
