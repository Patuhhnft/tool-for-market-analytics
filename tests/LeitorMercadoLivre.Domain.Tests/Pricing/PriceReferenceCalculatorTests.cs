using LeitorMercadoLivre.Domain.Pricing;

namespace LeitorMercadoLivre.Domain.Tests.Pricing;

/// <summary>
/// Cobertura do §11 do brief. Cada número esperado aqui foi conferido à mão — é isso que
/// dá a esta suíte o direito de reprovar o código em vez de ser reescrita para passar.
/// </summary>
[TestFixture]
public class PriceReferenceCalculatorTests
{
    private const string Anchor = "MLB54982411";

    private static SellerListing L(
        string seller,
        decimal price,
        int units = 1,
        string? anchor = Anchor,
        string? listingId = null) =>
        new(seller, listingId ?? $"{seller}#{price}", price, anchor, units);

    /// <summary>Um vendedor distinto por preço informado.</summary>
    private static List<SellerListing> Sample(params decimal[] prices) =>
        [.. prices.Select((price, index) => L($"S{index:00}", price))];

    private static PriceReferenceResult Run(
        IEnumerable<SellerListing> listings,
        PriceReferenceOptions? options = null,
        PriceReferenceHistory? previous = null) =>
        PriceReferenceCalculator.Calculate(Anchor, [.. listings], options ?? new PriceReferenceOptions(), previous);

    // ----------------------------------------------------------------------------------
    // Etapa 1 — arredondamento para o balde mais próximo
    // ----------------------------------------------------------------------------------

    [TestCase(19.90, 20)]
    [TestCase(19.99, 20)]
    [TestCase(20.00, 20)]
    [TestCase(20.49, 20)]
    [TestCase(20.50, 21)]  // empate vai para cima, nunca para par
    [TestCase(23.40, 23)]  // o caso que o arredondamento "sempre para cima" inflava
    [TestCase(199.50, 200)]
    public void ArredondaParaOInteiroMaisProximo(decimal preco, decimal baldeEsperado)
    {
        var resultado = Run(Sample(preco, preco, preco));

        Assert.That(resultado.MarketPrice, Is.EqualTo(baldeEsperado));
    }

    [TestCase(347, 345)]
    [TestCase(349, 350)]
    [TestCase(202, 200)]
    public void AcimaDoLimiteArredondaParaOMultiploDeCincoMaisProximo(decimal preco, decimal baldeEsperado)
    {
        var resultado = Run(Sample(preco, preco, preco));

        Assert.That(resultado.MarketPrice, Is.EqualTo(baldeEsperado));
    }

