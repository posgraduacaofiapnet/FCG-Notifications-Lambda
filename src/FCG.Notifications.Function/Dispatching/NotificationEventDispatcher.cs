using Amazon.Lambda.Core;
using FCG.Notifications.Function.Models;
using FCG.Notifications.Function.Services;

namespace FCG.Notifications.Function.Dispatching;

public sealed class NotificationEventDispatcher(
    UserCreatedNotificationService userCreatedService,
    OrderPlacedNotificationService orderPlacedService,
    PaymentProcessedNotificationService paymentProcessedService)
{
    public Task DispatchAsync(
        NotificationEnvelope envelope,
        ILambdaLogger logger,
        CancellationToken cancellationToken)
    {
        return envelope.ParseEventType() switch
        {
            NotificationEventType.UserCreated =>
                userCreatedService.ProcessAsync(envelope, logger, cancellationToken),
            NotificationEventType.OrderPlaced =>
                orderPlacedService.ProcessAsync(envelope, logger, cancellationToken),
            NotificationEventType.PaymentProcessed =>
                paymentProcessedService.ProcessAsync(envelope, logger, cancellationToken),
            var eventType => throw new InvalidOperationException(
                $"No notification service is registered for '{eventType}'.")
        };
    }
}
