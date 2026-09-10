using System.Text.Json.Serialization;

namespace FCG.Notifications.Function.Models;

public sealed class NotificationLogEvent
{
    [JsonPropertyName("event")]
    public string Event { get; init; } = "notification_sent";

    [JsonPropertyName("orderId")]
    public Guid OrderId { get; init; }

    [JsonPropertyName("userId")]
    public Guid UserId { get; init; }

    [JsonPropertyName("gameIds")]
    public IReadOnlyList<Guid> GameIds { get; init; } = [];

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "email-simulated";

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "success";
}
