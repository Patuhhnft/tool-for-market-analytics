using System.Net;
using System.Text;
using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.MercadoLivre;
using LeitorMercadoLivre.Infrastructure.Pricing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.Tests.MercadoLivre;

/// <summary>Responde por rota e registra o que foi pedido.</summary>
internal sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return route(request);
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class MemoryCredentialStore(StoredCredential? initial = null) : ICredentialStore
{
    public StoredCredential? Current { get; private set; } = initial;

    public int Saves { get; private set; }

    public Task<StoredCredential?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task SaveAsync(StoredCredential credential, CancellationToken cancellationToken)
    {
        Current = credential;
        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

// ======================================================================================
// Cliente — com as respostas REAIS capturadas no diagnóstico de 24/09/2026
// ======================================================================================

public sealed class MercadoLivreClientTests
{
    private const string HighlightsReal = """
        {"query_data":{"highlight_type":"BEST_SELLER","criteria":"CATEGORY","id":"MLB1051"},"content":[
          {"id":"MLB54982411","position":1,"type":"PRODUCT"},
          {"id":"MLB54961556","position":2,"type":"PRODUCT"},
          {"id":"MLBU3452892927","position":20,"type":"USER_PRODUCT"}]}
        """;

    private const string ItemReal = """
        {"item_id":"MLB6716754230","site_id":"MLB","seller_id":723878806,"accepts_mercadopago":true,"price":1140,
         "category_id":"MLB1055","currency_id":"BRL","warranty":"Garantia de fábrica: 12 meses","condition":"new",
         "listing_type_id":"gold_special","international_delivery_mode":"none","tier":"","inventory_id":"",
         "tags":["kvs_primary"],"deal_ids":[],"official_store_id":null,"original_price":null,
         "shipping":{"free_shipping":true,"store_pick_up":false,"local_pick_up":false,"mode":"me2","logistic_type":"cross_docking","tags":[],"cost":0},
         "seller_address":{"city":{"id":"X","name":"São Paulo"}},"sale_terms":[],"user_product_id":"MLBU3936061369","min_purchase_unit":1}
        """;

    private const string VisitasJanelaReal = """
        {"item_id":"MLB6716754230","date_from":"2026-09-17T00:00:00Z","date_to":"2026-09-24T00:00:00Z","total_visits":803,"last":7,"unit":"day",
         "results":[{"date":"2026-09-21T00:00:00Z","total":17,"visits_detail":[{"company":"mercadolibre","quantity":17}]},
                    {"date":"2026-09-23T00:00:00Z","total":389,"visits_detail":[]},
                    {"date":"2026-09-24T00:00:00Z","total":11,"visits_detail":[]},
                    {"date":"2026-09-22T00:00:00Z","total":386,"visits_detail":[]}]}
        """;

    private const string ListingPricesReal = """
        [{"currency_id":"BRL","free_relist":false,"listing_exposure":"highest","listing_fee_amount":0,"listing_fee_details":{"fixed_fee":0,"gross_amount":0},
          "listing_type_id":"gold_pro","listing_type_name":"Premium","requires_picture":true,"sale_fee_amount":16,
          "sale_fee_details":{"fixed_fee":0,"gross_amount":16,"percentage_fee":16},"stop_time":"2046-09-19T00:00:00.000-04:00"},
         {"currency_id":"BRL","free_relist":false,"listing_exposure":"highest","listing_fee_amount":0,"listing_fee_details":{"fixed_fee":0,"gross_amount":0},
          "listing_type_id":"gold_special","listing_type_name":"Clássico","requires_picture":true,"sale_fee_amount":11,
          "sale_fee_details":{"fixed_fee":0,"gross_amount":11,"percentage_fee":11},"stop_time":"2046-09-19T00:00:00.000-04:00"}]
        """;

    private static (MercadoLivreClient Client, StubHttp Http) Create(Func<HttpRequestMessage, HttpResponseMessage> route)
    {
        var http = new StubHttp(route);
        var client = new MercadoLivreClient(
            new HttpClient(http) { BaseAddress = new Uri("https://api.test") },
            Options.Create(new MercadoLivreApiOptions()));
        return (client, http);
    }

    [Test]
    public async Task HighlightsSeparaProdutoDeCatalogoDeUserProduct()
    {
        var (client, http) = Create(_ => StubHttp.Json(HighlightsReal));

        var destaques = await client.GetHighlightsAsync("MLB1051", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(http.Requests.Single().RequestUri!.PathAndQuery, Is.EqualTo("/highlights/MLB/category/MLB1051"));
            Assert.That(destaques, Has.Count.EqualTo(3));
            Assert.That(destaques.Count(d => d.IsCatalogProduct), Is.EqualTo(2));
            Assert.That(destaques.Single(d => !d.IsCatalogProduct).Id, Is.EqualTo("MLBU3452892927"));
        });
    }

    [Test]
    public async Task AnunciosDoProdutoLeOFormatoRealEPagina()
    {
        // 150 anúncios: página de 100 e página de 50.
        var (client, http) = Create(request =>
        {
            var offset = request.RequestUri!.Query.Contains("offset=100") ? 100 : 0;
            var count = offset == 0 ? 100 : 50;
            var items = string.Join(',', Enumerable.Range(0, count).Select(_ => ItemReal));
            return StubHttp.Json($$"""{"paging":{"total":150,"offset":{{offset}},"limit":100},"results":[{{items}}]}""");
        });

        var anuncios = await client.GetProductListingsAsync("MLB54982411", CancellationToken.None);
        var primeiro = anuncios[0];

        Assert.Multiple(() =>
        {
            Assert.That(http.Requests, Has.Count.EqualTo(2));
            Assert.That(anuncios, Has.Count.EqualTo(150));
            Assert.That(primeiro.ItemId, Is.EqualTo("MLB6716754230"));
            Assert.That(primeiro.SellerId, Is.EqualTo(723878806L));
            Assert.That(primeiro.Price, Is.EqualTo(1140m));
            Assert.That(primeiro.ListingTypeId, Is.EqualTo("gold_special"));
            Assert.That(primeiro.Shipping!.FreeShipping, Is.True);
            Assert.That(primeiro.Shipping.LogisticType, Is.EqualTo("cross_docking"));
            Assert.That(primeiro.OfficialStoreId, Is.Null);
        });
    }

    [Test]
    public async Task LoteRecusadoEDivididoAteAApiAceitar()
    {
        // A primeira coleta real levou 400 num lote de 50. Aqui a API só aceita até 5 ids.
        var (client, http) = Create(request =>
        {
            var ids = Uri.UnescapeDataString(request.RequestUri!.Query.Split("ids=")[1]).Split(',');
            return ids.Length > 5
                ? StubHttp.Json("""{"message":"too many ids"}""", HttpStatusCode.BadRequest)
                : StubHttp.Json("{" + string.Join(',', ids.Select(id => $"\"{id}\":7")) + "}");
        });

        var totais = await client.GetTotalVisitsAsync(Enumerable.Range(1, 20).Select(i => $"MLB{i}"), CancellationToken.None);

        Assert.That(totais, Has.Count.EqualTo(20), "nenhum anúncio perdido pela recusa do lote");
    }

    [Test]
    public async Task VisitasVaoUmIdPorChamada()
    {
        // O endpoint aceita o parâmetro "ids" no plural, mas responde
        // "maximum amount of items to query is 1" para qualquer lote maior — medido na API
        // real em 25/09/2026. Este teste trava esse fato: se voltarmos a agrupar, ele acusa.
        var (client, http) = Create(request =>
        {
            var ids = Uri.UnescapeDataString(request.RequestUri!.Query.Split("ids=")[1]).Split(',');
            return ids.Length > 1
                ? StubHttp.Json("""{"message":"maximum amount of items to query is 1","error":"validation_parameters"}""", HttpStatusCode.BadRequest)
                : StubHttp.Json($"{{\"{ids[0]}\":10}}");
        });

        var totais = await client.GetTotalVisitsAsync(Enumerable.Range(1, 12).Select(i => $"MLB{i}"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(totais, Has.Count.EqualTo(12));
            Assert.That(http.Requests, Has.Count.EqualTo(12), "uma chamada por anúncio, sem 400 nenhum");
            Assert.That(http.Requests.Select(r => Uri.UnescapeDataString(r.RequestUri!.Query).Split("ids=")[1]),
                Has.All.Not.Contains(","), "nenhuma requisição com mais de um id");
        });
    }

    [Test]
    public async Task SerieDeVisitasDevolveSoOsDiasQueVieram()
    {
        // O preenchimento com zero e o corte do dia corrente são do domínio, não do cliente.
        var (client, _) = Create(_ => StubHttp.Json(VisitasJanelaReal));

        var dias = await client.GetDailyVisitsAsync("MLB6716754230", 7, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dias, Has.Count.EqualTo(4));
            Assert.That(dias.Single(d => d.Date == new DateOnly(2026, 9, 22)).Visits, Is.EqualTo(386));
        });
    }

    [Test]
    public async Task TarifaVemComoFracaoEComCategoria()
    {
        var (client, http) = Create(_ => StubHttp.Json(ListingPricesReal));

        var tarifas = await client.GetListingPricesAsync(100m, "MLB1055", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(http.Requests.Single().RequestUri!.Query, Is.EqualTo("?price=100&category_id=MLB1055"));
            Assert.That(tarifas.Single(t => t.ListingTypeId == "gold_pro").PercentageRate, Is.EqualTo(0.16m));
            Assert.That(tarifas.Single(t => t.ListingTypeId == "gold_special").PercentageRate, Is.EqualTo(0.11m));
        });
    }

    [Test]
    public async Task NomeDoProdutoRecusadoViraNuloNaoErro()
    {
        var (client, _) = Create(_ => StubHttp.Json("""{"message":"forbidden"}""", HttpStatusCode.Forbidden));

        Assert.That(await client.GetProductNameAsync("MLB1", CancellationToken.None), Is.Null);
    }

    [Test]
    public void ErroDeServidorSobeComOCaminhoNaMensagem()
    {
        var (client, _) = Create(_ => StubHttp.Json("""{"message":"boom"}""", HttpStatusCode.InternalServerError));

        var erro = Assert.ThrowsAsync<HttpRequestException>(() => client.GetHighlightsAsync("MLB1", CancellationToken.None));

        Assert.That(erro!.Message, Does.Contain("/highlights/MLB/category/MLB1").And.Contain("500"));
    }
}

