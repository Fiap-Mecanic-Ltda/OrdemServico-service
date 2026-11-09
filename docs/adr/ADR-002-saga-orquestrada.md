# ADR-002 — Saga orquestrada pelo OS Service

- **Status:** proposto
- **Data:** 2026-11-09

## Contexto

O fluxo de uma OS atravessa três serviços: abrir a OS, diagnosticar, gerar o orçamento, aguardar a
aprovação do cliente, receber o pagamento e executar o reparo. Uma falha em qualquer passo precisa
desfazer o que os passos anteriores fizeram (Saga Pattern com compensação).

## Decisão

A Saga é **orquestrada** e o orquestrador é o **OS Service**:

- O OS Service já é o dono do ciclo de vida da OS. O estado da Saga e o status da OS ficam no mesmo
  banco e mudam na mesma transação.
- Cada passo é um comando do orquestrador e uma resposta do serviço que executa
  ([contratos-eventos.md](../contratos-eventos.md)).
- As compensações ficam declaradas num único lugar, a state machine `OrdemServicoSaga`:

| Falha | Compensação | Status final |
|---|---|---|
| `DiagnosticoFalhou` | nenhuma | `Recebida` |
| `OrcamentoFalhou` | nenhuma | `EmDiagnostico` |
| `OrcamentoRecusado` | `LiberarReservaEstoque` | `Cancelada` |
| `PagamentoFalhou` | `CancelarOrcamento` + `LiberarReservaEstoque` | `Cancelada` |
| `ExecucaoFalhou` | `EstornarPagamento` + `CancelarOrcamento` | `Cancelada` |
| Prazo de aprovação expirado | `CancelarOrcamento` + `LiberarReservaEstoque` | `Cancelada` |

## Alternativas

| Alternativa | Por que não |
|---|---|
| Coreografia (cada serviço reage aos eventos dos outros) | A regra do fluxo ficaria espalhada em três repositórios mantidos por pessoas diferentes; descobrir em que passo uma OS parou exigiria juntar logs de todos |
| Orquestrador como um quarto serviço | Mais um repositório, deploy e banco sem ganho: a OS já é o centro do fluxo |

## Consequências

- O OS Service conhece todos os passos; os outros serviços só conhecem os próprios comandos.
- Consultar `GET /api/ordemservico/{id}/historico` mostra o caminho completo da Saga, inclusive as
  compensações.
- O OS Service vira um ponto central: se ficar fora do ar, as mensagens esperam na fila e a Saga
  continua de onde parou quando ele voltar.
