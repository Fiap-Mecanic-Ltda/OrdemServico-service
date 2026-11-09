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
dotnet test OsService.sln --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory coverage
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"coverage/**/coverage.cobertura.xml" -targetdir:coverage/relatorio -reporttypes:"Cobertura;Html"
python3 scripts/verificar-cobertura.py coverage/relatorio/Cobertura.xml 80
```

| Projeto | O que cobre |
|---|---|
| `tests/OsService.UnitTests` | Domínio, aplicação (Moq), infraestrutura (gateway, repositório EF InMemory, verificador de prazos) e a state machine da Saga com o test harness do MassTransit, passo a passo e compensação por compensação |
| `tests/OsService.API.Tests` | API em memória (WebApplicationFactory com JWT, banco e gateway falsos) e a Saga ponta a ponta com transporte em memória e os simuladores |
| `tests/OsService.API.Tests/Bdd` | Cenários BDD em Reqnroll: `fluxo-completo.feature` (caminho feliz) e `pagamento-recusado.feature` (compensações) |

A esteira falha se a cobertura de linhas ficar abaixo de 80% e publica o relatório no resumo
do job. Com o secret `SONAR_TOKEN` configurado no repositório, ela também analisa o código no
SonarCloud e falha se o quality gate reprovar. As variáveis `SONAR_ORGANIZATION` e
`SONAR_PROJECT_KEY` são opcionais e têm padrão `fiap-mecanic-ltda` e
`Fiap-Mecanic-Ltda_OrdemServico-service`.

## Esteira (CI/CD)

`.github/workflows/ci.yml` roda build, testes, gate de cobertura e build da imagem em todo PR.
No push na `main`, ela também publica a imagem `mechanicltda-os` no ECR (tags SHA e `latest`) e
dispara o deploy no `OrdemServico-InfraKubernete` (`repository_dispatch` do tipo
`imagem-publicada`). Secrets do repositório:

| Secret | Uso |
|---|---|
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | Só para conferir se a role de OIDC já existe (mesma chave do terraform-deployer usada no MechanicLtda) |
| `AWS_ROLE_ARN` | Role `mechanicltda-prod-os-github-actions`, criada pelo Terraform do OrdemServico-InfraKubernete |
| `OS_INFRA_DISPATCH_TOKEN` | Token fine-grained com Contents read/write só no OrdemServico-InfraKubernete. Sem ele, a imagem é publicada e o deploy fica manual |
| `SONAR_TOKEN` | Opcional por enquanto (ver Testes) |

Antes do primeiro `terraform apply` da infra, o push na `main` pula a publicação com um aviso.

## Configuração

| Chave | Padrão | Uso |
|---|---|---|
| `JWT_SECRET_KEY` | (obrigatória) | Mesma chave do MechanicLtda e da Lambda |
| `ConnectionStrings__DefaultConnection` | SQL Server local | Banco próprio do serviço (schema `os`) |
| `Integracoes__MechanicLtdaApi__BaseUrl` | `http://localhost:8080` | API do MechanicLtda, usada para validar cliente e veículo |
| `Saga__Habilitada` | `false` | Liga a Saga (RabbitMQ). Com `true`, as transições feitas pela Saga ficam bloqueadas na API (409) |
| `Saga__PrazoDiagnosticoHoras` / `Saga__PrazoAprovacaoHoras` | `48` / `72` | Prazos da Saga (ver contratos) |
| `RabbitMq__Host`, `RabbitMq__Usuario`, `RabbitMq__Senha` | `localhost`, `guest` | Broker da Saga |
| `Mensageria__Transporte` | `RabbitMq` | `InMemory` só para testes: sem broker e sem nada durável |
| `Simuladores__Habilitados` | `false` | Billing e Execução simulados no próprio processo (só local) |

As migrations são aplicadas na subida da API.

## Saga

Com `Saga__Habilitada=true` a OS anda sozinha a partir do diagnóstico: o OS Service publica
os comandos para Billing e Execução e reage aos eventos deles
([contratos](docs/contratos-eventos.md), [ADR-002](docs/adr/ADR-002-saga-orquestrada.md)).
O `docker-compose` já sobe assim, com os dois serviços simulados. Para ver o fluxo:

1. `POST /api/ordemservico` e depois `PATCH /api/ordemservico/{id}/iniciar-diagnostico`.
2. `GET /api/ordemservico/{id}/historico` mostra Recebida → Diagnóstico → Aguardando Orçamento →
   Aguardando Aprovação → Aguardando Pagamento → Execução → Finalizada, com origem `Saga`.
3. Para ver uma compensação, coloque um marcador na descrição do problema:
   `[falha-pagamento]`, `[falha-execucao]`, `[recusar]` ou `[falha-diagnostico]`.

As filas aparecem no painel do RabbitMQ: `os-<evento>` para cada evento recebido,
`os-ordem-servico-saga` para a Saga e `simulador-*` para os simuladores.

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
