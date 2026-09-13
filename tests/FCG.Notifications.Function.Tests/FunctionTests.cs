using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using FCG.Notifications.Function.Dispatching;
using FCG.Notifications.Function.Idempotency;
using FCG.Notifications.Function.Services;
using FluentAssertions;
using Xunit;

namespace FCG.Notifications.Function.Tests;

public sealed class FunctionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [MemberData(nameof(ValidEvents))]
    public async Task FunctionHandler_WithSupportedEvent_ProcessesNotification(
        string eventType,
        object payload,
        string expectedLogEvent)
    {
        var context = new TestLambdaContext();
        var store = new InMemoryIdempotencyStore();
        var sut = CreateFunction(store);
        var eventId = Guid.NewGuid();
        var envelope = new
        {
            id = eventId,
            eventType,
            createdAt = DateTimeOffset.UtcNow,
            payload
        };

        var response = await sut.FunctionHandler(
            CreateEvent("message-1", JsonSerializer.Serialize(envelope, JsonOptions)),
            context);

        response.BatchItemFailures.Should().BeEmpty();
        ((TestLambdaLogger)context.Logger).Buffer.ToString().Should().Contain(expectedLogEvent);
        store.Completed.Should().Contain(eventId);
    }

    [Fact]
    public async Task FunctionHandler_WithMalformedJson_ReturnsPartialBatchFailure()
    {
        var context = new TestLambdaContext();
        var sut = CreateFunction(new InMemoryIdempotencyStore());

        var response = await sut.FunctionHandler(CreateEvent("bad-message", "not-json"), context);

        response.BatchItemFailures.Should().ContainSingle()
            .Which.ItemIdentifier.Should().Be("bad-message");
        ((TestLambdaLogger)context.Logger).Buffer.ToString()
            .Should().Contain("notification_processing_failed");
    }

    [Fact]
    public async Task FunctionHandler_WithCompletedEvent_IgnoresDuplicate()
    {
        var context = new TestLambdaContext();
        var eventId = Guid.NewGuid();
        var store = new InMemoryIdempotencyStore();
        store.Completed.Add(eventId);
        var sut = CreateFunction(store);
        var envelope = new
        {
            id = eventId,
            eventType = "UserCreated",
            createdAt = DateTimeOffset.UtcNow,
            payload = NewUserCreatedPayload()
        };

        var response = await sut.FunctionHandler(
            CreateEvent("duplicate", JsonSerializer.Serialize(envelope, JsonOptions)),
            context);

        response.BatchItemFailures.Should().BeEmpty();
        ((TestLambdaLogger)context.Logger).Buffer.ToString()
            .Should().Contain("notification_duplicate_ignored");
    }

    public static IEnumerable<object[]> ValidEvents()
    {
        yield return ["UserCreated", NewUserCreatedPayload(), "welcome_email_sent"];
        yield return ["OrderPlaced", NewOrderPlacedPayload(), "order_received_email_sent"];
        yield return ["PaymentProcessed", NewPaymentProcessedPayload(), "payment_result_email_sent"];
    }

    private static Function CreateFunction(INotificationIdempotencyStore store) => new(
        new NotificationEventDispatcher(
            new UserCreatedNotificationService(JsonOptions),
            new OrderPlacedNotificationService(JsonOptions),
            new PaymentProcessedNotificationService(JsonOptions)),
        store);

    private static SQSEvent CreateEvent(string messageId, string body) => new()
    {
        Records =
        [
            new SQSEvent.SQSMessage
            {
                MessageId = messageId,
                Body = body
            }
        ]
    };

    private static object NewUserCreatedPayload() => new
    {
        userId = Guid.NewGuid(),
        name = "Ada Lovelace",
        email = "ada@example.com",
        createdAt = DateTimeOffset.UtcNow
    };

    private static object NewOrderPlacedPayload() => new
    {
        orderId = Guid.NewGuid(),
        userId = Guid.NewGuid(),
        gameId = Guid.NewGuid(),
        gameTitle = "FCG Game",
        price = 49.90m,
        userEmail = "player@example.com",
        placedAt = DateTimeOffset.UtcNow
    };

    private static object NewPaymentProcessedPayload() => new
    {
        orderId = Guid.NewGuid(),
        userId = Guid.NewGuid(),
        gameId = Guid.NewGuid(),
        gameTitle = "FCG Game",
        price = 49.90m,
        status = "Approved",
        userEmail = "player@example.com",
        processedAt = DateTimeOffset.UtcNow
    };

    private sealed class InMemoryIdempotencyStore : INotificationIdempotencyStore
    {
        public HashSet<Guid> Completed { get; } = [];
        private HashSet<Guid> Processing { get; } = [];

        public Task<IdempotencyAcquireResult> TryAcquireAsync(
            Guid eventId,
            CancellationToken cancellationToken)
        {
            if (Completed.Contains(eventId))
            {
                return Task.FromResult(IdempotencyAcquireResult.Completed);
            }

            return Task.FromResult(Processing.Add(eventId)
                ? IdempotencyAcquireResult.Acquired
                : IdempotencyAcquireResult.Busy);
        }

        public Task MarkCompletedAsync(Guid eventId, CancellationToken cancellationToken)
        {
            Processing.Remove(eventId);
            Completed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid eventId, CancellationToken cancellationToken)
        {
            Processing.Remove(eventId);
            return Task.CompletedTask;
        }
    }
}
