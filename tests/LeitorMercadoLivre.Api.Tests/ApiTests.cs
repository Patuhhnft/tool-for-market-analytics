using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeitorMercadoLivre.Api.Tests;

/// <summary>
/// A API de verdade (o Program real) contra um Postgres de teste descartável e uma pasta de
/// configuração temporária. Existe porque dois bugs passaram por compilação, lint e todos os
/// testes de unidade e só apareceram por HTTP: validação em record posicional e limites
/// decimais lidos na cultura da máquina. Por isso roda em pt-BR, a cultura do usuário.
/// </summary>
[TestFixture, Category("Integration"), NonParallelizable]
public sealed class ApiTests
{
    private const string AdminKey = "chave-de-teste";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    private long operador;
    private string connection = "";
    private string configFolder = "";

    private const string Configuracao = """
        {
          "tributos": [ { "regime": "RC-PF", "vigenteDesde": "2026-05-12", "icms": 0.20, "fonte": "Receita Federal",
            "faixasImpostoImportacao": [ { "ateUsd": 50, "aliquota": 0 }, { "ateUsd": 3000, "aliquota": 0.60, "deducaoUsd": 30 } ] } ],
          "parametros": [ { "vigenteDesde": "2026-01-01", "regimeImportacao": "RC-PF", "impostoSobreVenda": 0.06,
            "margemAlvo": 0.30, "spreadCambial": 0.06, "tipoAnuncio": "gold_special", "limiarFreteGratis": 79,
            "faixasTarifaFixa": [29, 50, 79], "freteAbsorvidoPorUnidade": 20, "freteNacionalPorRemessa": 50,
            "categorias": ["MLB1051"] } ]
        }
        """;

    [OneTimeSetUp]
    public async Task StartApi()
    {
        // A cultura da máquina do usuário: é nela que os limites decimais quebravam.
        CultureInfo.DefaultThreadCurrentCulture = PtBr;
        CultureInfo.DefaultThreadCurrentUICulture = PtBr;
        CultureInfo.CurrentCulture = PtBr;

        try
        {
            await using var probe = new NpgsqlConnection("Host=localhost;Port=5432;Database=leitor;Username=leitor;Password=leitor_dev;Timeout=10");
            await probe.OpenAsync();
        }
        catch (Exception exception) when (exception is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            Assert.Ignore("Postgres local indisponível (docker compose up -d). " + exception.Message);
        }

        connection = $"Host=localhost;Port=5432;Database=leitor_api_{Guid.NewGuid():N};Username=leitor;Password=leitor_dev;Timeout=30";
        configFolder = Path.Combine(Path.GetTempPath(), "leitor-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configFolder);
        File.WriteAllText(Path.Combine(configFolder, "configuracoes.json"), Configuracao);

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Leitor", connection);
            builder.UseSetting("Configuracao:Folder", configFolder);
            builder.UseSetting("Admin:ApiKey", AdminKey);
        });

        client = factory.CreateClient();

        // Toda escrita exige um operador. A tela de seleção faz isso no uso normal; aqui,
        // o setup — senão todo teste de escrita falharia por falta de assinatura.
        var criado = await client.PostAsJsonAsync("/api/operadores", new { nome = "Ana Teste" });
        operador = (await criado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();

        if (!string.IsNullOrEmpty(connection))
        {
            await using var db = new LeitorDbContext(new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options);
            await db.Database.EnsureDeletedAsync();
        }

        if (Directory.Exists(configFolder)) Directory.Delete(configFolder, recursive: true);
    }

