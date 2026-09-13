# FCG Notifications Lambda

Função AWS Lambda .NET 8 da Fase 3 acionada pela fila `fcg-notifications-queue`. Ela recebe um
envelope único e encaminha cada evento por um `switch` de enum para um serviço separado.

## Contrato

```json
{
  "id": "0b61fe94-4adb-4fb3-9935-19192c23d51c",
  "eventType": "UserCreated",
  "createdAt": "2026-09-13T12:00:00Z",
  "payload": {
    "userId": "b8dd0efe-4187-480a-b197-a81a39f98839",
    "name": "Ada Lovelace",
    "email": "ada@example.com",
    "createdAt": "2026-09-13T12:00:00Z"
  }
}
```

Eventos suportados:

| Valor | `EventType` | Serviço |
|---:|---|---|
| 1 | `UserCreated` | E-mail de boas-vindas |
| 2 | `OrderPlaced` | Confirmação de pedido recebido |
| 3 | `PaymentProcessed` | Resultado aprovado ou rejeitado |

O worker é deliberadamente genérico. O parse do texto para o enum e o dispatch ficam nesta função.

## Confiabilidade

- Resposta parcial de lote (`ReportBatchItemFailures`) evita repetir itens bem-sucedidos.
- DynamoDB usa `id` como chave idempotente e possui lease de processamento.
- Itens concluídos expiram pelo TTL configurado.
- SQS possui DLQ e política de redrive.
- Logs são JSON estruturado e não gravam o payload completo.

## Deploy

```bash
sam build
sam deploy --guided
```

O `template.yaml` cria fila, DLQ, tabela DynamoDB, permissões e trigger. Os produtores devem usar
o output `NotificationsQueueUrl` na configuração do `FCG-Outbox-Processor`.
