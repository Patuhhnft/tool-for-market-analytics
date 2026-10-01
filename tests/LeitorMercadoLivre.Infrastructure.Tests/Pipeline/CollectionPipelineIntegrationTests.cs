using System.Globalization;
using System.Net;
using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.MercadoLivre;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using LeitorMercadoLivre.Infrastructure.Pricing;
using LeitorMercadoLivre.Infrastructure.Tests.MercadoLivre;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LeitorMercadoLivre.Infrastructure.Tests.Pipeline;

/// <summary>
/// O ciclo inteiro contra um Postgres de verdade (banco descartável, criado e apagado pelo
/// teste), com a API do Mercado Livre e o PTAX simulados no formato real. Se o Postgres do
/// docker-compose não estiver de pé, o teste é pulado — não reprovado.
/// </summary>
[TestFixture, Category("Integration")]
public sealed class CollectionPipelineIntegrationTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(Agora.UtcDateTime);

    private string connection = "";
    private string configFolder = "";
    private DbContextOptions<LeitorDbContext> dbOptions = null!;

    [SetUp]
    public async Task CreateDatabase()
    {
        var database = "leitor_teste_" + Guid.NewGuid().ToString("N")[..12];
        connection = $"Host=localhost;Port=5432;Database={database};Username=leitor;Password=leitor_dev;Timeout=30;Command Timeout=60";

        try
        {
            await using var probe = new NpgsqlConnection("Host=localhost;Port=5432;Database=leitor;Username=leitor;Password=leitor_dev;Timeout=10");
            await probe.OpenAsync();
        }
        catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            Assert.Ignore("Postgres local indisponível (docker compose up -d). " + exception.Message);
        }

        dbOptions = new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options;
        await using var db = new LeitorDbContext(dbOptions);
        await db.Database.MigrateAsync();

        configFolder = Path.Combine(Path.GetTempPath(), "leitor-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configFolder);
        File.WriteAllText(Path.Combine(configFolder, "configuracoes.json"), Configuracao);
    }

    [TearDown]
    public async Task DropDatabase()
    {
        if (dbOptions is not null)
        {
            await using var db = new LeitorDbContext(dbOptions);
            await db.Database.EnsureDeletedAsync();
        }

        if (Directory.Exists(configFolder)) Directory.Delete(configFolder, recursive: true);
    }

    // ----------------------------------------------------------------------------------

    private const string Configuracao = """
        {
          "tributos": [ { "regime": "RC-PF", "vigenteDesde": "2026-05-12", "icms": 0.20, "fonte": "Receita Federal",
            "faixasImpostoImportacao": [ { "ateUsd": 50, "aliquota": 0 }, { "ateUsd": 3000, "aliquota": 0.60, "deducaoUsd": 30 } ] } ],
          "parametros": [ { "vigenteDesde": "2026-09-01", "regimeImportacao": "RC-PF", "impostoSobreVenda": 0.06,
            "margemAlvo": 0.30, "spreadCambial": 0.06, "tipoAnuncio": "gold_special", "limiarFreteGratis": 79,
            "faixasTarifaFixa": [29, 50, 79], "freteAbsorvidoPorUnidade": 20, "freteNacionalPorRemessa": 50,
            "categorias": ["MLB1051"], "finalistasPorCiclo": 5, "candidatosPorCiclo": 10, "anunciosPorProdutoNaSerie": 3 } ]
        }
        """;

    /// <summary>Produto A: 10 vendedores em torno de R$ 90, um deles com dois anúncios. Produto B: 3 vendedores.</summary>
    private static string Anuncios(string produto)
    {
        (string Seller, decimal Price)[] linhas = produto == "MLBA"
            ? [("1", 85), ("2", 89.90m), ("3", 90), ("4", 90), ("5", 90.49m), ("6", 95), ("7", 99), ("8", 105), ("9", 78), ("10", 120), ("3", 97)]
            : [("21", 40), ("22", 41), ("23", 42)];

        var itens = linhas.Select((linha, i) => $$"""
            {"item_id":"{{produto}}-I{{i}}","seller_id":{{linha.Seller}},"price":{{linha.Price.ToString(CultureInfo.InvariantCulture)}},
             "category_id":"MLB1055","condition":"new","listing_type_id":"gold_special","official_store_id":null,
             "shipping":{"free_shipping":true,"logistic_type":"cross_docking","mode":"me2"},"min_purchase_unit":1}
            """);

        return $$"""{"paging":{"total":{{linhas.Length}},"offset":0,"limit":100},"results":[{{string.Join(',', itens)}}]}""";
    }

    /// <summary>15 dias completos + hoje: semana antiga com 70 visitas/dia, semana recente com 120; hoje parcial.</summary>
    private static string SerieDeVisitas(string item)
    {
        var dias = Enumerable.Range(0, 16).Select(back =>
        {
            var data = Hoje.AddDays(-back);
            var visitas = back == 0 ? 7 : back <= 7 ? 120 : back <= 14 ? 70 : 50;
            return $$"""{"date":"{{data:yyyy-MM-dd}}T00:00:00Z","total":{{visitas}}}""";
        });

        return $$"""{"item_id":"{{item}}","total_visits":0,"results":[{{string.Join(',', dias)}}]}""";
    }

    private static HttpResponseMessage RotaMercadoLivre(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;

        if (path == "/highlights/MLB/category/MLB1051")
        {
            return StubHttp.Json("""{"content":[{"id":"MLBA","position":1,"type":"PRODUCT"},{"id":"MLBB","position":2,"type":"PRODUCT"},{"id":"MLBU9","position":3,"type":"USER_PRODUCT"}]}""");
        }

        if (path.StartsWith("/products/") && path.EndsWith("/items")) return StubHttp.Json(Anuncios(path.Split('/')[2]));
        if (path.StartsWith("/products/")) return StubHttp.Json($$"""{"id":"{{path.Split('/')[2]}}","name":"Produto {{path.Split('/')[2]}}"}""");

        if (path == "/visits/items")
        {
            var ids = Uri.UnescapeDataString(query.Split("ids=")[1]).Split(',');
            return StubHttp.Json("{" + string.Join(',', ids.Select((id, i) => $"\"{id}\":{1000 + i}")) + "}");
        }

        if (path.EndsWith("/visits/time_window")) return StubHttp.Json(SerieDeVisitas(path.Split('/')[2]));

        if (path == "/sites/MLB/listing_prices")
        {
            var preco = decimal.Parse(query.Split("price=")[1].Split('&')[0], CultureInfo.InvariantCulture);
            var fixa = preco < 79 ? 6.75m : 0m;
            return StubHttp.Json($$$"""[{"listing_type_id":"gold_special","listing_type_name":"Clássico","sale_fee_amount":0,"sale_fee_details":{"fixed_fee":{{{fixa.ToString(CultureInfo.InvariantCulture)}}},"gross_amount":0,"percentage_fee":11}}]""");
        }

        return StubHttp.Json("""{"message":"rota não simulada"}""", HttpStatusCode.NotFound);
    }

    private (CollectionPipeline Pipeline, StubHttp MercadoLivre) Montar()
    {
        var contexts = new FixedFactory(dbOptions);
        var clock = new FixedClock(Agora);
        var apiOptions = Options.Create(new MercadoLivreApiOptions { VisitWindowDays = 15 });

        var mlHttp = new StubHttp(RotaMercadoLivre);
        var client = new MercadoLivreClient(new HttpClient(mlHttp) { BaseAddress = new Uri("https://api.test") }, apiOptions);

        var ptaxHttp = new StubHttp(_ => StubHttp.Json("""{"value":[{"cotacaoVenda":5.14140}]}"""));
        var exchange = new ExchangeRateService(contexts, new PtaxClient(new HttpClient(ptaxHttp) { BaseAddress = new Uri("https://olinda.test/") }), clock);

        var store = new BusinessConfigurationStore(Options.Create(new ConfigurationFolderOptions { Folder = configFolder }));
        var feeTables = new SaleScheduleProvider(client, new MemoryCache(new MemoryCacheOptions()));
        var assessor = new MarginAssessor(exchange, store, clock);

        var pipeline = new CollectionPipeline(
            contexts,
            client,
            feeTables,
            assessor,
            store,
            apiOptions,
            Options.Create(new CollectionOptions()),
            clock,
            NullLogger<CollectionPipeline>.Instance);

        return (pipeline, mlHttp);
    }

    private async Task CadastrarFornecedor(string produto)
    {
        await using var db = new LeitorDbContext(dbOptions);
        db.Suppliers.Add(new Supplier
        {
            ProductId = produto, Name = "Shenzhen Teste", UnitPrice = 5m, Currency = "USD",
            ShipmentQuantity = 100, InternationalFreightTotal = 50m, CreatedAt = Agora, UpdatedAt = Agora
        });
        await db.SaveChangesAsync();
    }

    // ----------------------------------------------------------------------------------

    [Test]
    public async Task CicloCompletoDoRankingAoIndice()
    {
        var (pipeline, ml) = Montar();
        await using (var db = new LeitorDbContext(dbOptions))
        {
            // O produto precisa existir antes do fornecedor ser cadastrado para ele.
            db.Products.Add(new CatalogProduct { Id = "MLBA", CategoryId = "MLB1051", FirstSeenAt = Agora, LastSeenAt = Agora });
            await db.SaveChangesAsync();
        }

        await CadastrarFornecedor("MLBA");

        var resumo = await pipeline.RunAsync(force: false, CancellationToken.None);

        await using var leitura = new LeitorDbContext(dbOptions);
        var analises = await leitura.Analyses.AsNoTracking().OrderBy(a => a.ProductId).ToListAsync();
        var a = analises.Single(x => x.ProductId == "MLBA");
        var b = analises.Single(x => x.ProductId == "MLBB");

        var preco = AnalysisJson.Read<PricePayload>(a.PriceJson)!;
        var demanda = AnalysisJson.Read<DemandPayload>(a.DemandJson)!;
        var margem = AnalysisJson.Read<MarginPayload>(a.MarginJson)!;
        var oportunidade = AnalysisJson.Read<OpportunityPayload>(a.OpportunityJson)!;

        Assert.Multiple(() =>
        {
            Assert.That(resumo.Status, Is.EqualTo(CycleStatus.Completed));
            Assert.That(resumo.Errors, Is.Empty, string.Join(" | ", resumo.Errors));
            Assert.That(analises, Has.Count.EqualTo(2), "USER_PRODUCT não é âncora de catálogo (I5)");

            // Preço: o vendedor 3 tem dois anúncios e entra só com o mais barato.
            Assert.That(preco.Result.SellersFound, Is.EqualTo(10));
            Assert.That(preco.Result.MarketPrice, Is.EqualTo(90m));
            Assert.That(preco.CategoryId, Is.EqualTo("MLB1055"), "comissão pela categoria folha");

            // Demanda: 3 anúncios × (7 × 120 − 7 × 70) = 1.050 visitas a mais; hoje (parcial) fora.
            Assert.That(demanda.Result.Source, Is.EqualTo(DemandSource.Visits));
            Assert.That(demanda.Result.AbsoluteDelta, Is.EqualTo(1050));
            Assert.That(demanda.ItemsInSeries, Has.Count.EqualTo(3));

            // Margem com fornecedor: PTAX gravada, versão da configuração gravada (I7).
            Assert.That(margem.Unavailable, Is.Null);
            Assert.That(margem.Exchange!.Ptax, Is.EqualTo(5.14140m));
            Assert.That(margem.Config.Regime, Is.EqualTo("RC-PF"));
            Assert.That(margem.Config.RegimeSource, Is.EqualTo("Receita Federal"));
            Assert.That(margem.Margin!.WorstInRange.Margin, Is.EqualTo(a.WorstMargin));
            Assert.That(margem.MaxSupplierUnitPrice, Is.GreaterThan(0m));

            Assert.That(oportunidade.Result.IncludesMargin, Is.True);
            Assert.That(a.OpportunityIndex, Is.InRange(0m, 1m));
            Assert.That(a.Explanation, Does.StartWith("recebeu 1.050 visitas a mais"));

            // Sem fornecedor: sem margem, e o índice sai marcado como sem margem.
            Assert.That(b.MarginJson, Is.Null);
            Assert.That(AnalysisJson.Read<OpportunityPayload>(b.OpportunityJson)!.Result.IncludesMargin, Is.False);

            Assert.That(
                ml.Requests.Where(r => r.RequestUri!.AbsolutePath == "/sites/MLB/listing_prices").Select(r => r.RequestUri!.Query),
                Has.All.Contains("category_id=MLB1055"));
        });
    }

    [Test]
    public async Task CoberturaDasVisitasRegistraSoOQueFoiConsultado()
    {
        var (pipeline, _) = Montar();

        await pipeline.RunAsync(force: false, CancellationToken.None);

        await using var db = new LeitorDbContext(dbOptions);
        var cobertura = await db.VisitCoverage.AsNoTracking().ToListAsync();
        var hojeGravado = await db.Visits.AnyAsync(v => v.Date == Hoje);

        Assert.Multiple(() =>
        {
            Assert.That(cobertura, Is.Not.Empty);
            Assert.That(cobertura, Has.All.Matches<VisitCoverage>(c => c.CoveredFrom == Hoje.AddDays(-15) && c.CoveredTo == Hoje.AddDays(-1)));
            Assert.That(hojeGravado, Is.False, "o dia corrente, parcial, não é gravado");
        });
    }

    [Test]
    public async Task MesmaJanelaNaoDuplicaEForcarRefazSemDuplicar()
    {
        var (pipeline, _) = Montar();

        await pipeline.RunAsync(force: false, CancellationToken.None);
        var segunda = await pipeline.RunAsync(force: false, CancellationToken.None);
        await pipeline.RunAsync(force: true, CancellationToken.None);

        await using var db = new LeitorDbContext(dbOptions);

        Assert.Multiple(async () =>
        {
            Assert.That(segunda.Skipped, Is.True, "janela já coletada");
            Assert.That(await db.Cycles.CountAsync(), Is.EqualTo(1));
            Assert.That(await db.Analyses.CountAsync(), Is.EqualTo(2), "forçar refaz, não soma");
            Assert.That(await db.Listings.CountAsync(), Is.EqualTo(14), "11 + 3 anúncios, uma vez só");
        });
    }

    [Test]
    public async Task ConfiguracaoInvalidaNaoRoda()
    {
        File.WriteAllText(Path.Combine(configFolder, "configuracoes.json"), """{ "tributos": [], "parametros": [] }""");
        var (pipeline, ml) = Montar();

        var erro = Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync(false, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(erro!.Message, Does.Contain("Nenhuma versão de parâmetros vigente"));
            Assert.That(ml.Requests, Is.Empty, "nenhuma chamada à API com configuração inválida");
        });
    }

    private sealed class FixedFactory(DbContextOptions<LeitorDbContext> options) : IDbContextFactory<LeitorDbContext>
    {
        public LeitorDbContext CreateDbContext() => new(options);
    }
}