    /// <param name="operadorId">
    /// Quem assina a escrita. <c>null</c> omite o cabeçalho — usado só pelos testes que
    /// verificam a recusa.
    /// </param>
    private HttpRequestMessage Admin(
        HttpMethod method, string path, object? body = null, string? key = AdminKey, long? operadorId = -1)
    {
        var request = new HttpRequestMessage(method, path);
        if (key is not null) request.Headers.Add("X-Admin-Key", key);
        // -1 é o sentinela de "use o operador do setup"; qualquer outro valor vai como está.
        var assinatura = operadorId == -1 ? operador : operadorId;
        if (assinatura is { } id) request.Headers.Add("X-Operador-Id", id.ToString(CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task SeedProductAsync(string productId)
    {
        await using var db = new LeitorDbContext(new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options);
        if (await db.Products.AnyAsync(p => p.Id == productId)) return;
        db.Products.Add(new CatalogProduct { Id = productId, CategoryId = "MLB1051", FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private static object Fornecedor(string whatsApp = "+86 139 0000 0000") => new
    {
        name = "Yiwu Cases",
        unitPrice = 0.9m,
        currency = "USD",
        minimumOrder = 100,
        shipmentQuantity = 100,
        internationalFreightTotal = 25m,
        contact = (string?)null,
        whatsApp,
        url = "https://example.com/fornecedor"
    };

    // ----------------------------------------------------------------------------------

    // Roda primeiro: os testes compartilham o banco da fixture, e os de fornecedor criam ciclos.
    [Test, Order(1)]
    public async Task QuadroVazioAntesDoPrimeiroCiclo()
    {
        var board = await client.GetFromJsonAsync<JsonElement>("/api/products");

        Assert.Multiple(() =>
        {
            Assert.That(board.GetProperty("cycle").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(board.GetProperty("products").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public async Task ConfiguracaoVigenteVemDaPasta()
    {
        var config = await client.GetFromJsonAsync<JsonElement>("/api/admin/config");
        var effective = config.GetProperty("effective");

        Assert.Multiple(() =>
        {
            Assert.That(config.GetProperty("problems").GetArrayLength(), Is.Zero);
            Assert.That(effective.GetProperty("taxRule").GetProperty("entry").GetProperty("regime").GetString(), Is.EqualTo("RC-PF"));
        });
    }

    [Test]
    public async Task CadastroDeFornecedorEmPtBr()
    {
        // O caso que dava 500 duas vezes: record posicional validado e decimal em cultura pt-BR.
        await SeedProductAsync("MLBAPI1");

        using var created = await client.SendAsync(Admin(HttpMethod.Post, "/api/products/MLBAPI1/suppliers", Fornecedor()));
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/products/MLBAPI1/suppliers");

        Assert.Multiple(async () =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
            Assert.That(listed.GetArrayLength(), Is.EqualTo(1));
            Assert.That(listed[0].GetProperty("unitPrice").GetDecimal(), Is.EqualTo(0.9m));
            Assert.That(listed[0].GetProperty("whatsAppLink").GetString(), Is.EqualTo("https://wa.me/8613900000000"));
        });
    }

    [Test]
    public async Task WhatsAppAmbiguoERecusadoComExplicacao()
    {
        await SeedProductAsync("MLBAPI2");

        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/products/MLBAPI2/suppliers", Fornecedor("138 1234 5678 9")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain("formato internacional"));
        });
    }

    [Test]
    public async Task FornecedorComPrecoInvalidoERecusadoNaoQuebra()
    {
        await SeedProductAsync("MLBAPI3");
        var invalido = new { name = "X", unitPrice = 0m, currency = "USD", minimumOrder = 1, shipmentQuantity = 1, internationalFreightTotal = 0m };

        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/products/MLBAPI3/suppliers", invalido));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "validação, não erro 500");
    }

    [TestCase(null, HttpStatusCode.Unauthorized)]
    [TestCase("errada", HttpStatusCode.Unauthorized)]
    public async Task EscritaSemAChaveCertaERecusada(string? key, HttpStatusCode expected)
    {
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/cycles/run", key: key));

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task NovaVersaoDeTributoComNomesEmPortugues()
    {
        var regra = new Dictionary<string, object?>
        {
            ["regime"] = "RC-PF",
            ["vigenteDesde"] = "2027-01-01",
            ["faixasImpostoImportacao"] = new[] { new Dictionary<string, object> { ["ateUsd"] = 3000, ["aliquota"] = 0.5m } },
            ["icms"] = 0.18m,
            ["fonte"] = "teste de API — com acento e travessão"
        };

        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/config/tax-rules", regra));
        var saved = File.ReadAllText(Path.Combine(configFolder, "configuracoes.json"));

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(saved, Does.Contain("2027-01-01").And.Contain("com acento e travessão"));
        });
    }

    [Test]
    public async Task AliquotaEmPercentualVoltaComInstrucao()
    {
        var regra = new Dictionary<string, object?>
        {
            ["regime"] = "RC-PF",
            ["vigenteDesde"] = "2027-06-01",
            ["faixasImpostoImportacao"] = new[] { new Dictionary<string, object> { ["aliquota"] = 0.6m } },
            ["icms"] = 18,
            ["fonte"] = "f"
        };

        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/config/tax-rules", regra));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body, Does.Contain("0.18 significa 18%"));
        });
    }

    [Test]
    public async Task UploadInvalidoNaoChegaAPasta()
    {
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/admin/config/files",
            new { nome = "agente.json", conteudo = """{ "tributos": [ { "regime": "X" } ] }""" }));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(File.Exists(Path.Combine(configFolder, "agente.json")), Is.False);
        });
    }

    /// <summary>
    /// Produto já analisado por um ciclo, com a tabela de tarifas gravada na análise e a PTAX
    /// do dia no cache — o estado em que o Worker deixa o banco.
    /// </summary>
    private async Task SeedAnalyzedProductAsync(string productId, bool withFees)
    {
        await using var db = new LeitorDbContext(new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        db.Products.Add(new CatalogProduct { Id = productId, Name = "Capinha", CategoryId = "MLB1051", FirstSeenAt = now, LastSeenAt = now });
        var cycle = new CollectionCycle { WindowStart = now.AddYears(-1).AddMinutes(Random.Shared.Next(100_000)), StartedAt = now, FinishedAt = now, Status = CycleStatus.Completed };
        db.Cycles.Add(cycle);

        if (!await db.ExchangeRates.AnyAsync(q => q.RequestedDate == today))
        {
            db.ExchangeRates.Add(new ExchangeRateQuote { Currency = "USD", RequestedDate = today, QuoteDate = today, Sell = 5.14m, FetchedAt = now });
        }

        await db.SaveChangesAsync();

        var listings = Enumerable.Range(0, 9)
            .Select(i => new LeitorMercadoLivre.Domain.Pricing.SellerListing($"S{i}", $"L{i}", 30m + i, productId))
            .ToList();
        var price = LeitorMercadoLivre.Domain.Pricing.PriceReferenceCalculator.Calculate(
            productId, listings, new LeitorMercadoLivre.Domain.Pricing.PriceReferenceOptions());

        var fees = withFees
            ? new LeitorMercadoLivre.Infrastructure.Pipeline.FeeSnapshot("gold_special", [29m, 50m, 79m],
            [
                new(29m, 0.11m, 6.25m), new(50m, 0.11m, 6.50m), new(79m, 0.11m, 6.75m), new(decimal.MaxValue, 0.11m, 0m)
            ])
            : null;

        var demand = LeitorMercadoLivre.Domain.Demand.DemandCalculator.FromRanking(3, 8, new LeitorMercadoLivre.Domain.Demand.DemandOptions());

        // O teto de compra sai da MESMA função que o ciclo usa — o seed chama a produção em vez
        // de repetir a conta, senão o teste passaria a conferir a cópia, não o sistema.
        var effective = new LeitorMercadoLivre.Infrastructure.Configuration.BusinessConfigurationStore(
                Microsoft.Extensions.Options.Options.Create(
                    new LeitorMercadoLivre.Infrastructure.Configuration.ConfigurationFolderOptions { Folder = configFolder }))
            .ResolveAt(today).Configuration!;

        var ceiling = LeitorMercadoLivre.Infrastructure.Pipeline.MarginAssessor.Ceiling(price, "MLB1055", "MLB1051", fees, effective, now);

        db.Analyses.Add(new ProductAnalysis
        {
            CycleId = cycle.Id,
            ProductId = productId,
            CalculatedAt = now,
            PriceStatus = price.Status.ToString(),
            MarketPrice = price.MarketPrice,
            SellersFound = price.SellersFound,
            PriceJson = LeitorMercadoLivre.Infrastructure.Pipeline.AnalysisJson.Write(
                new LeitorMercadoLivre.Infrastructure.Pipeline.PricePayload(price, "MLB1055", fees)),
            DemandJson = LeitorMercadoLivre.Infrastructure.Pipeline.AnalysisJson.Write(
                new LeitorMercadoLivre.Infrastructure.Pipeline.DemandPayload(demand, [], null)),
            CeilingJson = ceiling is null
                ? null
                : LeitorMercadoLivre.Infrastructure.Pipeline.AnalysisJson.Write(ceiling)
        });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task FornecedorNovoMostraMargemNaHoraSemFalarComOMercadoLivre()
    {
        // A fábrica de teste não tem credencial nenhuma do Mercado Livre: se a API tentasse
        // consultar tarifas, o recálculo quebraria. As tarifas vêm da análise gravada.
        await SeedAnalyzedProductAsync("MLBAPI4", withFees: true);

        using var created = await client.SendAsync(Admin(HttpMethod.Post, "/api/products/MLBAPI4/suppliers", Fornecedor()));
        var card = await client.GetFromJsonAsync<JsonElement>("/api/products/MLBAPI4");
        var margin = card.GetProperty("margin");

        Assert.Multiple(async () =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
            Assert.That(margin.GetProperty("unavailable").ValueKind, Is.EqualTo(JsonValueKind.Null), margin.ToString());
            Assert.That(margin.GetProperty("exchange").GetProperty("ptax").GetDecimal(), Is.EqualTo(5.14m));
            Assert.That(margin.GetProperty("margin").GetProperty("market").GetProperty("saleCost").GetProperty("fixedFee").GetDecimal(), Is.EqualTo(6.50m),
                "preço de mercado em R$ 34 cai na faixa de 29 a 50");
            Assert.That(card.GetProperty("opportunity").GetProperty("includesMargin").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task SemTabelaDeTarifasAMargemExplicaEmVezDeQuebrar()
    {
        await SeedAnalyzedProductAsync("MLBAPI5", withFees: false);

        using var created = await client.SendAsync(Admin(HttpMethod.Post, "/api/products/MLBAPI5/suppliers", Fornecedor()));
        var card = await client.GetFromJsonAsync<JsonElement>("/api/products/MLBAPI5");

        Assert.Multiple(() =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(card.GetProperty("margin").GetProperty("unavailable").GetString(), Does.Contain("próximo ciclo"));
        });
    }

    [Test]
    public async Task ColetarAgoraEnfileira()
    {
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/cycles/run"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    // ----------------------------------------------------------------------------------
    // Operador. Identificação por boa-fé: serve para atribuir autoria, não para barrar.
    // ----------------------------------------------------------------------------------

    [Test]
    public async Task EscritaSemOperadorERecusadaComoPedidoMalformado()
    {
        await SeedProductAsync("MLBOP1");

        using var response = await client.SendAsync(
            Admin(HttpMethod.Post, "/api/products/MLBOP1/suppliers", Fornecedor(), operadorId: null));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            // 400, não 401: falta dizer QUEM está trabalhando, não falta credencial.
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain("X-Operador-Id"));
        });
    }

    [Test]
    public async Task EscritaComOperadorInexistenteERecusada()
    {
        await SeedProductAsync("MLBOP2");

        using var response = await client.SendAsync(
            Admin(HttpMethod.Post, "/api/products/MLBOP2/suppliers", Fornecedor(), operadorId: 999_999));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ChaveDeAdminContinuaSendoQuemBarra()
    {
        // O operador não substitui a chave: sem ela, continua 401 mesmo assinando.
        using var response = await client.SendAsync(Admin(HttpMethod.Post, "/api/cycles/run", key: null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("José")]
    [TestCase("JOSE")]
    [TestCase("  josé  ")]
    public async Task MesmoNomeEscritoDeFormasDiferentesNaoDuplicaOperador(string nome)
    {
        // Se virassem três operadores, a auditoria se partiria em três e nenhuma contaria
        // a história inteira.
        using var primeiro = await client.PostAsJsonAsync("/api/operadores", new { nome = "José" });
        var esperado = (await primeiro.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        using var repetido = await client.PostAsJsonAsync("/api/operadores", new { nome });
        var obtido = (await repetido.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        Assert.That(obtido, Is.EqualTo(esperado));
    }

    [TestCase("", "Digite um nome")]
    [TestCase("   ", "Digite um nome")]
    [TestCase("Ana<script>", "letras")]
    public async Task NomeInvalidoERecusadoComExplicacao(string nome, string trecho)
    {
        using var response = await client.PostAsJsonAsync("/api/operadores", new { nome });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain(trecho));
        });
    }

    [Test]
    public async Task FornecedorGuardaQuemCadastrou()
    {
        await SeedProductAsync("MLBOP3");

        using var criado = await client.SendAsync(Admin(HttpMethod.Post, "/api/products/MLBOP3/suppliers", Fornecedor()));
        Assert.That(criado.StatusCode, Is.EqualTo(HttpStatusCode.Created), await criado.Content.ReadAsStringAsync());

        await using var db = new LeitorDbContext(new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options);
        var fornecedor = await db.Suppliers.AsNoTracking().FirstAsync(f => f.ProductId == "MLBOP3");

        Assert.Multiple(() =>
        {
            Assert.That(fornecedor.CreatedByOperatorId, Is.EqualTo(operador));
            Assert.That(fornecedor.UpdatedByOperatorId, Is.EqualTo(operador));
        });
    }

    [Test]
    public async Task EscolherOperadorRegistraASessaoDeTrabalho()
    {
        using var response = await client.PostAsync($"/api/operadores/{operador}/sessoes", null);

        await using var db = new LeitorDbContext(new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options);
        var sessoes = await db.WorkSessions.CountAsync(s => s.OperatorId == operador);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(sessoes, Is.GreaterThan(0));
        });
    }

    // ----------------------------------------------------------------------------------
    // Explorar oportunidades. A area e nova; o quadro de sempre nao pode ter mudado.
    // ----------------------------------------------------------------------------------

    [Test]
    public async Task ExplorarRecusaFiltroInvalidoListandoTodosOsProblemas()
    {
        using var response = await client.GetAsync(
            "/api/oportunidades/explorar?crescimento=muito&concorrencia=zzz&ordenar=nada&precoMin=500&precoMax=100&page=0");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            // Uma resposta, todos os erros: quem monta a URL na mao corrige tudo de uma vez.
            Assert.That(body.GetProperty("errors").GetProperty("filtro").GetArrayLength(), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task ExplorarSemFiltroAindaEUmaPagina()
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/oportunidades/explorar?categoria=MLB9999");

        Assert.Multiple(() =>
        {
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.Zero);
            Assert.That(page.GetProperty("total").GetInt32(), Is.Zero);
            Assert.That(page.GetProperty("totalPages").GetInt32(), Is.EqualTo(1), "zero itens nao vira divisao por zero");
            Assert.That(page.GetProperty("hasNext").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task MetaDeFiltrosVemDoServidor()
    {
        var meta = await client.GetFromJsonAsync<JsonElement>("/api/oportunidades/explorar/filtros-meta");

        Assert.Multiple(() =>
        {
            // Nenhum limiar mora no front (I8): ate o rotulo de "explodindo" carrega o numero.
            Assert.That(meta.GetProperty("growth").GetArrayLength(), Is.EqualTo(4));
            Assert.That(meta.GetProperty("sort").GetArrayLength(), Is.EqualTo(5));
            Assert.That(meta.GetProperty("growth")[3].GetProperty("label").GetString(), Does.Contain("%"));
        });
    }

    [Test]
    public async Task ExplorarDevolveExatamenteOCardDoQuadro()
    {
        await SeedAnalyzedProductAsync("MLBAPI6", withFees: true);

        var found = await client.GetFromJsonAsync<JsonElement>("/api/oportunidades/explorar?categoria=MLB1051&pageSize=100");
        var direct = await client.GetFromJsonAsync<JsonElement>("/api/products/MLBAPI6");

        var fromSearch = found.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("productId").GetString() == "MLBAPI6");

        // O card e o mesmo objeto, campo por campo: a area nova muda QUAIS produtos aparecem,
        // nunca o que um produto mostra.
        Assert.That(fromSearch.GetRawText(), Is.EqualTo(direct.GetRawText()));
    }

    [Test]
    public async Task TetoDeCompraApareceSemFornecedorCadastrado()
    {
        // Nenhum fornecedor para MLBAPI8: ainda assim o card diz quanto vale a pena pagar.
        await SeedAnalyzedProductAsync("MLBAPI8", withFees: true);

        var card = await client.GetFromJsonAsync<JsonElement>("/api/products/MLBAPI8");
        var ceiling = card.GetProperty("ceiling").GetProperty("ceiling");

        Assert.Multiple(() =>
        {
            Assert.That(card.GetProperty("margin").ValueKind, Is.EqualTo(JsonValueKind.Null), "sem fornecedor, sem margem");
            Assert.That(card.GetProperty("suppliers").GetArrayLength(), Is.Zero);
            Assert.That(ceiling.GetProperty("inRange").GetDecimal(), Is.GreaterThan(0m));
            // O teto seguro nunca passa do teto no preço de mercado: ele aguenta a faixa inteira.
            Assert.That(ceiling.GetProperty("inRange").GetDecimal(),
                Is.LessThanOrEqualTo(ceiling.GetProperty("atMarket").GetDecimal()));
            Assert.That(ceiling.GetProperty("viable").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task QuadroMantemOsCamposDeSempre()
    {
        await SeedAnalyzedProductAsync("MLBAPI7", withFees: true);

        var board = await client.GetFromJsonAsync<JsonElement>("/api/products");
        var card = board.GetProperty("products").EnumerateArray().First();

        string[] esperados =
        [
            "productId", "name", "categoryId", "cycleId", "calculatedAt", "quadrant", "opportunityIndex",
            "lowDemand", "explanation", "sellers", "newSellersThisWeek", "price", "priceTrend", "priceUnstable",
            "feeCategoryId", "demand", "margin", "ceiling", "runway", "risks", "opportunity", "suppliers"
        ];

        Assert.Multiple(() =>
        {
            Assert.That(board.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "cycle", "lastAttempt", "products" }));
            Assert.That(card.EnumerateObject().Select(p => p.Name), Is.EqualTo(esperados),
                "o contrato de /api/products nao muda por causa da busca nova");
            Assert.That(card.GetProperty("newSellersThisWeek").ValueKind, Is.EqualTo(JsonValueKind.Number),
                "a coluna passou a aceitar nulo; o contrato continua mandando numero");
        });
    }
}
