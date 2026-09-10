using System.Text.Json.Serialization;

namespace FCG.Notifications.Function.Models;

public sealed class OrderPaidMessage
{
    [JsonPropertyName("orderId")]
    public Guid OrderId { get; init; }

    [JsonPropertyName("userId")]
    public Guid UserId { get; init; }

    [JsonPropertyName("gameIds")]
    public IReadOnlyList<Guid> GameIds { get; init; } = [];

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
