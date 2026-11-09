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
