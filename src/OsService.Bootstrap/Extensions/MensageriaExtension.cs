using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OsService.Application.Interfaces;
using OsService.Application.Options;
using OsService.Application.Saga;
using OsService.Domain.Exceptions;
using OsService.Infrastructure;
using OsService.Infrastructure.Mensageria;
using OsService.Infrastructure.Simuladores;

namespace OsService.Bootstrap.Extensions
{
    public static class MensageriaExtension
    {
        /// <summary>
        /// MassTransit 8 com a Saga da OS. Só é registrado com Saga:Habilitada=true; desligada,
        /// o serviço funciona como na Fase 3 e não depende do RabbitMQ.
        ///
        /// Mensageria:Transporte escolhe o broker: "RabbitMq" (padrão; estado da Saga, inbox e
        /// outbox no SQL Server, na mesma transação da OS) ou "InMemory" (testes de API e
        /// execução local sem broker; nada é durável).
        /// </summary>
        public static IServiceCollection AddMensageria(this IServiceCollection services, IConfiguration configuration)
        {
            var saga = configuration.GetSection(SagaOptions.Secao).Get<SagaOptions>() ?? new SagaOptions();
            if (!saga.Habilitada)
            {
                services.AddScoped<IPublicadorMensagens, PublicadorDesligado>();
                return services;
            }

            var emMemoria   = string.Equals(configuration["Mensageria:Transporte"], "InMemory", StringComparison.OrdinalIgnoreCase);
            var simuladores = SimuladoresAtivos.Ler(configuration);

            if (emMemoria)
                services.AddScoped<IPublicadorMensagens, PublicadorAposSalvar>();
            else
                services.AddScoped<IPublicadorMensagens, PublicadorMassTransit>();
            if (simuladores.Billing)
                services.AddSingleton<MemoriaSimuladores>();

            services.AddMassTransit(x =>
            {
                // A fila da Saga não se liga a exchange nenhum: recebe só o que os encaminhadores
                // e a própria API enviam para ela (ver FilasSaga).
                var registroSaga = x.AddSagaStateMachine<OrdemServicoStateMachine, OrdemServicoSagaState>()
                                    .Endpoint(e =>
                                    {
                                        e.Name = FilasSaga.Saga;
                                        e.ConfigureConsumeTopology = false;
                                    });

                foreach (var evento in FilasSaga.EventosExternos)
                    x.AddConsumer(typeof(EncaminhadorParaSaga<>).MakeGenericType(evento))
                     .Endpoint(e => e.Name = FilasSaga.FilaDoEvento(evento));

                if (emMemoria)
                {
                    registroSaga.InMemoryRepository();
                }
                else
                {
                    registroSaga.EntityFrameworkRepository(r =>
                    {
                        // Lock da linha da Saga durante o processamento: duas mensagens da mesma OS
                        // nunca são tratadas ao mesmo tempo.
                        r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                        r.ExistingDbContext<OsServiceContext>();
                        r.UseSqlServer();
                    });

                    // Bus outbox: o que a API publica é gravado na transação da OS e enviado depois.
                    x.AddEntityFrameworkOutbox<OsServiceContext>(o =>
                    {
                        o.UseSqlServer();
                        o.UseBusOutbox();
                    });
                }

                // Cada simulador sai sozinho quando o servico real entra no ar: com a Execucao
                // real consumindo os comandos, Simuladores:Execucao=false e o Billing segue simulado.
                if (simuladores.Execucao)
                    x.AddConsumer<SimuladorExecucao>().Endpoint(e => e.Name = "simulador-execucao");
                if (simuladores.Billing)
                    x.AddConsumer<SimuladorBilling>().Endpoint(e => e.Name = "simulador-billing");

                var filasDeEventos = FilasSaga.EventosExternos.Select(FilasSaga.FilaDoEvento).ToHashSet();

                x.AddConfigureEndpointsCallback((contexto, nome, endpoint) =>
                {
                    // Filas de evento recebem um tipo só, então aceitam JSON sem cabeçalho de tipo
                    // (publicado por quem não usa MassTransit). Nas outras filas o tipo vem no cabeçalho.
                    if (filasDeEventos.Contains(nome))
                        endpoint.UseRawJsonDeserializer(RawSerializerOptions.AnyMessageType | RawSerializerOptions.CopyHeaders, isDefault: true);

                    // Erro técnico: tenta de novo com intervalo crescente; esgotado, vai para a fila _error.
                    // Erro de negócio (transição inválida, OS inexistente) não melhora com retry.
                    endpoint.UseMessageRetry(r =>
                    {
                        r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3));
                        r.Ignore<RegraDeNegocioException>();
                        r.Ignore<RecursoNaoEncontradoException>();
                    });

                    // Inbox (descarta mensagem repetida pelo MessageId) e outbox (o que a Saga
                    // publica só sai depois do commit), na mesma transação do estado da Saga.
                    // As filas de evento ficam de fora: só repassam a mensagem e podem receber
                    // JSON sem MessageId de quem não usa MassTransit. Um repasse duplicado é
                    // ignorado pela Saga, que só aceita cada evento no estado certo.
                    if (!emMemoria && !filasDeEventos.Contains(nome))
                        endpoint.UseEntityFrameworkOutbox<OsServiceContext>(contexto);
                });

                if (emMemoria)
                {
                    x.UsingInMemory((contexto, cfg) =>
                    {
                        ConfigurarSerializacao(cfg);
                        cfg.ConfigureEndpoints(contexto);
                    });
                }
                else
                {
                    x.UsingRabbitMq((contexto, cfg) =>
                    {
                        var rabbit = configuration.GetSection("RabbitMq");
                        cfg.Host(rabbit["Host"] ?? "localhost", rabbit["VirtualHost"] ?? "/", h =>
                        {
                            h.Username(rabbit["Usuario"] ?? "guest");
                            h.Password(rabbit["Senha"] ?? "guest");
                        });

                        ConfigurarSerializacao(cfg);
                        cfg.ConfigureEndpoints(contexto);
                    });
                }
            });

            if (!emMemoria)
                services.AddHostedService<VerificadorPrazosSaga>();

            return services;
        }

        /// <summary>
        /// JSON puro, sem o envelope do MassTransit, como define o contrato: os outros serviços
        /// não precisam usar MassTransit. O exchange de cada mensagem continua sendo
        /// "MechanicLtda.Contratos:NomeDaMensagem".
        /// </summary>
        private static void ConfigurarSerializacao(IBusFactoryConfigurator cfg)
        {
            cfg.UseRawJsonSerializer(RawSerializerOptions.AddTransportHeaders | RawSerializerOptions.CopyHeaders, isDefault: true);
            cfg.UseRawJsonDeserializer(RawSerializerOptions.Default | RawSerializerOptions.CopyHeaders, isDefault: true);
        }
    }
}
