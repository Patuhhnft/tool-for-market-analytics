using System.Globalization;
using System.Net;
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

namespace LeitorMercadoLivre.Infrastructure.Tests.Pipeline;

/// <summary>
/// Monta um banco de DEMONSTRAÇÃO (leitor_demo) para ver o painel com dados sem precisar do
/// token. Não roda na suíte normal: só com --filter "Category=Demo".
/// <para>
/// Usa a pasta config/ real e a PTAX real do Banco Central; só o Mercado Livre é simulado,
/// com produtos de nome obviamente fictício. Nada aqui toca o banco de desenvolvimento.
/// </para>
/// </summary>
[TestFixture, Explicit("Semeia o banco de demonstração."), Category("Demo")]
public sealed class DemoSeed
{
    private const string Connection = "Host=localhost;Port=5432;Database=leitor_demo;Username=leitor;Password=leitor_dev;Timeout=30";

    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Produto → (nome, vendedores com preço, visitas/dia na semana antiga e na recente).</summary>
    private static readonly Dictionary<string, (string Name, decimal[] Prices, int OldDaily, int NewDaily)> Produtos = new()
    {
        ["MLBDEMO1"] = ("[DEMO] Fone Bluetooth TWS", [72m, 75.9m, 77m, 79m, 79m, 79.9m, 80m, 82m, 85m, 85m, 85m, 85m, 89.9m, 94m, 99m], 80, 140),
        ["MLBDEMO2"] = ("[DEMO] Capinha antichoque", [29.9m, 32m, 34.9m, 35m, 35m, 35m, 36m, 38m, 41m], 150, 260),
        ["MLBDEMO3"] = ("[DEMO] Carregador USB-C 20W", [44m, 45m, 49.9m, 52m], 2, 5)
    };

