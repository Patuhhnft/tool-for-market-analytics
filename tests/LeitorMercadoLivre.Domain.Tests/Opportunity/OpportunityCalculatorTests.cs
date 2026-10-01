using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Domain.Opportunity;
using LeitorMercadoLivre.Domain.Pricing;
using LeitorMercadoLivre.Domain.Suppliers;

namespace LeitorMercadoLivre.Domain.Tests.Opportunity;

[TestFixture]
public class OpportunityCalculatorTests
{
    private static readonly OpportunityOptions Opcoes = new();

    /// <summary>Tudo no melhor valor possível; cada teste estraga só o que interessa.</summary>
    private static OpportunityInput Ideal() => new(
        DemandScore: 1m, Persistence: 1m, DemandFalling: false,
        Sellers: 10, NewSellersThisWeek: 0,
        WeeklyPriceChange: 0m, DispersionIndex: 0m, WeeklyModeStrengthChange: 0m,
        WorstMargin: 0.30m);

    [Test]
    public void TodosOsFatoresNoMaximoDaoUm()
    {
        Assert.That(OpportunityCalculator.Calculate(Ideal(), Opcoes).Index, Is.EqualTo(1m));
    }

    [Test]
    public void MediaGeometricaNaoEsmagaOIndice()
    {
        // Fatores 0,5 · 1 · 1 · 0,5 · 1 → produto 0,25 → raiz quinta 0,7579.
        // O produto puro daria 0,25 — dois fatores medianos derrubariam o índice a um quarto.
        var entrada = Ideal() with { DemandScore = 0.5m, Persistence = 0.5m };

        var resultado = OpportunityCalculator.Calculate(entrada, Opcoes);

        Assert.That(resultado.Index, Is.EqualTo(0.7579m).Within(0.0001m));
    }

    [Test]
    public void MargemNegativaZeraOIndiceDePropósito()
    {
        var resultado = OpportunityCalculator.Calculate(Ideal() with { WorstMargin = -0.05m }, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.MarginFactor, Is.Zero);
            Assert.That(resultado.Index, Is.Zero);
        });
    }

    [Test]
    public void PressaoAcimaDeUmNaoInverteOSinal()
    {
        // Queda de 200%, dispersão 5, força da moda caindo 300%: sem clamp, a pressão
        // passaria de 1 e (1 − pressão) ficaria negativo.
        var colapso = Ideal() with { WeeklyPriceChange = -2m, DispersionIndex = 5m, WeeklyModeStrengthChange = -3m };

        var resultado = OpportunityCalculator.Calculate(colapso, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.PricePressure, Is.EqualTo(1m));
            Assert.That(resultado.Index, Is.Zero);
            Assert.That(resultado.Index, Is.Not.Negative);
        });
    }

    [Test]
    public void SemFornecedorOIndiceSaiSemMargemEMarcado()
    {
        // Quatro fatores: 0,5 · 1 · 1 · 0,5 → 0,25 → raiz quarta 0,7071.
        var semFornecedor = Ideal() with { DemandScore = 0.5m, Persistence = 0.5m, WorstMargin = null };

        var resultado = OpportunityCalculator.Calculate(semFornecedor, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.IncludesMargin, Is.False);
            Assert.That(resultado.Index, Is.EqualTo(0.7071m).Within(0.0001m));
        });
    }

    [Test]
    public void EntrantesDaSemanaReduzemOFatorDeOferta()
    {
        // 12 vendedores, 3 novos: base 9, crescimento 3 ÷ 9 = 1/3, fator 1 ÷ (4/3) = 0,75.
        var resultado = OpportunityCalculator.Calculate(Ideal() with { Sellers = 12, NewSellersThisWeek = 3 }, Opcoes);

        Assert.That(resultado.SupplyFactor, Is.EqualTo(0.75m).Within(0.0000001m));
    }

    [Test]
    public void TodoFatorFicaEmZeroAUm()
    {
        var absurda = new OpportunityInput(7m, -3m, false, 5, 50, 4m, -9m, 8m, 12m);

        var resultado = OpportunityCalculator.Calculate(absurda, Opcoes);

        Assert.Multiple(() =>
        {
            foreach (var fator in new[] { resultado.DemandFactor, resultado.SupplyFactor, resultado.PricePressure, resultado.PersistenceFactor, resultado.MarginFactor!.Value, resultado.Index })
            {
                Assert.That(fator, Is.InRange(0m, 1m));
            }
        });
    }

    [TestCase(true, 0.9, 0, Quadrant.EndOfCycle)]
    [TestCase(false, 0.1, 0, Quadrant.Saturating)]
    [TestCase(false, 0.6, 0, Quadrant.OpenWindow)]
    [TestCase(false, 0.6, 5, Quadrant.Race)]
    public void QuadranteSegueOCicloDeVida(bool caindo, decimal demanda, int novos, Quadrant esperado)
    {
        var entrada = Ideal() with { DemandFalling = caindo, DemandScore = demanda, NewSellersThisWeek = novos };

        Assert.That(OpportunityCalculator.Calculate(entrada, Opcoes).Quadrant, Is.EqualTo(esperado));
    }

    [Test]
    public void PesosQueNaoSomamUmSaoRecusados()
    {
        var torta = new OpportunityOptions { PriceDropWeight = 0.5m };

        Assert.Throws<ArgumentException>(() => OpportunityCalculator.Calculate(Ideal(), torta));
    }
}

