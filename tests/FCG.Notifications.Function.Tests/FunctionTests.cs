using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using FCG.Notifications.Function.Models;
using FluentAssertions;
using Xunit;

namespace FCG.Notifications.Function.Tests;

public sealed class FunctionTests
{
    private readonly Function _sut = new();
    private readonly TestLambdaContext _context = new();

    private string GetLogOutput() => ((TestLambdaLogger)_context.Logger).Buffer.ToString();

    [Fact]
    public async Task FunctionHandler_WithValidOrderPaidMessage_LogsStructuredNotification()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        var message = new OrderPaidMessage
        {
            OrderId = orderId,
            UserId = userId,
            GameIds = [gameId],
            CorrelationId = "test-corr-123",
            Timestamp = DateTime.UtcNow
        };

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "msg-001",
                    Body = JsonSerializer.Serialize(message)
                }
            ]
        };

        // Act
        Func<Task> act = async () => await _sut.FunctionHandler(sqsEvent, _context);

        // Assert
        await act.Should().NotThrowAsync();
        var logOutput = GetLogOutput();
        logOutput.Should().Contain("notification_sent");
        logOutput.Should().Contain(orderId.ToString());
        logOutput.Should().Contain(userId.ToString());
        logOutput.Should().Contain("email-simulated");
    }

    [Fact]
    public async Task FunctionHandler_WithNullOrEmptyRecords_DoesNotThrow()
    {
        // Arrange
        var emptyEvent = new SQSEvent { Records = [] };

        // Act
        Func<Task> act = async () => await _sut.FunctionHandler(emptyEvent, _context);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FunctionHandler_WithMalformedJson_LogsErrorAndDoesNotThrow()
    {
        // Arrange
        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "msg-bad-json",
                    Body = "invalid-json-payload-{"
                }
            ]
        };

        // Act
        Func<Task> act = async () => await _sut.FunctionHandler(sqsEvent, _context);

        // Assert
        await act.Should().NotThrowAsync();
        var logOutput = GetLogOutput();
        logOutput.Should().Contain("notification_error");
        logOutput.Should().Contain("msg-bad-json");
    }

    [Fact]
    public async Task FunctionHandler_WithMissingRequiredFields_LogsErrorAndDoesNotThrow()
    {
        // Arrange
        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "msg-missing-fields",
                    Body = """{"gameIds":["00000000-0000-0000-0000-000000000001"]}"""
                }
            ]
        };

        // Act
        Func<Task> act = async () => await _sut.FunctionHandler(sqsEvent, _context);

        // Assert
        await act.Should().NotThrowAsync();
        var logOutput = GetLogOutput();
        logOutput.Should().Contain("notification_error");
        logOutput.Should().Contain("OrderId/UserId");
    }

    [Fact]
    public async Task FunctionHandler_WithMultipleRecordsInBatch_ProcessesAllRecords()
    {
        // Arrange
        var orderId1 = Guid.NewGuid();
        var orderId2 = Guid.NewGuid();

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "msg-batch-1",
                    Body = JsonSerializer.Serialize(new OrderPaidMessage
                    {
                        OrderId = orderId1,
                        UserId = Guid.NewGuid(),
                        GameIds = [Guid.NewGuid()]
                    })
                },
                new SQSEvent.SQSMessage
                {
                    MessageId = "msg-batch-2",
                    Body = JsonSerializer.Serialize(new OrderPaidMessage
                    {
                        OrderId = orderId2,
                        UserId = Guid.NewGuid(),
                        GameIds = [Guid.NewGuid()]
                    })
                }
            ]
        };

        // Act
        await _sut.FunctionHandler(sqsEvent, _context);

        // Assert
        var logOutput = GetLogOutput();
        logOutput.Should().Contain(orderId1.ToString());
        logOutput.Should().Contain(orderId2.ToString());
    }
}