    private static HttpResponseMessage Rota(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;

        if (path == "/highlights/MLB/category/MLB1051")
        {
            var itens = Produtos.Keys.Select((id, i) => $$"""{"id":"{{id}}","position":{{i + 1}},"type":"PRODUCT"}""");
            return StubHttp.Json($$"""{"content":[{{string.Join(',', itens)}}]}""");
        }

        if (path.StartsWith("/highlights/")) return StubHttp.Json("""{"content":[]}""");

        var produto = path.Split('/').ElementAtOrDefault(2) ?? "";
        if (path.StartsWith("/products/") && path.EndsWith("/items"))
        {
            var precos = Produtos[produto].Prices;
            var itens = precos.Select((preco, i) => $$"""
                {"item_id":"{{produto}}-{{i}}","seller_id":{{1000 + i}},"price":{{preco.ToString(CultureInfo.InvariantCulture)}},
                 "category_id":"MLB1055","condition":"new","listing_type_id":"gold_special","official_store_id":null,
                 "shipping":{"free_shipping":true,"logistic_type":"cross_docking","mode":"me2"},"min_purchase_unit":1}
                """);
            return StubHttp.Json($$"""{"paging":{"total":{{precos.Length}},"offset":0,"limit":100},"results":[{{string.Join(',', itens)}}]}""");
        }

        if (path.StartsWith("/products/")) return StubHttp.Json($$"""{"id":"{{produto}}","name":"{{Produtos[produto].Name}}"}""");

        if (path == "/visits/items")
        {
            var ids = Uri.UnescapeDataString(query.Split("ids=")[1]).Split(',');
            return StubHttp.Json("{" + string.Join(',', ids.Select((id, i) => $"\"{id}\":{5000 - (i * 100)}")) + "}");
        }

        if (path.EndsWith("/visits/time_window"))
        {
            var item = path.Split('/')[2];
            var (_, _, antiga, recente) = Produtos[item[..item.LastIndexOf('-')]];
            var dias = Enumerable.Range(0, 16).Select(back =>
            {
                // Um pouco de ruído determinístico, para a curva não ser uma escada.
                var ruido = ((back * 7) % 5) - 2;
                var visitas = back == 0 ? recente / 5 : back <= 7 ? recente + ruido * (recente / 20) : antiga + ruido * (antiga / 20);
                return $$"""{"date":"{{Hoje.AddDays(-back):yyyy-MM-dd}}T00:00:00Z","total":{{Math.Max(0, visitas)}}}""";
            });
            return StubHttp.Json($$"""{"item_id":"{{item}}","results":[{{string.Join(',', dias)}}]}""");
        }

        if (path == "/sites/MLB/listing_prices")
        {
            var preco = decimal.Parse(query.Split("price=")[1].Split('&')[0], CultureInfo.InvariantCulture);
            var fixa = preco < 29 ? 6.25m : preco < 50 ? 6.50m : preco < 79 ? 6.75m : 0m;
            return StubHttp.Json($$$"""[{"listing_type_id":"gold_special","listing_type_name":"Clássico","sale_fee_amount":0,"sale_fee_details":{"fixed_fee":{{{fixa.ToString(CultureInfo.InvariantCulture)}}},"gross_amount":0,"percentage_fee":11}}]""");
        }

        return StubHttp.Json("""{"message":"rota não simulada"}""", HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SemearBancoDeDemonstracao()
    {
        var options = new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(Connection).Options;
        await using (var db = new LeitorDbContext(options))
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();

            // Fornecedor do fone: US$ 6,80 a unidade, 80 por remessa, US$ 60 de frete.
            var agora = DateTimeOffset.UtcNow;
            db.Products.Add(new CatalogProduct { Id = "MLBDEMO1", CategoryId = "MLB1051", FirstSeenAt = agora, LastSeenAt = agora });
            db.Suppliers.Add(new Supplier
            {
                ProductId = "MLBDEMO1", Name = "[DEMO] Shenzhen Audio Co.", UnitPrice = 6.80m, Currency = "USD",
                MinimumOrder = 50, ShipmentQuantity = 80, InternationalFreightTotal = 60m,
                WhatsApp = "+86 138 0000 0000", Url = "https://example.com/fornecedor-demo",
                CreatedAt = agora, UpdatedAt = agora
            });
            await db.SaveChangesAsync();
        }

        var repo = RepositoryRoot.Find();

        var contexts = new PooledFactory(options);
        var apiOptions = Options.Create(new MercadoLivreApiOptions { VisitWindowDays = 15 });
        var client = new MercadoLivreClient(new HttpClient(new StubHttp(Rota)) { BaseAddress = new Uri("https://api.test") }, apiOptions);

        // PTAX real, da API pública do Banco Central.
        var exchange = new ExchangeRateService(contexts, new PtaxClient(new HttpClient { BaseAddress = new Uri("https://olinda.bcb.gov.br/") }), TimeProvider.System);
        var store = new BusinessConfigurationStore(Options.Create(new ConfigurationFolderOptions { Folder = Path.Combine(repo, "config") }));
        var feeTables = new SaleScheduleProvider(client, new MemoryCache(new MemoryCacheOptions()));
        var assessor = new MarginAssessor(exchange, store, TimeProvider.System);

        var pipeline = new CollectionPipeline(contexts, client, feeTables, assessor, store, apiOptions,
            Options.Create(new CollectionOptions()), TimeProvider.System, NullLogger<CollectionPipeline>.Instance);

        var resumo = await pipeline.RunAsync(force: true, CancellationToken.None);

        Assert.That(resumo.Errors, Is.Empty, string.Join(" | ", resumo.Errors));
        TestContext.Out.WriteLine($"Demonstração semeada: {resumo.Analyses} análises no ciclo {resumo.CycleId}.");
    }

    private sealed class PooledFactory(DbContextOptions<LeitorDbContext> options) : IDbContextFactory<LeitorDbContext>
    {
        public LeitorDbContext CreateDbContext() => new(options);
    }
}
