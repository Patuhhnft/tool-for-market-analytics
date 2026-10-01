using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.MercadoLivre;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using LeitorMercadoLivre.Infrastructure.Pricing;
using LeitorMercadoLivre.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Tudo que a API e o Worker compartilham: banco, clientes HTTP, configuração, pipeline.</summary>
    /// <param name="contentRoot">
    /// Raiz de conteúdo do processo. A pasta de configuração relativa é resolvida a partir dela,
    /// e não do diretório de onde o comando foi disparado — API e Worker leem a mesma pasta.
    /// </param>
    public static IServiceCollection AddLeitorInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        var connection = configuration.GetConnectionString(LeitorDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                "Connection string 'Leitor' ausente. Defina ConnectionStrings__Leitor ou use appsettings.Development.json.");

        services.AddPooledDbContextFactory<LeitorDbContext>(options => options.UseNpgsql(connection));

        services.Configure<MercadoLivreApiOptions>(configuration.GetSection(MercadoLivreApiOptions.SectionName));
        services.Configure<ConfigurationFolderOptions>(configuration.GetSection(ConfigurationFolderOptions.SectionName));
        services.Configure<CollectionOptions>(configuration.GetSection(CollectionOptions.SectionName));
        services.PostConfigure<ConfigurationFolderOptions>(options =>
            options.Folder = Path.GetFullPath(Path.Combine(contentRoot, options.Folder)));

        // Aceita também os nomes curtos que já são usados no terminal (MELI_*).
        services.PostConfigure<MercadoLivreApiOptions>(options =>
        {
            options.BootstrapAccessToken ??= Environment.GetEnvironmentVariable("MELI_ACCESS_TOKEN");
            options.BootstrapRefreshToken ??= Environment.GetEnvironmentVariable("MELI_REFRESH_TOKEN");
            options.ClientId ??= Environment.GetEnvironmentVariable("MELI_CLIENT_ID");
            options.ClientSecret ??= Environment.GetEnvironmentVariable("MELI_CLIENT_SECRET");
        });

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddSingleton<ICredentialStore, DbCredentialStore>();
        services.AddSingleton<MercadoLivreTokenProvider>();
        services.AddTransient<MercadoLivreAuthHandler>();
        services.AddTransient<RetryHandler>();

        services.AddHttpClient(MercadoLivreTokenProvider.AuthClientName, (provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(30));

        services.AddHttpClient<MercadoLivreClient>((provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<MercadoLivreApiOptions>>().Value.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddHttpMessageHandler<MercadoLivreAuthHandler>()
            .AddHttpMessageHandler<RetryHandler>();

        services.AddHttpClient<PtaxClient>(client =>
        {
            client.BaseAddress = new Uri("https://olinda.bcb.gov.br/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddTransient<ExchangeRateService>();
        services.AddTransient<SaleScheduleProvider>();
        services.AddSingleton<BusinessConfigurationStore>();
        services.AddSingleton<OpportunitySearchService>();
        services.AddSingleton<HistorySearchService>();

        // Busca de catálogo para a API: LÊ o token, nunca renova. Cliente separado de propósito,
        // sem o MercadoLivreAuthHandler — é o handler que renovaria, e renovar fora do Worker
        // invalidaria o refresh token de uso único no meio de uma coleta.
        services.AddHttpClient<CatalogSearchClient>(client =>
        {
            client.BaseAddress = new Uri(configuration.GetSection(MercadoLivreApiOptions.SectionName)["BaseUrl"]
                ?? "https://api.mercadolibre.com/");
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddTransient<MarginAssessor>();
        services.AddTransient<CollectionPipeline>();
        services.AddTransient<CollectionJob>();
        services.AddTransient<OnDemandAnalysisJob>();

        return services;
    }
}
