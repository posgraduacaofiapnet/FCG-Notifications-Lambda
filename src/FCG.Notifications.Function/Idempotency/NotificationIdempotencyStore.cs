using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace FCG.Notifications.Function.Idempotency;

public enum IdempotencyAcquireResult
{
    Acquired,
    Completed,
    Busy
}

public interface INotificationIdempotencyStore
{
    Task<IdempotencyAcquireResult> TryAcquireAsync(Guid eventId, CancellationToken cancellationToken);
    Task MarkCompletedAsync(Guid eventId, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid eventId, CancellationToken cancellationToken);
}

public sealed class DynamoDbNotificationIdempotencyStore(
    IAmazonDynamoDB client,
    string tableName,
    TimeSpan retention) : INotificationIdempotencyStore
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

    public async Task<IdempotencyAcquireResult> TryAcquireAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = tableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["EventId"] = new() { S = eventId.ToString("D") },
                    ["Status"] = new() { S = "Processing" },
                    ["ExpiresAt"] = new() { N = now.Add(ProcessingLease).ToUnixTimeSeconds().ToString() }
                },
                ConditionExpression = "attribute_not_exists(EventId) OR ExpiresAt < :now",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":now"] = new() { N = now.ToUnixTimeSeconds().ToString() }
                }
            }, cancellationToken);
            return IdempotencyAcquireResult.Acquired;
        }
        catch (ConditionalCheckFailedException)
        {
            var current = await client.GetItemAsync(new GetItemRequest
            {
                TableName = tableName,
                Key = Key(eventId),
                ConsistentRead = true
            }, cancellationToken);

            return current.Item.TryGetValue("Status", out var status) && status.S == "Completed"
                ? IdempotencyAcquireResult.Completed
                : IdempotencyAcquireResult.Busy;
        }
    }

    public Task MarkCompletedAsync(Guid eventId, CancellationToken cancellationToken) =>
        client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = tableName,
            Key = Key(eventId),
            UpdateExpression = "SET #status = :completed, ExpiresAt = :expiresAt",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":completed"] = new() { S = "Completed" },
                [":expiresAt"] = new() { N = DateTimeOffset.UtcNow.Add(retention).ToUnixTimeSeconds().ToString() }
            }
        }, cancellationToken);

    public Task ReleaseAsync(Guid eventId, CancellationToken cancellationToken) =>
        client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = tableName,
            Key = Key(eventId),
            ConditionExpression = "#status = :processing",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":processing"] = new() { S = "Processing" }
            }
        }, cancellationToken);

    private static Dictionary<string, AttributeValue> Key(Guid eventId) => new()
    {
        ["EventId"] = new() { S = eventId.ToString("D") }
    };
}