// ======================================================================================
// Token — renovação, rotação do refresh de uso único, 401
// ======================================================================================

public sealed class MercadoLivreTokenTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static MercadoLivreApiOptions Opcoes(string? bootstrapRefresh = null) => new()
    {
        BaseUrl = "https://api.test",
        ClientId = "2727129198048698",
        ClientSecret = "segredo",
        BootstrapRefreshToken = bootstrapRefresh
    };

    private static int _emitidos;

    private static HttpResponseMessage TokenNovo() =>
        StubHttp.Json($$"""{"access_token":"APP_USR-novo-{{Interlocked.Increment(ref _emitidos)}}","token_type":"Bearer","expires_in":21600,"refresh_token":"TG-refresh-{{_emitidos}}","user_id":384756070}""");

    private static MercadoLivreTokenProvider Provider(ICredentialStore store, StubHttp auth, MercadoLivreApiOptions options, TimeProvider? clock = null) =>
        new(store, new SingleClientFactory(auth), Options.Create(options), clock ?? new FixedClock(Agora), NullLogger<MercadoLivreTokenProvider>.Instance);

    [Test]
    public async Task TokenValidoNaoGastaRenovacao()
    {
        var store = new MemoryCredentialStore(new StoredCredential("valido", "TG-r", Agora.AddHours(3)));
        var auth = new StubHttp(_ => TokenNovo());

        var token = await Provider(store, auth, Opcoes()).GetAccessTokenAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(token, Is.EqualTo("valido"));
            Assert.That(auth.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task TokenVencendoRenovaEGravaORefreshNovo()
    {
        var store = new MemoryCredentialStore(new StoredCredential("velho", "TG-usado", Agora.AddMinutes(2)));
        var auth = new StubHttp(_ => TokenNovo());

        var token = await Provider(store, auth, Opcoes()).GetAccessTokenAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(token, Does.StartWith("APP_USR-novo-"));
            Assert.That(auth.Bodies.Single(), Does.Contain("grant_type=refresh_token").And.Contain("refresh_token=TG-usado"));
            Assert.That(store.Current!.RefreshToken, Does.StartWith("TG-refresh-"), "o refresh de uso único foi trocado e gravado");
            Assert.That(store.Current.ExpiresAt, Is.EqualTo(Agora.AddSeconds(21600)));
        });
    }

    [Test]
    public async Task DoisQuatrocentosEUmSimultaneosRenovamUmaVezSo()
    {
        // O cenário que mataria o ciclo: duas chamadas levam 401 com o mesmo token e as duas
        // tentam renovar. A segunda gastaria um refresh já consumido.
        var store = new MemoryCredentialStore(new StoredCredential("recusado", "TG-r", Agora.AddHours(3)));
        var auth = new StubHttp(_ => TokenNovo());
        var provider = Provider(store, auth, Opcoes());

        var tokens = await Task.WhenAll(
            provider.GetAccessTokenAsync(CancellationToken.None, rejectedToken: "recusado"),
            provider.GetAccessTokenAsync(CancellationToken.None, rejectedToken: "recusado"));

        Assert.Multiple(() =>
        {
            Assert.That(auth.Requests, Has.Count.EqualTo(1));
            Assert.That(tokens[0], Is.EqualTo(tokens[1]));
        });
    }

    [Test]
    public async Task PrimeiraExecucaoUsaORefreshDoAmbiente()
    {
        var store = new MemoryCredentialStore();
        var auth = new StubHttp(_ => TokenNovo());

        var token = await Provider(store, auth, Opcoes(bootstrapRefresh: "TG-do-ambiente")).GetAccessTokenAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(token, Does.StartWith("APP_USR-novo-"));
            Assert.That(auth.Bodies.Single(), Does.Contain("refresh_token=TG-do-ambiente"));
            Assert.That(store.Saves, Is.EqualTo(2), "grava o do ambiente e depois o renovado");
        });
    }

    [Test]
    public void SemCredencialNenhumaExplicaOQueConfigurar()
    {
        var provider = Provider(new MemoryCredentialStore(), new StubHttp(_ => TokenNovo()), Opcoes());

        var erro = Assert.ThrowsAsync<MercadoLivreAuthException>(() => provider.GetAccessTokenAsync(CancellationToken.None));

        Assert.That(erro!.Message, Does.Contain("MercadoLivre__BootstrapRefreshToken"));
    }

    [Test]
    public void RenovacaoRecusadaMandaRefazerAAutorizacao()
    {
        var store = new MemoryCredentialStore(new StoredCredential("velho", "TG-revogado", Agora.AddMinutes(-1)));
        var auth = new StubHttp(_ => StubHttp.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest));

        var erro = Assert.ThrowsAsync<MercadoLivreAuthException>(() => Provider(store, auth, Opcoes()).GetAccessTokenAsync(CancellationToken.None));

        Assert.That(erro!.Message, Does.Contain("Refaça a autorização"));
    }

    [Test]
    public async Task QuatrocentosEUmRenovaERepeteComOTokenNovo()
    {
        var store = new MemoryCredentialStore(new StoredCredential("recusado-pela-ml", "TG-r", Agora.AddHours(3)));
        var provider = Provider(store, new StubHttp(_ => TokenNovo()), Opcoes());

        var api = new StubHttp(request => request.Headers.Authorization!.Parameter == "recusado-pela-ml"
            ? StubHttp.Json("{}", HttpStatusCode.Unauthorized)
            : StubHttp.Json("""{"content":[]}"""));

        var http = new HttpClient(new MercadoLivreAuthHandler(provider) { InnerHandler = api }) { BaseAddress = new Uri("https://api.test") };
        using var resposta = await http.GetAsync("/highlights/MLB/category/MLB1");

        Assert.Multiple(() =>
        {
            Assert.That(resposta.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(api.Requests, Has.Count.EqualTo(2));
            Assert.That(api.Requests[1].Headers.Authorization!.Parameter, Does.StartWith("APP_USR-novo-"));
        });
    }

    [Test]
    public async Task QuatrocentosEVinteENoveEsperaERepete()
    {
        var chamadas = 0;
        var api = new StubHttp(_ => ++chamadas < 3 ? StubHttp.Json("{}", HttpStatusCode.TooManyRequests) : StubHttp.Json("{}"));
        var opcoes = Options.Create(new MercadoLivreApiOptions { RetryBaseDelay = TimeSpan.Zero });
        var http = new HttpClient(new RetryHandler(opcoes, TimeProvider.System) { InnerHandler = api }) { BaseAddress = new Uri("https://api.test") };

        using var resposta = await http.GetAsync("/x");

        Assert.Multiple(() =>
        {
            Assert.That(resposta.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(chamadas, Is.EqualTo(3));
        });
    }
}

// ======================================================================================
// Câmbio e tarifas
// ======================================================================================

public sealed class ExchangeAndFeesTests
{
    [Test]
    public async Task PtaxVoltaAteOUltimoDiaUtil()
    {
        // Domingo 20/09 e sábado 19/09 vazios, como a API real respondeu; sexta 18/09 com cotação.
        var http = new StubHttp(request => request.RequestUri!.Query.Contains("09-18-2026")
            ? StubHttp.Json("""{"value":[{"cotacaoCompra":5.1400,"cotacaoVenda":5.1406,"dataHoraCotacao":"2026-09-18 13:04:22.36"}]}""")
            : StubHttp.Json("""{"value":[]}"""));
        var ptax = new PtaxClient(new HttpClient(http) { BaseAddress = new Uri("https://olinda.test/") });

        var cotacao = await ptax.GetUsdSellAsync(new DateOnly(2026, 9, 20), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(cotacao!.Value.QuoteDate, Is.EqualTo(new DateOnly(2026, 9, 18)));
            Assert.That(cotacao.Value.Sell, Is.EqualTo(5.1406m));
            Assert.That(http.Requests, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task TabelaDeTarifasPerguntaUmPrecoDentroDeCadaFaixa()
    {
        // Tarifa fixa por faixa como a API responderia; comissão 11% no Clássico.
        var http = new StubHttp(request =>
        {
            var preco = decimal.Parse(request.RequestUri!.Query.Split("price=")[1].Split('&')[0], System.Globalization.CultureInfo.InvariantCulture);
            var fixa = preco < 29 ? 6.25m : preco < 50 ? 6.50m : preco < 79 ? 6.75m : 0m;
            return StubHttp.Json($$$"""[{"listing_type_id":"gold_special","listing_type_name":"Clássico","sale_fee_amount":0,"sale_fee_details":{"fixed_fee":{{{fixa.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"gross_amount":0,"percentage_fee":11}}]""");
        });
        var client = new MercadoLivreClient(new HttpClient(http) { BaseAddress = new Uri("https://api.test") }, Options.Create(new MercadoLivreApiOptions()));
        var provider = new SaleScheduleProvider(client, new MemoryCache(new MemoryCacheOptions()));
        var parametros = new ParametersEntry
        {
            ListingTypeId = "gold_special", FeeBandLimits = [29m, 50m, 79m], FreeShippingThreshold = 79m,
            AbsorbedFreightPerUnit = 20m, SaleTaxRate = 0.06m, Categories = ["MLB1051"]
        };

        var tabela = await provider.GetAsync(parametros, "MLB1055", CancellationToken.None);
        await provider.GetAsync(parametros, "MLB1055", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(http.Requests.Select(r => r.RequestUri!.Query), Is.EqualTo(new[]
            {
                "?price=28&category_id=MLB1055", "?price=49&category_id=MLB1055",
                "?price=78&category_id=MLB1055", "?price=100&category_id=MLB1055"
            }), "e a segunda chamada saiu do cache");
            Assert.That(tabela.At(20m).FixedFee, Is.EqualTo(6.25m));
            Assert.That(tabela.At(60m).FixedFee, Is.EqualTo(6.75m));
            Assert.That(tabela.At(100m).FixedFee, Is.Zero);
            Assert.That(tabela.At(100m).AbsorbedFreight, Is.EqualTo(20m));
            Assert.That(tabela.At(100m).Commission, Is.EqualTo(11m));
        });
    }
}
