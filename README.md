# FCG Notifications Serverless Function (AWS Lambda + SQS)

Repositório independente do serviço de **Notificações Serverless** do **FCG (FIAP Cloud Games)** — Fase 3 do Tech Challenge.

---

## 🎯 1. Visão Geral e Motivação Arquitetural

Na arquitetura anterior, o serviço `NotificationsAPI` executava como um container 24/7 (mesmo quando não havia pagamentos ou notificações a serem emitidas), gerando custos desnecessários de infraestrutura e ociosidade computacional.

Nesta **Fase 3**, o serviço foi refatorado para uma **Função Serverless (AWS Lambda)** disparada orientada a eventos (*Event-Driven Architecture*) via fila **Amazon SQS**:
- **Custo Zero em Ociosidade**: A função executa e consome recursos estritamente quando há novas mensagens na fila.
- **Escalabilidade Automática**: Processamento concorrente sob picos de compras/pedidos.
- **Resiliência e Tolerância a Falhas**: Configuração nativa de tentativas (*retries*) e Dead Letter Queue (**DLQ**) para isolar falhas sem perder mensagens.

---

## 🏗️ 2. Arquitetura do Trigger

```text
+-----------------------+           +--------------------------+
|  FCG API / Monolito   |           |  Amazon SQS              |
|  (OrderService)       | --------> |  (fcg-notifications-     |
|  Aprova Pagamento     |           |   queue)                 |
+-----------------------+           +--------------------------+
                                                 |
                                                 | (Trigger automático)
                                                 v
+-----------------------+           +--------------------------+
|  CloudWatch Logs      |           |  AWS Lambda (.NET 10)    |
|  Log estruturado JSON | <-------- |  (fcg-notifications-     |
|  (notification_sent)  |           |   function)              |
+-----------------------+           +--------------------------+
```

---

## 📦 3. Estrutura do Repositório

```text
FCG-Notifications-Lambda/
├── src/
│   └── FCG.Notifications.Function/         # Handler .NET 10 da AWS Lambda
│       ├── Function.cs                     # Lógica de processamento e logs JSON
│       ├── Models/                         # DTOs de mensagens e logs
│       └── FCG.Notifications.Function.csproj
├── tests/
│   └── FCG.Notifications.Function.Tests/   # Testes unitários com xUnit e FluentAssertions
├── template.yaml                           # Infraestrutura como Código (AWS SAM)
├── README.md                               # Documentação e instruções de operação
└── .gitignore
```

---

## 🚀 4. Pré-requisitos

1. **.NET 10 SDK** instalado.
2. **AWS CLI** instalado e configurado (`aws configure`).
3. **AWS SAM CLI** instalado (`brew install aws-sam-cli`).

---

## 🛠️ 5. Como Executar os Testes Unitários

```bash
dotnet test ./tests/FCG.Notifications.Function.Tests
```

---

## ☁️ 6. Deploy na AWS com AWS SAM

Para compilar e provisionar a fila SQS, DLQ, regras IAM e a Lambda automaticamente:

```bash
# 1. Compilação do artefato .NET para AWS Lambda
sam build

# 2. Deploy guiado para a AWS (primeira vez)
sam deploy --guided
```

### Configurações sugeridas no assistente interativo:
- **Stack Name**: `fcg-notifications-stack`
- **AWS Region**: `us-east-1` (ou a região da sua conta)
- **Confirm changes before deploy**: `N`
- **Allow SAM CLI IAM role creation**: `Y`
- **Disable rollback**: `N`
- **Save arguments to configuration file**: `Y`

Ao final do deploy, o SAM imprimirá nos **Outputs** a URL da fila SQS criada:
```text
NotificationsQueueUrl: https://sqs.us-east-1.amazonaws.com/123456789012/fcg-notifications-queue
```

---

## 🧪 7. Testes e Validação em Nuvem

### 7.1. Acompanhamento de Logs em Tempo Real
```bash
sam logs -n NotificationsFunction --tail
```
*(Ou acesse pelo Console AWS: **CloudWatch** → **Log groups** → `/aws/lambda/fcg-notifications-function`)*.

### 7.2. Envio de Mensagem de Teste via AWS CLI
```bash
aws sqs send-message \
  --queue-url "<COLE_A_URL_DA_FILA_AQUI>" \
  --message-body '{"orderId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","userId":"7b134d4a-5c27-4a64-9279-22a3d0267c7e","gameIds":["c9b1f681-3be2-4a06-b3cb-77b3112beea4"],"correlationId":"test-manual-01","timestamp":"2026-09-10T00:00:00Z"}'
```

A função será acionada e o log estruturado JSON será registrado:
```json
{
  "event": "notification_sent",
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "7b134d4a-5c27-4a64-9279-22a3d0267c7e",
  "gameIds": [
    "c9b1f681-3be2-4a06-b3cb-77b3112beea4"
  ],
  "channel": "email-simulated",
  "correlationId": "test-manual-01",
  "timestamp": "2026-09-10T00:00:01.1234567Z",
  "status": "success"
}
```

---

## 📊 8. Observabilidade

- **Padrão Open Source (Opção A) & CloudWatch**: O projeto FCG adota a stack de observabilidade Prometheus/Grafana para seus serviços em container. Como funções AWS Lambda são efêmeras e não expõem um endpoint de métricas Prometheus persistente para *scraping*, a observabilidade desta função é centralizada no **AWS CloudWatch Logs** via a política nativa `AWSLambdaBasicExecutionRole` (sem agentes pesados ou de terceiros).
- **Integração com Grafana (Evolução Futura)**: O time pode opcionalmente adicionar o plugin **CloudWatch Data Source** no Grafana para exibir os logs e métricas da Lambda nos mesmos dashboards centrais.

---

## 🔍 9. Troubleshooting

- **Mensagens caindo na DLQ (`fcg-notifications-dlq`)**: Verifique se o payload enviado possui JSON válido e os campos obrigatórios `orderId` e `userId`.
- **Lambda não dispara**: Verifique no CloudWatch se o *Event Source Mapping* está ativo e se a role da Lambda possui a permissão de polling na fila SQS.

---

## ✅ 10. Checklist Final do Tech Challenge (Fase 3 - Serverless)

- [x] **NotificationsAPI refatorada como função Serverless (AWS Lambda)**.
- [x] **Função acionada automaticamente por eventos em fila SQS** (sem container rodando 24/7).
- [x] **Código da função e IaC (AWS SAM) em repositório próprio e independente**.
- [x] **Logs estruturados em JSON** com correlação (`orderId`, `userId`, `correlationId`).
- [x] **Dead Letter Queue (DLQ)** e tratamento de mensagens malformadas implementados.
- [x] **Suíte de testes unitários automatizados** com xUnit e FluentAssertions.
- [x] **Documentação completa de deploy e operação**.
# FCG-Notifications-Lambda
