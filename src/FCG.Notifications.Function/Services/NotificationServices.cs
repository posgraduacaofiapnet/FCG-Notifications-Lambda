using System.Text.Json;
using Amazon.Lambda.Core;
using FCG.Notifications.Function.Models;

namespace FCG.Notifications.Function.Services;

public interface INotificationService
{
    NotificationEventType EventType { get; }
    Task ProcessAsync(NotificationEnvelope envelope, ILambdaLogger logger, CancellationToken cancellationToken);
}

public abstract class NotificationService<TPayload>(JsonSerializerOptions jsonOptions) : INotificationService
{
    public abstract NotificationEventType EventType { get; }

    public Task ProcessAsync(
        NotificationEnvelope envelope,
        ILambdaLogger logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = envelope.Payload.Deserialize<TPayload>(jsonOptions)
            ?? throw new InvalidOperationException($"Payload for '{EventType}' is required.");
        return ProcessAsync(payload, envelope, logger, cancellationToken);
    }

    protected abstract Task ProcessAsync(
        TPayload payload,
        NotificationEnvelope envelope,
        ILambdaLogger logger,
        CancellationToken cancellationToken);
}

public sealed class UserCreatedNotificationService(JsonSerializerOptions jsonOptions)
    : NotificationService<UserCreatedPayload>(jsonOptions)
{
    public override NotificationEventType EventType => NotificationEventType.UserCreated;

    protected override Task ProcessAsync(
        UserCreatedPayload payload,
        NotificationEnvelope envelope,
        ILambdaLogger logger,
        CancellationToken cancellationToken)
    {
        if (payload.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(payload.Name)
            || string.IsNullOrWhiteSpace(payload.Email)
            || payload.CreatedAt == default)
        {
            throw new InvalidOperationException("UserCreated payload is incomplete.");
        }

        Function.Log(logger, "welcome_email_sent", new
        {
            envelope.Id,
            payload.UserId,
            payload.Email,
            channel = "email-simulated",
            status = "success"
        });
        return Task.CompletedTask;
    }
}

public sealed class OrderPlacedNotificationService(JsonSerializerOptions jsonOptions)
    : NotificationService<OrderPlacedPayload>(jsonOptions)
{
    public override NotificationEventType EventType => NotificationEventType.OrderPlaced;

    protected override Task ProcessAsync(
        OrderPlacedPayload payload,
        NotificationEnvelope envelope,
        ILambdaLogger logger,
        CancellationToken cancellationToken)
    {
        if (payload.OrderId == Guid.Empty
            || payload.UserId == Guid.Empty
            || payload.GameId == Guid.Empty
            || string.IsNullOrWhiteSpace(payload.GameTitle)
            || payload.Price < 0
            || string.IsNullOrWhiteSpace(payload.UserEmail)
            || payload.PlacedAt == default)
        {
            throw new InvalidOperationException("OrderPlaced payload is incomplete.");
        }

        Function.Log(logger, "order_received_email_sent", new
        {
            envelope.Id,
            payload.OrderId,
            payload.UserId,
            payload.GameId,
            payload.UserEmail,
            channel = "email-simulated",
            status = "success"
        });
        return Task.CompletedTask;
    }
}

public sealed class PaymentProcessedNotificationService(JsonSerializerOptions jsonOptions)
    : NotificationService<PaymentProcessedPayload>(jsonOptions)
{
    public override NotificationEventType EventType => NotificationEventType.PaymentProcessed;

    protected override Task ProcessAsync(
        PaymentProcessedPayload payload,
        NotificationEnvelope envelope,
        ILambdaLogger logger,
        CancellationToken cancellationToken)
    {
        if (payload.OrderId == Guid.Empty
            || payload.UserId == Guid.Empty
            || payload.GameId == Guid.Empty
            || string.IsNullOrWhiteSpace(payload.GameTitle)
            || payload.Price < 0
            || string.IsNullOrWhiteSpace(payload.UserEmail)
            || payload.ProcessedAt == default)
        {
            throw new InvalidOperationException("PaymentProcessed payload is incomplete.");
        }

        if (payload.Status is not ("Approved" or "Rejected"))
        {
            throw new InvalidOperationException($"Unsupported payment status '{payload.Status}'.");
        }

        Function.Log(logger, "payment_result_email_sent", new
        {
            envelope.Id,
            payload.OrderId,
            payload.UserId,
            payload.GameId,
            payload.UserEmail,
            paymentStatus = payload.Status,
            channel = "email-simulated",
            status = "success"
        });
        return Task.CompletedTask;
    }
}
