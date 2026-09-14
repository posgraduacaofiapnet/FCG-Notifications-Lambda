using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FCG.Notifications.Function.Idempotency;
using FluentAssertions;
using Moq;
using Xunit;

namespace FCG.Notifications.Function.Tests;

public sealed class DynamoDbNotificationIdempotencyStoreTests
{
    [Fact]
    public async Task TryAcquireAsync_WhenInsertSucceeds_ReturnsAcquired()
    {
        var client = new Mock<IAmazonDynamoDB>();
        client.Setup(x => x.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());
        var sut = CreateStore(client);

        var result = await sut.TryAcquireAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(IdempotencyAcquireResult.Acquired);
    }

    [Theory]
    [InlineData("Completed", IdempotencyAcquireResult.Completed)]
    [InlineData("Processing", IdempotencyAcquireResult.Busy)]
    [InlineData(null, IdempotencyAcquireResult.Busy)]
    public async Task TryAcquireAsync_WhenEventExists_ReturnsCurrentState(
        string? status,
        IdempotencyAcquireResult expected)
    {
        var client = new Mock<IAmazonDynamoDB>();
        client.Setup(x => x.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("exists"));
        client.Setup(x => x.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = status is null
                    ? []
                    : new Dictionary<string, AttributeValue> { ["Status"] = new() { S = status } }
            });
        var sut = CreateStore(client);

        var result = await sut.TryAcquireAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task MarkCompletedAsync_UpdatesStatusAndRetention()
    {
        var client = new Mock<IAmazonDynamoDB>();
        UpdateItemRequest? captured = null;
        client.Setup(x => x.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new UpdateItemResponse());
        var eventId = Guid.NewGuid();
        var sut = CreateStore(client);

        await sut.MarkCompletedAsync(eventId, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Key["EventId"].S.Should().Be(eventId.ToString("D"));
        captured.ExpressionAttributeValues[":completed"].S.Should().Be("Completed");
    }

    [Fact]
    public async Task ReleaseAsync_DeletesOnlyProcessingEvent()
    {
        var client = new Mock<IAmazonDynamoDB>();
        DeleteItemRequest? captured = null;
        client.Setup(x => x.DeleteItemAsync(It.IsAny<DeleteItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteItemRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new DeleteItemResponse());
        var eventId = Guid.NewGuid();
        var sut = CreateStore(client);

        await sut.ReleaseAsync(eventId, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Key["EventId"].S.Should().Be(eventId.ToString("D"));
        captured.ExpressionAttributeValues[":processing"].S.Should().Be("Processing");
    }

    private static DynamoDbNotificationIdempotencyStore CreateStore(Mock<IAmazonDynamoDB> client) =>
        new(client.Object, "notifications-idempotency", TimeSpan.FromDays(7));
}
