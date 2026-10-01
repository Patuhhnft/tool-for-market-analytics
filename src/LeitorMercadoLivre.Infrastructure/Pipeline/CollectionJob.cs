using System.ComponentModel;
using Hangfire;
using Hangfire.PostgreSql;

namespace LeitorMercadoLivre.Infrastructure.Pipeline;

/// <summary>
/// O ciclo como job do Hangfire. Fica na Infrastructure porque o Hangfire grava o TIPO do job
/// no banco: a API enfileira ("coletar agora") e o Worker executa, e os dois precisam
/// enxergar a mesma classe.
/// </summary>
public sealed class CollectionJob(CollectionPipeline pipeline)
{
    public const string RecurringId = "coleta-mercado-livre";

    // Sem repetição automática: credencial ausente ou configuração inválida não se resolvem
    // tentando de novo, só gastam chamadas. O próximo ciclo agendado tenta outra vez.
    // Sem concorrência: dois ciclos ao mesmo tempo disputariam o refresh token de uso único.
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    [DisplayName("Coleta do Mercado Livre (forçar janela: {0})")]
    public Task<CycleSummary> RunAsync(bool force, CancellationToken cancellationToken) =>
        pipeline.RunAsync(force, cancellationToken);
}

/// <summary>
/// Análise de um produto pedida pela barra de pesquisa. Job pelo mesmo motivo do ciclo: quem
/// fala com o Mercado Livre é o Worker, porque o refresh token é de uso único.
/// </summary>
public sealed class OnDemandAnalysisJob(CollectionPipeline pipeline)
{
    /// <summary>
    /// Idade a partir da qual vale remedir. Seis horas é a mesma janela do ciclo: dentro dela
    /// o número não mudaria o bastante para justificar ~23 chamadas da conta do ML.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    // Sem repetição: produto sem anúncio ou credencial ausente não se resolvem tentando de novo.
    // Sem concorrência por produto: duas análises do mesmo item disputariam o mesmo ciclo.
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 10 * 60)]
    [DisplayName("Analisar produto {0}")]
    public Task<OnDemandResult> RunAsync(string productId, CancellationToken cancellationToken) =>
        pipeline.AnalyzeOnDemandAsync(productId, MaxAge, cancellationToken);
}

public static class HangfireStorage
{
    /// <summary>Armazenamento do Hangfire no mesmo Postgres, no esquema "hangfire".</summary>
    public static IGlobalConfiguration UseLeitorStorage(this IGlobalConfiguration configuration, string connectionString) =>
        configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString));
}
