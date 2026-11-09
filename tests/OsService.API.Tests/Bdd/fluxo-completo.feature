# language: pt
Funcionalidade: Fluxo completo da Ordem de Serviço
  Como atendente da oficina
  Quero que a OS siga sozinha do diagnóstico até a finalização
  Para que orçamento, pagamento e execução aconteçam sem passos manuais

  Contexto:
    Dado que a Saga está ligada com Billing e Execução simulados
    E que estou autenticado como funcionário

  Cenário: OS aprovada e paga é executada e finalizada
    Dado uma OS aberta para o cliente 1 e o veículo 10 com o problema "Barulho no freio dianteiro"
    Quando eu inicio o diagnóstico
    Então a OS chega ao status "Finalizada"
    E o histórico mostra os status "Recebida, Diagnóstico, Aguardando Orçamento, Aguardando Aprovação, Aguardando Pagamento, Execução, Finalizada"
    E as mudanças depois do diagnóstico foram feitas pela Saga
    E a OS guarda o orçamento e o pagamento

  Cenário: OS finalizada é entregue ao cliente
    Dado uma OS aberta para o cliente 1 e o veículo 10 com o problema "Troca de óleo"
    E que eu iniciei o diagnóstico e a OS foi finalizada
    Quando eu registro a entrega
    Então a OS fica com o status "Entregue"