    [Test]
    public void CentavosVizinhosCaemNoMesmoBalde()
    {
        // 19,90 / 19,99 / 20,00 são o mesmo preço para quem compra. Truncar os separaria.
        var resultado = Run(Sample(19.90m, 19.99m, 20.00m, 20.49m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.MarketPrice, Is.EqualTo(20m));
            Assert.That(resultado.ModalBucketSellers, Is.EqualTo(4));
            Assert.That(resultado.ModeStrength, Is.EqualTo(1m));
        });
    }

    // ----------------------------------------------------------------------------------
    // Etapa 2 — corte dos extremos
    // ----------------------------------------------------------------------------------

    [TestCase(7, 5)]    // floor(0,15 × 7)  = 1 por ponta
    [TestCase(10, 8)]   // floor(0,15 × 10) = 1 por ponta
    [TestCase(20, 14)]  // floor(0,15 × 20) = 3 por ponta
    public void CortaQuinzePorCentoDeCadaPonta(int vendedores, int amostraLimpaEsperada)
    {
        var precos = Enumerable.Range(1, vendedores).Select(valor => (decimal)valor * 10).ToArray();

        var resultado = Run(Sample(precos));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.SellersFound, Is.EqualTo(vendedores));
            Assert.That(resultado.SampleSize, Is.EqualTo(amostraLimpaEsperada));
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void AmostraPequenaNaoCortaEAvisa(int vendedores)
    {
        var precos = Enumerable.Range(1, vendedores).Select(valor => (decimal)valor * 10).ToArray();

        var resultado = Run(Sample(precos));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.SampleSize, Is.EqualTo(vendedores), "sem corte");
            Assert.That(resultado.HasFlag(PriceFlags.SmallSample), Is.True);
            Assert.That(resultado.Status, Is.EqualTo(PriceStatus.Calculated));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MenosDeTresVendedoresNaoProduzPreco(int vendedores)
    {
        var precos = Enumerable.Range(1, vendedores).Select(valor => (decimal)valor * 10).ToArray();

        var resultado = Run(Sample(precos));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Status, Is.EqualTo(PriceStatus.InsufficientData));
            Assert.That(resultado.MarketPrice, Is.Null);
            Assert.That(resultado.Median, Is.Null);
        });
    }

    // ----------------------------------------------------------------------------------
    // Etapa 0 — normalização da amostra
    // ----------------------------------------------------------------------------------

    [Test]
    public void VendedorComVariosAnunciosEntraSoComOMaisBarato()
    {
        // A regra que impede um vendedor sozinho de fabricar uma moda.
        var anuncios = new List<SellerListing>
        {
            L("A", 30m), L("A", 25m), L("A", 40m),
            L("B", 25m),
            L("C", 25m)
        };

        var resultado = Run(anuncios);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.SellersFound, Is.EqualTo(3), "três vendedores, não cinco anúncios");
            Assert.That(resultado.MarketPrice, Is.EqualTo(25m));
            Assert.That(
                resultado.Discarded.Count(d => d.Reason == DiscardReason.DuplicateSeller),
                Is.EqualTo(2));
        });
    }

    [Test]
    public void KitEntraPeloPrecoPorUnidade()
    {
        var anuncios = new List<SellerListing> { L("A", 60m, units: 3), L("B", 20m), L("C", 20m) };

        var resultado = Run(anuncios);

        Assert.That(resultado.MarketPrice, Is.EqualTo(20m));
    }

    [TestCase(false, true, true, DiscardReason.NotNew)]
    [TestCase(true, false, true, DiscardReason.Inactive)]
    [TestCase(true, true, false, DiscardReason.NoStock)]
    public void DescartaAnuncioForaDaNormalizacao(bool novo, bool ativo, bool estoque, DiscardReason motivo)
    {
        var suspeito = new SellerListing("X", "X#1", 20m, Anchor, 1, novo, ativo, estoque);
        var anuncios = new List<SellerListing> { suspeito, L("A", 20m), L("B", 20m), L("C", 20m) };

        var resultado = Run(anuncios);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.SellersFound, Is.EqualTo(3));
            Assert.That(resultado.Discarded.Single(d => d.SellerId == "X").Reason, Is.EqualTo(motivo));
        });
    }

    [Test]
    public void AnuncioDeOutroProdutoNaoContamina()
    {
        var anuncios = new List<SellerListing>
        {
            L("A", 20m), L("B", 20m), L("C", 20m),
            L("X", 999m, anchor: "MLB00000000")
        };

        var resultado = Run(anuncios);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.SellersFound, Is.EqualTo(3));
            Assert.That(resultado.Discarded.Single().Reason, Is.EqualTo(DiscardReason.DifferentProduct));
            Assert.That(resultado.MarketPrice, Is.EqualTo(20m));
        });
    }

    [Test]
    public void SemAncoraDeProdutoNaoCalculaPreco()
    {
        // I5: amostra sem âncora de catálogo é um monte de anúncio parecido, não um produto.
        var resultado = PriceReferenceCalculator.Calculate(
            null,
            [.. Sample(20m, 20m, 20m, 20m)],
            new PriceReferenceOptions());

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Status, Is.EqualTo(PriceStatus.NoProductAnchor));
            Assert.That(resultado.MarketPrice, Is.Null);
        });
    }

    // ----------------------------------------------------------------------------------
    // Etapa 3 — moda validada pela mediana
    // ----------------------------------------------------------------------------------

    [Test]
    public void ModaForteECentralViraOPrecoDeMercado()
    {
        // 10 vendedores, corta 1 de cada ponta → limpa: 19,20,20,20,20,21,22,23
        // mediana (par, central mais baixo) = 20 · balde modal 20 com 4 de 8 = 50%
        var resultado = Run(Sample(18m, 19m, 20m, 20m, 20m, 20m, 21m, 22m, 23m, 24m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Source, Is.EqualTo(PriceSource.Mode));
            Assert.That(resultado.MarketPrice, Is.EqualTo(20m));
            Assert.That(resultado.ModalBucketSellers, Is.EqualTo(4));
            Assert.That(resultado.ModeStrength, Is.EqualTo(0.5m));
            Assert.That(resultado.HasFlag(PriceFlags.DispersedMarket), Is.False);
        });
    }

    [Test]
    public void ModaFracaCaiParaAMediana()
    {
        // Mesma amostra, só a exigência de força muda: 37,5% não alcança os 50% pedidos.
        var amostra = Sample(5m, 10m, 10m, 10m, 20m, 30m, 40m, 50m, 60m, 70m);
        var exigente = new PriceReferenceOptions { MinModeStrength = 0.5m };

        var comPadrao = Run(amostra);
        var comExigencia = Run(amostra, exigente);

        Assert.Multiple(() =>
        {
            Assert.That(comPadrao.Source, Is.EqualTo(PriceSource.Mode));
            Assert.That(comPadrao.MarketPrice, Is.EqualTo(10m), "moda");

            Assert.That(comExigencia.Source, Is.EqualTo(PriceSource.Median));
            Assert.That(comExigencia.MarketPrice, Is.EqualTo(20m), "mediana");
            Assert.That(comExigencia.HasFlag(PriceFlags.DispersedMarket), Is.True);
        });
    }

    [Test]
    public void DoisVendedoresNoBaldeModalNaoSaoModa()
    {
        // 7 vendedores, corta 1 de cada ponta → limpa: 20,20,30,40,50
        // balde 20 tem 2 de 5 = 40%, passa nos 20% — mas 2 é menos que o piso absoluto de 3.
        var resultado = Run(Sample(10m, 20m, 20m, 30m, 40m, 50m, 60m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.ModeStrength, Is.EqualTo(0.4m), "passa no percentual");
            Assert.That(resultado.ModalBucketSellers, Is.EqualTo(2), "mas são duas pessoas");
            Assert.That(resultado.Source, Is.EqualTo(PriceSource.Median));
            Assert.That(resultado.MarketPrice, Is.EqualTo(30m));
        });
    }

    [Test]
    public void EmpateEntreBaldesEscolheOMaisProximoDaMediana()
    {
        // 9 vendedores, corta 1 de cada ponta → limpa: 10,10,10,20,40,40,40
        // mediana = 20 · baldes 10 e 40 empatam em 3 · |10−20| = 10 < |40−20| = 20
        var resultado = Run(Sample(5m, 10m, 10m, 10m, 20m, 40m, 40m, 40m, 90m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Median, Is.EqualTo(20m));
            Assert.That(resultado.MarketPrice, Is.EqualTo(10m));
        });
    }

    [Test]
    public void EmpateEquidistanteEscolheOMaisBaixo()
    {
        // limpa: 10,10,10,20,30,30,30 · mediana = 20 · |10−20| = |30−20| → o mais baixo,
        // que é a escolha conservadora: protege a margem.
        var resultado = Run(Sample(5m, 10m, 10m, 10m, 20m, 30m, 30m, 30m, 90m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Median, Is.EqualTo(20m));
            Assert.That(resultado.MarketPrice, Is.EqualTo(10m));
        });
    }

    [Test]
    public void TodosNoMesmoPrecoProduzemDispersaoZero()
    {
        var resultado = Run(Sample(Enumerable.Repeat(50m, 10).ToArray()));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.MarketPrice, Is.EqualTo(50m));
            Assert.That(resultado.ModeStrength, Is.EqualTo(1m));
            Assert.That(resultado.DispersionIndex, Is.EqualTo(0m));
            Assert.That(resultado.SuggestedShowcasePrice, Is.EqualTo(49.90m));
        });
    }

    [Test]
    public void ModaNuncaCaiForaDaFaixaLimpa()
    {
        // A condição (c) do §5 é verdadeira por construção: a moda é contada NA lista limpa.
        // O teste existe para travar a ordem das etapas — se alguém passar a contar a moda
        // antes do corte, este teste quebra e a regra volta a ter função.
        var resultado = Run(Sample(1m, 2m, 20m, 20m, 20m, 21m, 22m, 23m, 500m, 900m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Mode, Is.GreaterThanOrEqualTo(resultado.CleanRangeLow!.Value));
            Assert.That(resultado.Mode, Is.LessThanOrEqualTo(resultado.CleanRangeHigh!.Value));
        });
    }

    // ----------------------------------------------------------------------------------
    // Golden test — vetor fixo, tudo conferido à mão
    // ----------------------------------------------------------------------------------

    [Test]
    public void GoldenTest_VetorFixoComTodasAsSaidasConferidas()
    {
        // brutos:  89,90 · 90,00 · 90,00 · 90,49 · 95,00 · 99,00 · 105,00 · 78,00 · 120,00 · 85,50
        // baldes:  90 · 90 · 90 · 90 · 95 · 99 · 105 · 78 · 120 · 86
        // ordenados: 78, 86, 90, 90, 90, 90, 95, 99, 105, 120
        // corte de 1 por ponta (floor(0,15 × 10) = 1) → limpa: 86, 90, 90, 90, 90, 95, 99, 105
        // mediana (8 itens, central mais baixo, índice 3) = 90
        // balde modal 90 com 4 de 8 = 50% → usa a moda
        // dispersão = (105 − 86) / 90 = 0,2111...
        var resultado = Run(Sample(89.90m, 90.00m, 90.00m, 90.49m, 95.00m, 99.00m, 105.00m, 78.00m, 120.00m, 85.50m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Status, Is.EqualTo(PriceStatus.Calculated));
            Assert.That(resultado.SellersFound, Is.EqualTo(10));
            Assert.That(resultado.SampleSize, Is.EqualTo(8));
            Assert.That(resultado.Source, Is.EqualTo(PriceSource.Mode));
            Assert.That(resultado.MarketPrice, Is.EqualTo(90m));
            Assert.That(resultado.Mode, Is.EqualTo(90m));
            Assert.That(resultado.Median, Is.EqualTo(90m));
            Assert.That(resultado.ModalBucketSellers, Is.EqualTo(4));
            Assert.That(resultado.ModeStrength, Is.EqualTo(0.5m));
            Assert.That(resultado.CleanRangeLow, Is.EqualTo(86m));
            Assert.That(resultado.CleanRangeHigh, Is.EqualTo(105m));
            Assert.That(resultado.SuggestedShowcasePrice, Is.EqualTo(89.90m));
            Assert.That(Math.Round(resultado.DispersionIndex!.Value, 4), Is.EqualTo(0.2111m));
            Assert.That(resultado.Confidence, Is.EqualTo(PriceConfidence.Medium));
            Assert.That(resultado.Flags, Is.EqualTo(PriceFlags.None));
        });
    }

    // ----------------------------------------------------------------------------------
    // Confiança, variação semanal e auditoria
    // ----------------------------------------------------------------------------------

    [Test]
    public void AmostraCurtaRebaixaAConfianca()
    {
        var resultado = Run(Sample(20m, 20m, 20m, 21m));

        Assert.That(resultado.Confidence, Is.EqualTo(PriceConfidence.Low));
    }

    [Test]
    public void MercadoEspalhadoRebaixaAConfianca()
    {
        // limpa vai de 20 a 400 sobre mediana 50 → dispersão muito acima de 0,50
        var resultado = Run(Sample(10m, 20m, 30m, 40m, 50m, 60m, 200m, 400m, 500m, 900m));

        Assert.That(resultado.Confidence, Is.EqualTo(PriceConfidence.Low));
    }

    [Test]
    public void VariacaoSemanalComparaComOCicloAnterior()
    {
        var anterior = new PriceReferenceHistory(MarketPrice: 100m, ModeStrength: 0.5m);

        var resultado = Run(Sample(90m, 90m, 90m, 90m), previous: anterior);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.MarketPrice, Is.EqualTo(90m));
            Assert.That(resultado.WeeklyMarketPriceChange, Is.EqualTo(-0.10m));
            Assert.That(resultado.WeeklyModeStrengthChange, Is.EqualTo(1m), "0,5 → 1,0 é +100%");
        });
    }

    [Test]
    public void SemCicloAnteriorNaoInventaVariacao()
    {
        var resultado = Run(Sample(90m, 90m, 90m, 90m));

        Assert.Multiple(() =>
        {
            Assert.That(resultado.WeeklyMarketPriceChange, Is.Null);
            Assert.That(resultado.WeeklyModeStrengthChange, Is.Null);
        });
    }

    [Test]
    public void HashDaAmostraEEstavelESensivel()
    {
        // I7: sem hash reproduzível não dá para auditar o número meses depois.
        var mesmaAmostra = Run(Sample(20m, 21m, 22m, 23m));
        var repetida = Run(Sample(20m, 21m, 22m, 23m));
        var alterada = Run(Sample(20m, 21m, 22m, 24m));

        Assert.Multiple(() =>
        {
            Assert.That(repetida.SampleHash, Is.EqualTo(mesmaAmostra.SampleHash));
            Assert.That(alterada.SampleHash, Is.Not.EqualTo(mesmaAmostra.SampleHash));
        });
    }

    [Test]
    public void OrdemDeEntradaNaoMudaOResultado()
    {
        // Os MESMOS pares vendedor-preço em outra ordem. Gerar a lista embaralhada com
        // Sample() não serviria: ele numera os vendedores pela posição, então trocaria quem
        // cobra o quê — e aí o hash, corretamente, acusaria amostras diferentes.
        var anuncios = Sample(18m, 19m, 20m, 20m, 20m, 20m, 21m, 22m, 23m, 24m);
        int[] permutacao = [7, 2, 9, 0, 4, 1, 8, 3, 6, 5];
        var embaralhados = permutacao.Select(indice => anuncios[indice]);

        var original = Run(anuncios);
        var reordenada = Run(embaralhados);

        Assert.Multiple(() =>
        {
            Assert.That(reordenada.MarketPrice, Is.EqualTo(original.MarketPrice));
            Assert.That(reordenada.SampleHash, Is.EqualTo(original.SampleHash));
        });
    }

    [Test]
    public void HashDistingueQuemCobraOQue()
    {
        // Mesmo multiconjunto de preços, vendedores trocados: o preço de mercado não muda,
        // mas a amostra é outra e o hash tem de acusar. É o que permite auditar depois.
        var umaAtribuicao = Run(new List<SellerListing> { L("A", 20m), L("B", 20m), L("C", 30m) });
        var outraAtribuicao = Run(new List<SellerListing> { L("A", 30m), L("B", 20m), L("C", 20m) });

        Assert.Multiple(() =>
        {
            Assert.That(outraAtribuicao.MarketPrice, Is.EqualTo(umaAtribuicao.MarketPrice));
            Assert.That(outraAtribuicao.SampleHash, Is.Not.EqualTo(umaAtribuicao.SampleHash));
        });
    }

    // ----------------------------------------------------------------------------------
    // Parâmetros
    // ----------------------------------------------------------------------------------

    [Test]
    public void ParametroAbsurdoDerrubaOCicloEmVezDeProduzirPrecoErrado()
    {
        var invalido = new PriceReferenceOptions { TrimRatio = 0.5m };

        Assert.Throws<ArgumentException>(() => Run(Sample(20m, 20m, 20m), invalido));
    }
}
