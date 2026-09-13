using System.Text.Json;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using FCG.Notifications.Function.Dispatching;
using FCG.Notifications.Function.Idempotency;
using FCG.Notifications.Function.Models;
using FCG.Notifications.Function.Services;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace FCG.Notifications.Function;

public sealed class Function
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NotificationEventDispatcher _dispatcher;
    private readonly INotificationIdempotencyStore _idempotencyStore;

    public Function()
        : this(CreateDispatcher(), CreateIdempotencyStore())
    {
    }

    public Function(
        NotificationEventDispatcher dispatcher,
        INotificationIdempotencyStore idempotencyStore)
    {
        _dispatcher = dispatcher;
        _idempotencyStore = idempotencyStore;
    }

    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        var failures = new List<SQSBatchResponse.BatchItemFailure>();
        var records = sqsEvent?.Records ?? [];

        Log(context.Logger, "notification_batch_started", new { recordCount = records.Count });

        foreach (var record in records)
        {
            try
            {
                await ProcessRecordAsync(record, context.Logger, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Log(context.Logger, "notification_processing_failed", new
                {
                    messageId = record.MessageId,
                    errorType = exception.GetType().Name,
                    error = exception.Message
                }, isError: true);
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }

        Log(context.Logger, "notification_batch_finished", new
        {
            recordCount = records.Count,
            failureCount = failures.Count
        });

        return new SQSBatchResponse(failures);
    }

    internal async Task ProcessRecordAsync(
        SQSEvent.SQSMessage record,
        ILambdaLogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.Body))
        {
            throw new InvalidOperationException("SQS message body is required.");
        }

        var envelope = JsonSerializer.Deserialize<NotificationEnvelope>(record.Body, JsonOptions)
            ?? throw new InvalidOperationException("Notification envelope is required.");

        envelope.Validate();

        var acquireResult = await _idempotencyStore.TryAcquireAsync(envelope.Id, cancellationToken);
        if (acquireResult == IdempotencyAcquireResult.Completed)
        {
            Log(logger, "notification_duplicate_ignored", new { envelope.Id, envelope.EventType });
            return;
        }

        if (acquireResult == IdempotencyAcquireResult.Busy)
        {
            throw new InvalidOperationException($"Notification event {envelope.Id} is already being processed.");
        }

        try
        {
            await _dispatcher.DispatchAsync(envelope, logger, cancellationToken);
            await _idempotencyStore.MarkCompletedAsync(envelope.Id, cancellationToken);
        }
        catch (Exception processingException)
        {
            try
            {
                await _idempotencyStore.ReleaseAsync(envelope.Id, cancellationToken);
            }
            catch (Exception releaseException)
            {
                Log(logger, "notification_idempotency_release_failed", new
                {
                    envelope.Id,
                    errorType = releaseException.GetType().Name,
                    error = releaseException.Message
                }, isError: true);
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(processingException).Throw();
            throw;
        }
    }

    private static NotificationEventDispatcher CreateDispatcher() => new(
        new UserCreatedNotificationService(JsonOptions),
        new OrderPlacedNotificationService(JsonOptions),
        new PaymentProcessedNotificationService(JsonOptions));

    private static INotificationIdempotencyStore CreateIdempotencyStore()
    {
        var tableName = Environment.GetEnvironmentVariable("IDEMPOTENCY_TABLE_NAME")
            ?? throw new InvalidOperationException("IDEMPOTENCY_TABLE_NAME is required.");
        var regionName = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
        var retentionDays = int.TryParse(
            Environment.GetEnvironmentVariable("IDEMPOTENCY_RETENTION_DAYS"),
            out var configuredRetentionDays)
            ? configuredRetentionDays
            : 7;

        var client = new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(regionName));
        return new DynamoDbNotificationIdempotencyStore(client, tableName, TimeSpan.FromDays(retentionDays));
    }

    internal static void Log(ILambdaLogger logger, string eventName, object data, bool isError = false)
    {
        var json = JsonSerializer.Serialize(new
        {
            @event = eventName,
            timestamp = DateTimeOffset.UtcNow,
            data
        }, JsonOptions);

        if (isError)
        {
            logger.LogError(json);
        }
        else
        {
            logger.LogInformation(json);
        }
    }
}
