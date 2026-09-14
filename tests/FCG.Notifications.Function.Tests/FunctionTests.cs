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

    [Fact]
    public async Task FunctionHandler_WithBusyEvent_ReturnsPartialBatchFailure()
    {
        var context = new TestLambdaContext();
        var store = new ControlledIdempotencyStore { AcquireResult = IdempotencyAcquireResult.Busy };
        var sut = CreateFunction(store);

        var response = await sut.FunctionHandler(
            CreateEvent("busy", SerializeEnvelope("UserCreated", NewUserCreatedPayload())),
            context);

        response.BatchItemFailures.Should().ContainSingle()
            .Which.ItemIdentifier.Should().Be("busy");
    }

    [Fact]
    public async Task FunctionHandler_WhenDispatchFails_ReleasesIdempotencyLease()
    {
        var context = new TestLambdaContext();
        var store = new ControlledIdempotencyStore();
        var sut = CreateFunction(store);

        var response = await sut.FunctionHandler(
            CreateEvent("invalid-payload", SerializeEnvelope("UserCreated", new { })),
            context);

        response.BatchItemFailures.Should().ContainSingle();
        store.Released.Should().ContainSingle();
    }

    [Fact]
    public async Task FunctionHandler_WhenReleaseAlsoFails_LogsReleaseFailure()
    {
        var context = new TestLambdaContext();
        var store = new ControlledIdempotencyStore { ReleaseException = new InvalidOperationException("DynamoDB down") };
        var sut = CreateFunction(store);

        var response = await sut.FunctionHandler(
            CreateEvent("invalid-payload", SerializeEnvelope("UserCreated", new { })),
            context);

        response.BatchItemFailures.Should().ContainSingle();
        ((TestLambdaLogger)context.Logger).Buffer.ToString()
            .Should().Contain("notification_idempotency_release_failed");
    }

    [Fact]
    public async Task FunctionHandler_WithEmptyBatch_CompletesWithoutFailures()
    {
        var response = await CreateFunction(new ControlledIdempotencyStore())
            .FunctionHandler(new SQSEvent(), new TestLambdaContext());

        response.BatchItemFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task FunctionHandler_WithEmptyBody_ReturnsPartialBatchFailure()
    {
        var response = await CreateFunction(new ControlledIdempotencyStore())
            .FunctionHandler(CreateEvent("empty", ""), new TestLambdaContext());

        response.BatchItemFailures.Should().ContainSingle()
            .Which.ItemIdentifier.Should().Be("empty");
    }

    [Fact]
    public void DefaultConstructor_WithoutTableName_ThrowsConfigurationError()
    {
        using var environment = new EnvironmentScope();
        Environment.SetEnvironmentVariable("IDEMPOTENCY_TABLE_NAME", null);

        var action = () => new Function();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("IDEMPOTENCY_TABLE_NAME is required.");
    }

    [Theory]
    [InlineData(null, "invalid")]
    [InlineData("http://localhost:4566", "5")]
    public void DefaultConstructor_WithConfiguration_CreatesFunction(
        string? serviceUrl,
        string retentionDays)
    {
        using var environment = new EnvironmentScope();
        Environment.SetEnvironmentVariable("IDEMPOTENCY_TABLE_NAME", "notifications-test");
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");
        Environment.SetEnvironmentVariable("DYNAMODB_SERVICE_URL", serviceUrl);
        Environment.SetEnvironmentVariable("IDEMPOTENCY_RETENTION_DAYS", retentionDays);

        var sut = new Function();

        sut.Should().NotBeNull();
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

    private static string SerializeEnvelope(string eventType, object payload) => JsonSerializer.Serialize(new
    {
        id = Guid.NewGuid(),
        eventType,
        createdAt = DateTimeOffset.UtcNow,
        payload
    }, JsonOptions);

    private static object NewUserCreatedPayload() => new
    {
        userId = Guid.NewGuid(),
        name = "Ada Lovelace",
        email = "ada@example.com",
        createdAt = DateTimeOffset.UtcNow
    };

    private sealed class EnvironmentScope : IDisposable
    {
        private static readonly string[] VariableNames =
        [
            "IDEMPOTENCY_TABLE_NAME",
            "AWS_REGION",
            "AWS_ACCESS_KEY_ID",
            "AWS_SECRET_ACCESS_KEY",
            "DYNAMODB_SERVICE_URL",
            "IDEMPOTENCY_RETENTION_DAYS"
        ];

        private readonly Dictionary<string, string?> _values = VariableNames
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);

        public void Dispose()
        {
            foreach (var (name, value) in _values)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

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

    private sealed class ControlledIdempotencyStore : INotificationIdempotencyStore
    {
        public IdempotencyAcquireResult AcquireResult { get; init; } = IdempotencyAcquireResult.Acquired;
        public Exception? ReleaseException { get; init; }
        public List<Guid> Released { get; } = [];

        public Task<IdempotencyAcquireResult> TryAcquireAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult(AcquireResult);

        public Task MarkCompletedAsync(Guid eventId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReleaseAsync(Guid eventId, CancellationToken cancellationToken)
        {
            Released.Add(eventId);
            return ReleaseException is null
                ? Task.CompletedTask
                : Task.FromException(ReleaseException);
        }
    }
}
