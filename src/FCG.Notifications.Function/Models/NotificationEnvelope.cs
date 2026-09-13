using System.Text.Json;
using System.Text.Json.Serialization;

namespace FCG.Notifications.Function.Models;

public enum NotificationEventType
{
    UserCreated = 1,
    OrderPlaced = 2,
    PaymentProcessed = 3
}

public sealed class NotificationEnvelope
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }

    public NotificationEventType ParseEventType()
    {
        if (!Enum.TryParse<NotificationEventType>(EventType, true, out var eventType)
            || !Enum.IsDefined(eventType))
        {
            throw new InvalidOperationException($"Unsupported notification event type '{EventType}'.");
        }

        return eventType;
    }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Notification envelope id is required.");
        }

        if (CreatedAt == default)
        {
            throw new InvalidOperationException("Notification envelope creation date is required.");
        }

        if (Payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Notification envelope payload must be a JSON object.");
        }

        _ = ParseEventType();
    }
}

public sealed record UserCreatedPayload(
    Guid UserId,
    string Name,
    string Email,
    DateTimeOffset CreatedAt);

public sealed record OrderPlacedPayload(
    Guid OrderId,
    Guid UserId,
    Guid GameId,
    string GameTitle,
    decimal Price,
    string UserEmail,
    DateTimeOffset PlacedAt);

public sealed record PaymentProcessedPayload(
    Guid OrderId,
    Guid UserId,
    Guid GameId,
    string GameTitle,
    decimal Price,
    string Status,
    string UserEmail,
    DateTimeOffset ProcessedAt);
