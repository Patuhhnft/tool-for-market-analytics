using LeitorMercadoLivre.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.Tests.Configuration;

public sealed class BusinessConfigurationStoreTests
{
    private string folder = "";

    [SetUp]
    public void CreateFolder()
    {
        folder = Path.Combine(Path.GetTempPath(), "leitor-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private BusinessConfigurationStore Store(string? path = null) =>
        new(Options.Create(new ConfigurationFolderOptions { Folder = path ?? folder }));

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(folder, name), json);

    private const string Tributo2026 = """
        { "regime": "RC-PF", "vigenteDesde": "2026-05-12",
          "faixasImpostoImportacao": [ { "ateUsd": 50, "aliquota": 0 }, { "ateUsd": 3000, "aliquota": 0.60, "deducaoUsd": 30 } ],
          "icms": 0.20, "fonte": "Receita Federal" }
        """;

    private static string Parametros(string vigencia = "2026-09-01", string regime = "RC-PF") => $$"""
        { "vigenteDesde": "{{vigencia}}", "regimeImportacao": "{{regime}}", "impostoSobreVenda": 0.06,
          "margemAlvo": 0.30, "spreadCambial": 0.06, "tipoAnuncio": "gold_special", "limiarFreteGratis": 79,
          "faixasTarifaFixa": [29, 50, 79], "freteAbsorvidoPorUnidade": 20, "freteNacionalPorRemessa": 50,
          "categorias": ["MLB1051"] }
        """;

    private static string Documento(string tributos, string parametros) =>
        $$"""{ "tributos": [ {{tributos}} ], "parametros": [ {{parametros}} ] }""";

    // ----------------------------------------------------------------------------------

    [Test]
    public void OArquivoSementeDoRepositorioCarregaSemErroEResolveARegraDeHoje()
    {
        var repo = RepositoryRoot.Find();

        var store = Store(Path.Combine(repo, "config"));
        var carregada = store.Load();
        var vigente = store.ResolveAt(new DateOnly(2026, 9, 24), carregada);

        Assert.Multiple(() =>
        {
            Assert.That(carregada.Files, Has.All.Matches<ConfigFileStatus>(f => f.Accepted), string.Join(" | ", carregada.Files.SelectMany(f => f.Errors)));
            Assert.That(carregada.Errors, Is.Empty);
            Assert.That(vigente.Problems, Is.Empty);
            Assert.That(vigente.Configuration!.TaxRule.Entry.Regime, Is.EqualTo("Remessa Conforme — pessoa física"));
            Assert.That(vigente.Configuration.ImportTaxRates.ImportDutyBrackets, Has.Count.EqualTo(2));
            Assert.That(vigente.Configuration.ImportTaxRates.BracketFor(40m).Rate, Is.Zero);
            Assert.That(vigente.Configuration.ImportTaxRates.BracketFor(100m).DeductionForeign, Is.EqualTo(30m));
        });
    }

    [Test]
    public void ResolveAVersaoVigenteNaData()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros()));

        var vigente = Store().ResolveAt(new DateOnly(2026, 9, 24));

        Assert.Multiple(() =>
        {
            Assert.That(vigente.Problems, Is.Empty);
            Assert.That(vigente.Configuration!.Margin.TargetMargin, Is.EqualTo(0.30m));
            Assert.That(vigente.Configuration.Parameters.File, Is.EqualTo("configuracoes.json"));
        });
    }

    [Test]
    public void AntesDeQualquerVigenciaNaoInventaConfiguracao()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros("2026-09-01")));

        var vigente = Store().ResolveAt(new DateOnly(2026, 8, 31));

        Assert.Multiple(() =>
        {
            Assert.That(vigente.Configuration, Is.Null);
            Assert.That(vigente.Problems.Single(), Does.Contain("Nenhuma versão de parâmetros vigente"));
        });
    }

    [Test]
    public void ParametrosApontandoRegimeInexistenteSaoProblema()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros(regime: "Regime que não existe")));

        var vigente = Store().ResolveAt(new DateOnly(2026, 9, 24));

        Assert.That(vigente.Problems.Single(), Does.Contain("Regime que não existe"));
    }

    // ----------------------------------------------------------------------------------
    // Arquivos ruins — o que um agente automático poderia produzir
    // ----------------------------------------------------------------------------------

    [Test]
    public void PropriedadeComErroDeDigitacaoRejeitaOArquivoInteiro()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros()));
        Write("agente.json", """{ "tributos": [ { "regime": "X", "vigenteDesde": "2026-10-01", "faixasImpostoImportacao": [ { "aliquota": 0.6 } ], "icsm": 0.18, "icms": 0.18, "fonte": "f" } ] }""");

        var carregada = Store().Load();
        var agente = carregada.Files.Single(f => f.FileName == "agente.json");

        Assert.Multiple(() =>
        {
            Assert.That(agente.Accepted, Is.False);
            Assert.That(agente.Errors.Single(), Does.Contain("icsm"));
            Assert.That(carregada.TaxRules.Select(t => t.Entry.Regime), Does.Not.Contain("X"), "nada do arquivo ruim entra");
            Assert.That(carregada.Files.Single(f => f.FileName == "configuracoes.json").Accepted, Is.True, "os bons continuam valendo");
        });
    }

    [Test]
    public void TributoSemFonteERejeitado()
    {
        Write("a.json", """{ "tributos": [ { "regime": "X", "vigenteDesde": "2026-10-01", "faixasImpostoImportacao": [ { "aliquota": 0.6 } ], "icms": 0.18 } ] }""");
        Write("b.json", """{ "tributos": [ { "regime": "Y", "vigenteDesde": "2026-10-01", "faixasImpostoImportacao": [ { "aliquota": 0.6 } ], "icms": 0.18, "fonte": "  " } ] }""");

        var arquivos = Store().Load().Files;

        Assert.Multiple(() =>
        {
            Assert.That(arquivos.Single(f => f.FileName == "a.json").Errors.Single(), Does.Contain("fonte"), "campo ausente");
            Assert.That(arquivos.Single(f => f.FileName == "b.json").Errors.Single(), Does.Contain("'fonte' é obrigatória"), "campo em branco");
        });
    }

    [Test]
    public void FaixasForaDeOrdemSaoRejeitadasPelaRegraDoDominio()
    {
        Write("x.json", """
            { "tributos": [ { "regime": "X", "vigenteDesde": "2026-10-01", "icms": 0.18, "fonte": "f",
              "faixasImpostoImportacao": [ { "ateUsd": 3000, "aliquota": 0.6 }, { "ateUsd": 50, "aliquota": 0 } ] } ] }
            """);

        var arquivo = Store().Load().Files.Single();

        Assert.Multiple(() =>
        {
            Assert.That(arquivo.Accepted, Is.False);
            Assert.That(arquivo.Errors.Single(), Does.Contain("crescentes"));
        });
    }

    [Test]
    public void AliquotaEmPercentualEmVezDeFracaoERejeitada()
    {
        // O erro mais provável de um agente: escrever 18 querendo dizer 18%.
        Write("x.json", """{ "tributos": [ { "regime": "X", "vigenteDesde": "2026-10-01", "faixasImpostoImportacao": [ { "aliquota": 0.6 } ], "icms": 18, "fonte": "f" } ] }""");

        Assert.That(Store().Load().Files.Single().Accepted, Is.False);
    }

    [Test]
    public void VersaoDuplicadaEntreArquivosFicaComADoAdministrador()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros()));
        Write("agente.json", $$"""{ "tributos": [ {{Tributo2026.Replace("0.20", "0.17")}} ] }""");

        var carregada = Store().Load();

        Assert.Multiple(() =>
        {
            Assert.That(carregada.TaxRules, Has.Count.EqualTo(1));
            Assert.That(carregada.TaxRules.Single().Entry.Icms, Is.EqualTo(0.20m));
            Assert.That(carregada.Errors.Single(), Does.Contain("agente.json").And.Contain("já existe em configuracoes.json"));
        });
    }

    [Test]
    public void ArquivoNovoDeAgenteComVersaoFuturaEntraSemApagarAHistoria()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros()));
        Write("agente-2027.json", """
            { "tributos": [ { "regime": "RC-PF", "vigenteDesde": "2027-01-01", "icms": 0.18, "fonte": "lei hipotética",
              "faixasImpostoImportacao": [ { "ateUsd": 3000, "aliquota": 0.50, "deducaoUsd": 25 } ] } ] }
            """);

        var store = Store();
        var hoje = store.ResolveAt(new DateOnly(2026, 9, 24)).Configuration!;
        var depois = store.ResolveAt(new DateOnly(2027, 2, 1)).Configuration!;

        Assert.Multiple(() =>
        {
            Assert.That(hoje.TaxRule.Entry.Icms, Is.EqualTo(0.20m), "o que valia hoje continua valendo hoje");
            Assert.That(depois.TaxRule.Entry.Icms, Is.EqualTo(0.18m));
            Assert.That(depois.TaxRule.File, Is.EqualTo("agente-2027.json"));
        });
    }

    // ----------------------------------------------------------------------------------
    // Escrita pela área do administrador
    // ----------------------------------------------------------------------------------

    [Test]
    public async Task NovaVersaoEAcrescentadaNuncaSobrescrita()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros()));
        var store = Store();

        var nova = new TaxRuleEntry
        {
            Regime = "RC-PF",
            ValidFrom = new DateOnly(2026, 10, 1),
            ImportDutyBrackets = [new BracketEntry { UpToForeign = 3000m, Rate = 0.60m, DeductionForeign = 30m }],
            Icms = 0.18m,
            Source = "ajuste para SP"
        };

        var resultado = await store.AppendTaxRuleAsync(nova, CancellationToken.None);
        var carregada = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Saved, Is.True, string.Join(" | ", resultado.Errors));
            Assert.That(carregada.TaxRules, Has.Count.EqualTo(2), "a versão anterior continua lá");
            Assert.That(store.ResolveAt(new DateOnly(2026, 9, 30), carregada).Configuration!.TaxRule.Entry.Icms, Is.EqualTo(0.20m));
            Assert.That(store.ResolveAt(new DateOnly(2026, 10, 1), carregada).Configuration!.TaxRule.Entry.Icms, Is.EqualTo(0.18m));
        });
    }

    [Test]
    public async Task VersaoInvalidaNaoChegaAoArquivo()
    {
        Write("configuracoes.json", Documento(Tributo2026, Parametros()));
        var antes = File.ReadAllText(Path.Combine(folder, "configuracoes.json"));

        var invalida = new TaxRuleEntry { Regime = "X", ValidFrom = new DateOnly(2026, 10, 1), ImportDutyBrackets = [new BracketEntry { Rate = 0.6m }], Icms = 1.5m, Source = "f" };
        var resultado = await Store().AppendTaxRuleAsync(invalida, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Saved, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(folder, "configuracoes.json")), Is.EqualTo(antes));
        });
    }

    [Test]
    public async Task UploadInvalidoNaoChegaAPasta()
    {
        var resultado = await Store().SaveFileAsync("novo.json", """{ "tributos": [ { "regime": "X" } ] }""", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Saved, Is.False);
            Assert.That(File.Exists(Path.Combine(folder, "novo.json")), Is.False);
        });
    }

    [TestCase("../fora.json")]
    [TestCase("C:\\Windows\\x.json")]
    [TestCase("config.txt")]
    [TestCase("configuracoes.schema.json")]
    public async Task NomeDeArquivoPerigosoERecusado(string nome)
    {
        var resultado = await Store().SaveFileAsync(nome, Documento(Tributo2026, Parametros()), CancellationToken.None);

        Assert.That(resultado.Saved, Is.False);
    }

    [Test]
    public async Task UploadValidoEntraELido()
    {
        var resultado = await Store().SaveFileAsync("tributos-out.json", Documento(Tributo2026, Parametros()), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Saved, Is.True, string.Join(" | ", resultado.Errors));
            Assert.That(Store().ResolveAt(new DateOnly(2026, 9, 24)).Configuration, Is.Not.Null);
        });
    }
}
