using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using FCG.Notifications.Function.Models;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace FCG.Notifications.Function;

public sealed class Function
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Handler acionado automaticamente por eventos de mensagens na fila SQS.
    /// </summary>
    /// <param name="sqsEvent">Evento com lote de mensagens SQS.</param>
    /// <param name="context">Contexto de execucao da AWS Lambda.</param>
    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        context.Logger.LogInformation($"[INFO] Processando lote com {sqsEvent?.Records?.Count ?? 0} mensagem(ns) SQS.");

        if (sqsEvent?.Records is null || sqsEvent.Records.Count == 0)
        {
            return;
        }

        foreach (var record in sqsEvent.Records)
        {
            await ProcessRecordAsync(record, context);
        }
    }

    public static async Task ProcessRecordAsync(SQSEvent.SQSMessage record, ILambdaContext context)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(record.Body))
            {
                LogWarning(context, record.MessageId, "Mensagem SQS com corpo vazio recebida.");
                return;
            }

            var message = JsonSerializer.Deserialize<OrderPaidMessage>(record.Body, JsonOptions);

            if (message is null || message.OrderId == Guid.Empty || message.UserId == Guid.Empty)
            {
                LogError(context, record.MessageId, "Mensagem malformada ou dados obrigatorios ausentes (OrderId/UserId).", record.Body);
                return;
            }

            var logEvent = new NotificationLogEvent
            {
                Event = "notification_sent",
                OrderId = message.OrderId,
                UserId = message.UserId,
                GameIds = message.GameIds,
                Channel = "email-simulated",
                CorrelationId = message.CorrelationId ?? record.MessageId,
                Timestamp = DateTime.UtcNow,
                Status = "success"
            };

            var structuredLogJson = JsonSerializer.Serialize(logEvent, JsonOptions);
            
            // Grava o log estruturado JSON diretamente no CloudWatch Logs
            context.Logger.LogInformation(structuredLogJson);

            // Simulacao assincrona caso futuramente seja integrado SES/SNS
            await Task.CompletedTask;
        }
        catch (JsonException ex)
        {
            LogError(context, record.MessageId, $"Falha de desserializacao JSON: {ex.Message}", record.Body);
        }
        catch (Exception ex)
        {
            LogError(context, record.MessageId, $"Erro inesperado no processamento: {ex.Message}", record.Body);
            throw; // Re-lanca para acionar a politica de retry e DLQ do SQS
        }
    }

    private static void LogError(ILambdaContext context, string messageId, string reason, string? rawBody)
    {
        var errorPayload = new
        {
            @event = "notification_error",
            messageId,
            reason,
            rawBody,
            timestamp = DateTime.UtcNow,
            status = "error"
        };

        context.Logger.LogError(JsonSerializer.Serialize(errorPayload, JsonOptions));
    }

    private static void LogWarning(ILambdaContext context, string messageId, string reason)
    {
        var warnPayload = new
        {
            @event = "notification_warning",
            messageId,
            reason,
            timestamp = DateTime.UtcNow,
            status = "warning"
        };

        context.Logger.LogWarning(JsonSerializer.Serialize(warnPayload, JsonOptions));
    }
}