[TestFixture]
public class WhySellingNowTests
{
    private static PriceReferenceResult Preco(PriceStatus status = PriceStatus.Calculated) => new()
    {
        Status = status,
        SampleSize = 20,
        SellersFound = 22,
        Discarded = [],
        SampleHash = "x",
        Source = PriceSource.Mode,
        MarketPrice = 90m,
        ModeStrength = 0.34m,
        ModalBucketSellers = 7
    };

    /// <summary>Semana anterior de 5.000 visitas: base grande, percentual válido.</summary>
    private static DemandResult Visitas(int delta, decimal taxa) => new()
    {
        Source = DemandSource.Visits,
        Score = 0.6m,
        Persistence = 0.25m,
        AbsoluteDelta = delta,
        Rate = taxa,
        PreviousWeekVisits = 5000,
        LastWeekVisits = 5000 + delta
    };

    /// <summary>
    /// O caso real de 25/09/2026 (MLB78092304): nove dias em zero, estreia em 19/09 e 19.989
    /// visitas em sete dias. Era ele que imprimia "+1.998.900%" no card.
    /// </summary>
    private static DemandResult Estreia() => new()
    {
        Source = DemandSource.Visits,
        Score = 0.97m,
        Persistence = 0.25m,
        AbsoluteDelta = 19989,
        // A proteção contra divisão por zero do calculador: com base 0, a taxa vira o próprio
        // delta. É exatamente este número que não pode mais chegar à tela.
        Rate = 19989m,
        PreviousWeekVisits = 0,
        LastWeekVisits = 19989,
        Series =
        [
            new DailyCount(new DateOnly(2026, 9, 17), 0),
            new DailyCount(new DateOnly(2026, 9, 18), 0),
            new DailyCount(new DateOnly(2026, 9, 19), 18),
            new DailyCount(new DateOnly(2026, 9, 20), 3751),
            new DailyCount(new DateOnly(2026, 9, 21), 4469)
        ]
    };

    /// <summary>De 2 para 20 visitas: +900% e não é um mercado (I11).</summary>
    private static DemandResult BasePequena() => new()
    {
        Source = DemandSource.Visits,
        Score = 0.1m,
        Persistence = 0.25m,
        AbsoluteDelta = 18,
        Rate = 9m,
        PreviousWeekVisits = 2,
        LastWeekVisits = 20
    };

    /// <summary>
    /// O veredito sai da mesma função que a produção usa — o teste não inventa o seu próprio.
    /// </summary>
    private static VisitGrowth Crescimento(DemandResult demanda) => VisitGrowth.From(demanda, new DemandOptions());

    private static string Frase(DemandResult demanda, PriceReferenceResult? preco = null, int vendedores = 22, int? entrantes = 3) =>
        WhySellingNow.Explain(demanda, preco ?? Preco(), vendedores, entrantes, Crescimento(demanda));

    /// <summary>
    /// Só o trecho de demanda. A frase inteira tem três partes, e a de preço traz um percentual
    /// legítimo ("34% dos vendedores") que não tem nada a ver com crescimento.
    /// </summary>
    private static string TrechoDeDemanda(DemandResult demanda, int vendedores = 22, int? entrantes = 3) =>
        Frase(demanda, vendedores: vendedores, entrantes: entrantes).Split(" · ")[0];

    [Test]
    public void FraseCompletaComNumeroAbsolutoAoLadoDoPercentual()
    {
        var texto = Frase(Visitas(3000, 0.6m));

        Assert.That(texto, Is.EqualTo(
            "recebeu 3.000 visitas a mais que na semana anterior (+60%) · " +
            "22 vendedores, 3 entrantes desde a coleta anterior · " +
            "34% dos vendedores (7) praticam R$ 90"));
    }

