# language: pt
Funcionalidade: Compensação da Saga
  Como responsável pela oficina
  Quero que uma falha em qualquer passo desfaça o que os passos anteriores fizeram
  Para que nenhuma OS fique com orçamento ou pagamento pendurado

  Contexto:
    Dado que a Saga está ligada com Billing e Execução simulados
    E que estou autenticado como funcionário

  Cenário: Falha na execução estorna o pagamento e cancela a OS
    Dado uma OS aberta para o cliente 1 e o veículo 10 com o problema "Motor falhando [falha-execucao]"
    Quando eu inicio o diagnóstico
    Então a OS chega ao status "Cancelada"
    E o histórico mostra os status "Recebida, Diagnóstico, Aguardando Orçamento, Aguardando Aprovação, Execução, Compensando, Cancelada"
    E o motivo da compensação começa com "Execução falhou"

  Cenário: Orçamento recusado pelo cliente cancela a OS sem cobrança
    Dado uma OS aberta para o cliente 1 e o veículo 10 com o problema "Pintura completa [recusar]"
    Quando eu inicio o diagnóstico
    Então a OS chega ao status "Cancelada"
    E o histórico mostra os status "Recebida, Diagnóstico, Aguardando Orçamento, Aguardando Aprovação, Cancelada"
    E a OS não tem pagamento registrado
