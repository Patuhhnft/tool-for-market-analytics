using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LeitorMercadoLivre.Infrastructure.Tests.Search;

/// <summary>
/// A busca de "Explorar oportunidades" contra um Postgres de verdade (banco descartável). Tem
/// de ser banco real: o que está sendo testado é justamente a tradução para SQL — nulo fora
/// dos filtros, nulo no fim da ordenação, página estável. Nada disso aparece em memória.
/// <para>Sem o Postgres do docker-compose de pé, o teste é pulado — não reprovado.</para>
/// </summary>
[TestFixture, Category("Integration")]
public sealed class OpportunitySearchServiceTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private string connection = "";
    private DbContextOptions<LeitorDbContext> dbOptions = null!;
    private OpportunitySearchService search = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        try
        {
            await using var probe = new NpgsqlConnection("Host=localhost;Port=5432;Database=leitor;Username=leitor;Password=leitor_dev;Timeout=10");
            await probe.OpenAsync();
        }
        catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            Assert.Ignore("Postgres local indisponível (docker compose up -d). " + exception.Message);
        }

        connection = $"Host=localhost;Port=5432;Database=leitor_busca_{Guid.NewGuid():N};Username=leitor;Password=leitor_dev;Timeout=30";
        dbOptions = new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options;

        await using var db = new LeitorDbContext(dbOptions);
        await db.Database.MigrateAsync();
        await SeedAsync(db);

        // Pasta vazia: a busca precisa responder mesmo sem configuração vigente, com os
        // limiares padrão do domínio.
        var folder = Path.Combine(Path.GetTempPath(), "leitor-busca-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        search = new OpportunitySearchService(
            new BusinessConfigurationStore(Options.Create(new ConfigurationFolderOptions { Folder = folder })),
            TimeProvider.System);
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (dbOptions is null) return;
        await using var db = new LeitorDbContext(dbOptions);
        await db.Database.EnsureDeletedAsync();
    }

    private LeitorDbContext Db() => new(dbOptions);

    // ----------------------------------------------------------------------------------
    // O catálogo do teste. Cada produto existe para responder a uma pergunta diferente, e
    // o P6 tem DUAS análises: é ele quem prova que a busca olha só para a mais recente.
    // ----------------------------------------------------------------------------------

    private static async Task SeedAsync(LeitorDbContext db)
    {
        db.Cycles.Add(new CollectionCycle { Id = 1, WindowStart = Agora.AddDays(-1), StartedAt = Agora.AddDays(-1), FinishedAt = Agora.AddDays(-1), Status = CycleStatus.Completed });
        db.Cycles.Add(new CollectionCycle { Id = 2, WindowStart = Agora, StartedAt = Agora, FinishedAt = Agora, Status = CycleStatus.Completed });

        Product(db, "P1", "MLB1051", "Celulares e Telefones");
        Product(db, "P2", "MLB1051", "Celulares e Telefones");
        Product(db, "P3", "MLB1276", "Esportes e Fitness");
        Product(db, "P4", "MLB1276", "Esportes e Fitness");
        Product(db, "P5", "MLB1648", "Informática");
        Product(db, "P6", "MLB1648", "Informática");

        // preço 50, índice 0,9, 3 vendedores, 0 entrantes, cresceu 120%, novo, frete grátis, confiança alta
        Analysis(db, 2, "P1", price: 50m, index: 0.90m, sellers: 3, newSellers: 0, growth: 120m, confidence: "High");

        // preço 500, índice 0,4, 20 vendedores, 5 entrantes, caiu 10%, usado, sem frete, confiança média
        Analysis(db, 2, "P2", price: 500m, index: 0.40m, sellers: 20, newSellers: 5, growth: -10m,
            confidence: "Medium", freeShipping: false, hasNew: false, hasUsed: true);

        // demanda nova com volume: sem percentual, mas é o caso de "explodindo"
        Analysis(db, 2, "P3", price: 120m, index: 0.70m, sellers: 8, newSellers: 1, growth: null,
            isNewDemand: true, lastWeekVisits: 900, confidence: "Low");

        // sem ciclo anterior: entrantes desconhecidos, e sem preço medido
        Analysis(db, 2, "P4", price: null, index: null, sellers: 40, newSellers: null, growth: null, confidence: null);

        // caso de borda da faixa: exatamente 5 vendedores e exatamente 2 entrantes
        Analysis(db, 2, "P5", price: 79m, index: 0.55m, sellers: 5, newSellers: 2, growth: 50m, confidence: "Medium");

        // P6 tem duas análises: a antiga diria 1 vendedor, a atual diz 31
        Analysis(db, 1, "P6", price: 10m, index: 0.99m, sellers: 1, newSellers: 0, growth: 900m, confidence: "High");
        Analysis(db, 2, "P6", price: 900m, index: 0.20m, sellers: 31, newSellers: 9, growth: 5m, confidence: "Low");

        db.Suppliers.Add(new Supplier
        {
            ProductId = "P1", Name = "Fornecedor", UnitPrice = 1m, Currency = "USD",
            CreatedAt = Agora, UpdatedAt = Agora, Active = true
        });

        await db.SaveChangesAsync();
    }

    private static void Product(LeitorDbContext db, string id, string categoryId, string categoryName) =>
        db.Products.Add(new CatalogProduct
        {
            Id = id, CategoryId = categoryId, CategoryName = categoryName,
            FirstSeenAt = Agora.AddDays(-30), LastSeenAt = Agora
        });

    private static void Analysis(
        LeitorDbContext db, long cycleId, string productId, decimal? price, decimal? index, int sellers,
        int? newSellers, decimal? growth, string? confidence, bool isNewDemand = false, int? lastWeekVisits = null,
        bool freeShipping = true, bool hasNew = true, bool hasUsed = false) =>
        db.Analyses.Add(new ProductAnalysis
        {
            CycleId = cycleId,
            ProductId = productId,
            CalculatedAt = Agora,
            PriceStatus = price is null ? "InsufficientData" : "Calculated",
            MarketPrice = price,
            PriceConfidence = confidence,
            SellersFound = sellers,
            NewSellers = newSellers,
            DemandScore = 0.5m,
            DemandSource = "Visits",
            LowDemand = false,
            OpportunityIndex = index,
            Explanation = "teste",
            VisitGrowthPercent = growth,
            IsNewDemand = isNewDemand,
            LastWeekVisits = lastWeekVisits,
            HasFreeShipping = freeShipping,
            HasNewCondition = hasNew,
            HasUsedCondition = hasUsed
        });

    private async Task<string[]> FindAsync(OpportunityFilterQuery query)
    {
        var (filter, errors) = OpportunityFilter.Parse(query);
        Assert.That(errors, Is.Empty, string.Join(" | ", errors));

        await using var db = Db();
        var page = await search.SearchAsync(db, filter!, CancellationToken.None);
        return [.. page.Items.Select(analysis => analysis.ProductId)];
    }

    // ----------------------------------------------------------------------------------

    [Test]
    public async Task SemFiltroVemUmProdutoPorAnalise()
    {
        var found = await FindAsync(new OpportunityFilterQuery());

        Assert.That(found, Is.EquivalentTo(new[] { "P1", "P2", "P3", "P4", "P5", "P6" }),
            "seis produtos, sete análises: o P6 não pode aparecer duas vezes");
    }

    [Test]
    public async Task SoAUltimaAnaliseDeCadaProdutoConta()
    {
        // A análise antiga do P6 tinha 1 vendedor; a atual tem 31. Se a busca olhasse o
        // histórico, o P6 entraria em "até 5 vendedores".
        var poucos = await FindAsync(new OpportunityFilterQuery { Concorrencia = [CompetitionBand.UpTo5] });
        var muitos = await FindAsync(new OpportunityFilterQuery { Concorrencia = [CompetitionBand.From31] });

        Assert.Multiple(() =>
        {
            Assert.That(poucos, Does.Not.Contain("P6"));
            Assert.That(muitos, Does.Contain("P6"), "vale a análise atual, de 31 vendedores");
        });
    }

    [Test]
    public async Task FiltroPorProdutoIsolaUmSo()
    {
        // É o que a barra de pesquisa aplica ao escolher um resultado: a lista de sempre,
        // filtrada por ele — em vez de uma tela nova com regra própria.
        var found = await FindAsync(new OpportunityFilterQuery { Produto = "p3" });

        Assert.That(found, Is.EqualTo(new[] { "P3" }), "o id chega em minúsculo e é normalizado");
    }

    [Test]
    public async Task FiltroPorProdutoSeSomaAosDemais()
    {
        // Produto E concorrência: se o produto não passa nos outros filtros, some.
        var passa = await FindAsync(new OpportunityFilterQuery { Produto = "P3", Concorrencia = [CompetitionBand.From6To15] });
        var naoPassa = await FindAsync(new OpportunityFilterQuery { Produto = "P3", Concorrencia = [CompetitionBand.UpTo5] });

        Assert.Multiple(() =>
        {
            Assert.That(passa, Is.EqualTo(new[] { "P3" }));
            Assert.That(naoPassa, Is.Empty);
        });
    }

    [Test]
    public async Task CategoriaFiltraPeloIdDoProduto()
    {
        var found = await FindAsync(new OpportunityFilterQuery { Categoria = "mlb1276" });

        Assert.That(found, Is.EquivalentTo(new[] { "P3", "P4" }));
    }

    [Test]
    public async Task PrecoIgnoraQuemNaoTemPrecoMedido()
    {
        // O P4 não tem preço. Ele não é barato nem caro: fica fora dos dois lados.
        var barato = await FindAsync(new OpportunityFilterQuery { PrecoMax = 100m });
        var caro = await FindAsync(new OpportunityFilterQuery { PrecoMin = 100m });

        Assert.Multiple(() =>
        {
            Assert.That(barato, Is.EquivalentTo(new[] { "P1", "P5" }));
            Assert.That(caro, Is.EquivalentTo(new[] { "P2", "P3", "P6" }));
            Assert.That(barato.Concat(caro), Does.Not.Contain("P4"));
        });
    }

    [Test]
    public async Task FaixasDeConcorrenciaSomamSemSobrepor()
    {
        var primeira = await FindAsync(new OpportunityFilterQuery { Concorrencia = [CompetitionBand.UpTo5] });
        var segunda = await FindAsync(new OpportunityFilterQuery { Concorrencia = [CompetitionBand.From6To15] });
        var juntas = await FindAsync(new OpportunityFilterQuery { Concorrencia = [CompetitionBand.UpTo5, CompetitionBand.From6To15] });

        Assert.Multiple(() =>
        {
            Assert.That(primeira, Is.EquivalentTo(new[] { "P1", "P5" }), "5 vendedores entram em 'até 5'");
            Assert.That(segunda, Is.EquivalentTo(new[] { "P3" }));
            Assert.That(juntas, Is.EquivalentTo(primeira.Concat(segunda)), "opções do mesmo filtro são OU");
        });
    }

    [Test]
    public async Task EntranteDesconhecidoFicaForaDeTodasAsFaixas()
    {
        // O P4 não tem ciclo anterior: new_sellers é nulo. "Não medimos" não é "deu zero".
        var nenhum = await FindAsync(new OpportunityFilterQuery { NovosVendedores = [NewSellerBand.None] });
        var todos = await FindAsync(new OpportunityFilterQuery
        {
            NovosVendedores = [NewSellerBand.None, NewSellerBand.UpTo2, NewSellerBand.From3]
        });

        Assert.Multiple(() =>
        {
            Assert.That(nenhum, Is.EquivalentTo(new[] { "P1" }));
            Assert.That(todos, Does.Not.Contain("P4"));
            Assert.That(todos, Is.EquivalentTo(new[] { "P1", "P2", "P3", "P5", "P6" }));
        });
    }

    [Test]
    public async Task CrescimentoEPisoNaoFaixa()
    {
        var dez = await FindAsync(new OpportunityFilterQuery { Crescimento = GrowthOption.TenPercent });
        var cem = await FindAsync(new OpportunityFilterQuery { Crescimento = GrowthOption.HundredPercent });

        Assert.Multiple(() =>
        {
            Assert.That(dez, Is.EquivalentTo(new[] { "P1", "P5" }), "+50 e +120 passam do piso de 10");
            Assert.That(cem, Is.EquivalentTo(new[] { "P1" }));
        });
    }

    [Test]
    public async Task ExplodindoIncluiDemandaNovaComVolume()
    {
        // O P3 estreou: não tem percentual para comparar, mas 900 visitas na semana é
        // exatamente o que o filtro procura. O P1, com +120%, está abaixo do limiar de 300%.
        var found = await FindAsync(new OpportunityFilterQuery { Crescimento = GrowthOption.Exploding });

        Assert.That(found, Is.EquivalentTo(new[] { "P3" }));
    }

    [Test]
    public async Task CondicaoEFreteSaoTemAoMenosUm()
    {
        var usados = await FindAsync(new OpportunityFilterQuery { Condicao = [ItemCondition.Used] });
        var frete = await FindAsync(new OpportunityFilterQuery { FreteGratis = true });

        Assert.Multiple(() =>
        {
            Assert.That(usados, Is.EquivalentTo(new[] { "P2" }));
            Assert.That(frete, Does.Not.Contain("P2"));
            Assert.That(frete, Has.Length.EqualTo(5));
        });
    }

    [Test]
    public async Task ConfiancaDoPrecoFiltraSemPegarQuemNaoTem()
    {
        var found = await FindAsync(new OpportunityFilterQuery { Confianca = [SampleConfidence.High, SampleConfidence.Medium] });

        Assert.That(found, Is.EquivalentTo(new[] { "P1", "P2", "P5" }), "o P4 não tem confiança medida");
    }

    [Test]
    public async Task FornecedorCadastradoOuNao()
    {
        var com = await FindAsync(new OpportunityFilterQuery { TemFornecedor = true });
        var sem = await FindAsync(new OpportunityFilterQuery { TemFornecedor = false });

        Assert.Multiple(() =>
        {
            Assert.That(com, Is.EquivalentTo(new[] { "P1" }));
            Assert.That(sem, Does.Not.Contain("P1"));
            Assert.That(sem, Has.Length.EqualTo(5));
        });
    }

    [Test]
    public async Task FiltrosDiferentesSeSomam()
    {
        // Categoria E preço E concorrência ao mesmo tempo: cada filtro a mais só diminui.
        var found = await FindAsync(new OpportunityFilterQuery
        {
            Categoria = "MLB1051",
            PrecoMax = 100m,
            Concorrencia = [CompetitionBand.UpTo5]
        });

        Assert.That(found, Is.EqualTo(new[] { "P1" }));
    }

    [Test]
    public async Task OrdenacaoPorPrecoDeixaOSemPrecoNoFim()
    {
        var found = await FindAsync(new OpportunityFilterQuery { Ordenar = SortOption.PriceAsc });

        Assert.That(found, Is.EqualTo(new[] { "P1", "P5", "P3", "P2", "P6", "P4" }),
            "o P4, sem preço medido, não pode encabeçar 'menor preço'");
    }

    [Test]
    public async Task OrdenacaoPorCrescimentoDeixaOSemMedidaNoFim()
    {
        var found = await FindAsync(new OpportunityFilterQuery { Ordenar = SortOption.GrowthDesc });

        Assert.Multiple(() =>
        {
            Assert.That(found[..4], Is.EqualTo(new[] { "P1", "P5", "P6", "P2" }));
            Assert.That(found[4..], Is.EquivalentTo(new[] { "P3", "P4" }), "sem percentual, vão para o fim");
        });
    }

    [Test]
    public async Task PaginacaoNaoRepeteNemPulaItem()
    {
        var (filter, _) = OpportunityFilter.Parse(new OpportunityFilterQuery { PageSize = 2 });
        var seen = new List<string>();
        var total = 0;

        await using var db = Db();
        for (var page = 1; page <= 3; page++)
        {
            var result = await search.SearchAsync(db, filter! with { Page = page }, CancellationToken.None);
            total = result.Total;
            seen.AddRange(result.Items.Select(analysis => analysis.ProductId));
        }

        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(6), "o total é o do filtro inteiro, não o da página");
            Assert.That(seen, Has.Count.EqualTo(6));
            Assert.That(seen.Distinct().Count(), Is.EqualTo(6), "desempate pelo id: nada se repete entre páginas");
        });
    }

    [Test]
    public async Task PaginaVaziaAindaEUmaPagina()
    {
        var (filter, _) = OpportunityFilter.Parse(new OpportunityFilterQuery { Categoria = "MLB9999" });

        await using var db = Db();
        var result = await search.SearchAsync(db, filter!, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.Total, Is.Zero);
            Assert.That(result.TotalPages, Is.EqualTo(1), "zero itens não pode virar divisão por zero");
            Assert.That(result.HasNext, Is.False);
        });
    }

    [Test]
    public async Task MetadadosSoOferecemCategoriaQueTemProduto()
    {
        await using var db = Db();
        var meta = await search.GetMetaAsync(db, CancellationToken.None);

        Assert.Multiple(() =>
        {
            // Ordem alfabética pelo NOME: Celulares, Esportes, Informática.
            Assert.That(meta.Categories.Select(category => category.Id), Is.EqualTo(new[] { "MLB1051", "MLB1276", "MLB1648" }));
            Assert.That(meta.Categories.Sum(category => category.Count), Is.EqualTo(6));
            Assert.That(meta.PriceMin, Is.EqualTo(50m), "só a última análise conta: o preço 10 do P6 é histórico");
            Assert.That(meta.PriceMax, Is.EqualTo(900m));
            Assert.That(meta.TotalProducts, Is.EqualTo(6));
        });
    }
}