    [Test]
    public void SemColetaAnteriorOCardDizQueNaoHaComparacao()
    {
        // Nulo não é zero: dizer "nenhum entrante" seria afirmar o que não foi medido (I6).
        var texto = Frase(Visitas(3000, 0.6m), entrantes: null);

        Assert.That(texto, Does.Contain("entrantes ainda sem comparação"));
    }

    [Test]
    public void NuncaDizQueVendeu()
    {
        // §3: o que se mede é visita e posição. "Vendeu" seria afirmar o que não se mede.
        var textos = new[]
        {
            Frase(Visitas(3000, 0.6m)),
            Frase(Visitas(-400, -0.4m), vendedores: 5, entrantes: 0),
            Frase(DemandCalculator.FromRanking(7, 12, new DemandOptions()), vendedores: 9, entrantes: 1),
            Frase(Estreia()),
            Frase(BasePequena())
        };

        Assert.That(textos, Has.None.Contains("vendeu"));
    }

    [Test]
    public void ProxyPorPosicaoSeApresentaComoPosicao()
    {
        var demanda = DemandCalculator.FromRanking(7, 12, new DemandOptions());

        var texto = Frase(demanda, vendedores: 9, entrantes: 1);

        Assert.That(texto, Does.StartWith("subiu 5 posições no ranking de mais vendidos (agora 7º)"));
    }

    [Test]
    public void EstreiaDizADataEOVolume_NuncaUmPercentual()
    {
        // O percentual não existe quando a base é zero: não há razão a calcular. E a data em que
        // a curva saiu do zero informa mais do que qualquer número — diz que ANTES não existia.
        var trecho = TrechoDeDemanda(Estreia(), vendedores: 6, entrantes: 0);

        Assert.Multiple(() =>
        {
            Assert.That(trecho, Is.EqualTo("estreou em 19/09 e fez 19.989 visitas em 7 dias"));
            Assert.That(trecho, Does.Not.Contain("%"), "base zero não produz percentual nenhum");
            Assert.That(trecho, Does.Not.Contain("1.998.900"), "o número da divisão por zero não chega à tela");
        });
    }

    [Test]
    public void EstreiaSemSerieAindaEvitaOPercentual()
    {
        // Sem série não dá para dizer a data — mas continua não havendo percentual a mostrar.
        var semSerie = Estreia() with { Series = [] };

        var trecho = TrechoDeDemanda(semSerie, vendedores: 6, entrantes: 0);

        Assert.Multiple(() =>
        {
            Assert.That(trecho, Is.EqualTo("demanda nova: 19.989 visitas na semana, sem semana anterior para comparar"));
            Assert.That(trecho, Does.Not.Contain("%"));
        });
    }

    [Test]
    public void BasePequenaMostraOAbsolutoEDizPorQue()
    {
        // De 2 para 20 visitas são +900%. O card mostra as 18 visitas e explica a ausência.
        var trecho = TrechoDeDemanda(BasePequena(), vendedores: 3, entrantes: 0);

        Assert.Multiple(() =>
        {
            Assert.That(trecho, Is.EqualTo("recebeu 18 visitas a mais que na semana anterior (base pequena demais para percentual)"));
            Assert.That(trecho, Does.Not.Contain("900%"));
        });
    }

    [Test]
    public void BaseGrandeContinuaMostrandoOPercentual()
    {
        // A correção não pode ter apagado o caso normal, que é a maioria.
        Assert.That(Frase(Visitas(3000, 0.6m)), Does.Contain("(+60%)"));
    }

    [Test]
    public void SemPrecoCalculadoNaoCitaPreco()
    {
        var texto = Frase(Visitas(3000, 0.6m), Preco(PriceStatus.InsufficientData), vendedores: 2, entrantes: 0);

        Assert.That(texto, Does.Not.Contain("R$"));
    }
}

[TestFixture]
public class WhatsAppLinkTests
{
    [TestCase("+86 138 1234 5678", "https://wa.me/8613812345678")]
    [TestCase("0086 13812345678", "https://wa.me/8613812345678")]
    [TestCase("+55 11 98765-4321", "https://wa.me/5511987654321")]
    [TestCase("(11) 98765-4321", "https://wa.me/5511987654321")]
    [TestCase("11 3456-7890", "https://wa.me/551134567890")]
    public void MontaOLinkRespeitandoOPais(string telefone, string esperado)
    {
        Assert.That(WhatsAppLink.From(telefone), Is.EqualTo(esperado));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("12345")]
    [TestCase("138 1234 5678 9")]
    public void NumeroAmbiguoOuInvalidoNaoViraLink(string? telefone)
    {
        // Um número chinês sem +86 não pode virar 55 + número: seria um link para ninguém.
        Assert.That(WhatsAppLink.From(telefone), Is.Null);
    }
}
