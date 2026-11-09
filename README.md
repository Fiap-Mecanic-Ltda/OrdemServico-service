# OrdemServico-service

Microsserviço de **Ordem de Serviço (OS Service)** da MechanicLtda: abertura da OS, atualização
de status, consulta de status e histórico. É também o **orquestrador da Saga** que coordena
Orçamento e Pagamento e Execução e Produção.

| Documento | Conteúdo |
|---|---|
| [Contratos de mensagens](docs/contratos-eventos.md) | Comandos, eventos e status da Saga |
| [ADR-001](docs/adr/ADR-001-mensageria-rabbitmq.md) | Mensageria com RabbitMQ e MassTransit |
| [ADR-002](docs/adr/ADR-002-saga-orquestrada.md) | Saga orquestrada pelo OS Service |

Repositórios relacionados: `OrdemServico-InfraKubernete` (deploy no k3s) e
`OrdemServico-InfraSGBD` (banco de dados).

## Estrutura

Mesmas camadas do MechanicLtda:

```text
OrdemServico-service/
├── src/
│   ├── OsService.API             # Host REST: controllers, Swagger, JWT, CorrelationId, /health
│   ├── OsService.Bootstrap       # Composition root: banco, logs, roles
│   ├── OsService.Application     # Casos de uso e segurança
│   ├── OsService.Domain          # Entidades, enums e regras da OS
│   └── OsService.Infrastructure  # EF Core (SQL Server, schema "os") e integrações
├── tests/
│   ├── OsService.UnitTests       # Domínio e aplicação (xUnit + Moq)
│   └── OsService.API.Tests       # API em memória (WebApplicationFactory)
├── docs/                         # Contratos da Saga e ADRs
├── docker-compose.yml            # SQL Server + RabbitMQ + API
└── .github/workflows/ci.yml      # Build, testes e build da imagem
```

## Rodando localmente

Pré-requisitos: .NET 9 SDK e Docker.

```bash
cp .env.example .env            # ajuste as senhas e a chave JWT
docker compose up -d --build
curl http://localhost:8081/health
```

| Serviço | Endereço |
|---|---|
| API e Swagger | http://localhost:8081/swagger |
| Painel do RabbitMQ | http://localhost:15672 (usuário e senha do `.env`) |
| SQL Server | `localhost,1434` (porta diferente da do MechanicLtda) |

Para a API funcionar com os tokens do resto do sistema, `JWT_SECRET_KEY` precisa ser a mesma
chave usada pelo MechanicLtda e pela Lambda de autenticação.

Rodando sem Docker:

```bash
dotnet user-secrets set JWT_SECRET_KEY "<chave>" --project src/OsService.API
dotnet run --project src/OsService.API          # http://localhost:5070/swagger
```

## Testes

```bash
dotnet test OsService.sln --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```
