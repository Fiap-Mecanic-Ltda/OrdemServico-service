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

## Configuração

| Chave | Padrão | Uso |
|---|---|---|
| `JWT_SECRET_KEY` | (obrigatória) | Mesma chave do MechanicLtda e da Lambda |
| `ConnectionStrings__DefaultConnection` | SQL Server local | Banco próprio do serviço (schema `os`) |
| `Integracoes__MechanicLtdaApi__BaseUrl` | `http://localhost:8080` | API do MechanicLtda, usada para validar cliente e veículo |
| `Saga__Habilitada` | `false` | Com `true`, as transições feitas pela Saga ficam bloqueadas na API (409) |

As migrations são aplicadas na subida da API.

## Endpoints

Todas as rotas exigem o JWT do sistema. Admin = `Administrador` ou `Funcionario`.

| Método e rota | Quem | O que faz |
|---|---|---|
| `POST /api/ordemservico` | Admin | Abre a OS (confere no MechanicLtda que o veículo é do cliente) |
| `GET /api/ordemservico` | Admin | OS em andamento, das mais urgentes para as menos |
| `GET /api/ordemservico/{id}` | Admin, Cliente (só a própria) | Status, itens e referências |
| `GET /api/ordemservico/{id}/historico` | Admin, Cliente (só a própria) | Histórico de status |
| `GET /api/ordemservico/cliente/{clienteId}` | Admin, Cliente (só o próprio) | OS do cliente |
| `GET /api/ordemservico/tempo-medio-execucao` | Admin | Tempo médio das OS executadas |
| `PUT /api/ordemservico/{id}` | Admin | Descrição e valor estimado (com valor, vai para diagnóstico) |
| `PATCH /api/ordemservico/{id}/iniciar-diagnostico` | Admin | Recebida → Em Diagnóstico |
| `PATCH /api/ordemservico/{id}/aguardar-aprovacao` | Admin | Em Diagnóstico → Aguardando Aprovação (*) |
| `PATCH /api/ordemservico/{id}/aprovar` (ou `iniciar-execucao`) | Admin | Aguardando Aprovação → Em Execução (*) |
| `PATCH /api/ordemservico/{id}/recusar` | Admin | Aguardando Aprovação → Cancelada (*) |
| `PATCH /api/ordemservico/{id}/finalizar` | Admin | Em Execução → Finalizada (*) |
| `PATCH /api/ordemservico/{id}/entregar` | Admin | Finalizada → Entregue |
| `PATCH /api/ordemservico/{id}/cancelar` | Admin | Cancela (com a Saga ligada, só antes do orçamento) |
| `DELETE /api/ordemservico/{id}` | Admin | Remove OS Recebida ou em Diagnóstico |
| `GET/POST/PUT/DELETE /api/ordem-servico/{id}/itens` | Admin | Itens da OS (só em Recebida ou Diagnóstico) |

(*) Bloqueadas com `Saga__Habilitada=true`: nesse modo quem faz a transição é a Saga.

Erros seguem o envelope `{ "success": false, "errors": [...] }`: 400 regra de negócio ou
validação, 403 cliente acessando dados de outro, 404 não encontrado, 409 operação da Saga e
503 quando o MechanicLtda não responde.
