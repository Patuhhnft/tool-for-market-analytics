using LeitorMercadoLivre.Domain.Demand;

namespace LeitorMercadoLivre.Domain.Tests.Demand;

/// <summary>
/// O runway existe para responder uma pergunta de dinheiro: <b>dá tempo de importar isto?</b>
/// Comprar um viral que satura antes da caixa chegar é o erro mais caro do importador.
/// <para>
/// O que se testa aqui é sobretudo a HONESTIDADE do número: série curta não vira estimativa,
/// a faixa nunca vira ponto, e quem decide compra usa o limite inferior.
/// </para>
/// </summary>
[TestFixture]
public sealed class RunwayTests
{
    private static readonly RunwayOptions Opcoes = new();
    private static readonly DateOnly Inicio = new(2026, 9, 1);

    /// <summary>Monta uma série diária a partir dos valores, um por dia.</summary>
    private static List<DailyCount> Serie(params int[] visitas) =>
        [.. visitas.Select((valor, dia) => new DailyCount(Inicio.AddDays(dia), valor))];

    /// <summary>N dias iguais — curva plana.</summary>
    private static List<DailyCount> Plana(int dias, int porDia) => Serie([.. Enumerable.Repeat(porDia, dias)]);

    /// <summary>Três semanas com o total de cada uma distribuído por igual nos sete dias.</summary>
    private static List<DailyCount> TresSemanas(int primeira, int segunda, int terceira) =>
        Serie([.. Enumerable.Repeat(primeira / 7, 7), .. Enumerable.Repeat(segunda / 7, 7), .. Enumerable.Repeat(terceira / 7, 7)]);

    [Test]
    public void SerieCurtaNaoViraEstimativa()
    {
        // Com pouca série, o honesto é dizer "não sei" — e não sei NÃO é "não dá" (I6).
        var runway = RunwayCalculator.Estimate(Plana(5, 100), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Unknown));
            Assert.That(runway.LowerWeeks, Is.Null);
            Assert.That(runway.Reason, Does.Contain("5 dias"));
            Assert.That(runway.FitsLeadTime(30, 30), Is.Null, "sem estimativa, a resposta é null, nunca false");
        });
    }

    [Test]
    public void SerieVaziaNaoViraEstimativa()
    {
        var runway = RunwayCalculator.Estimate(Plana(21, 0), Opcoes);

        Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Unknown));
        Assert.That(runway.Reason, Does.Contain("sem visita"));
    }

    [Test]
    public void CurvaQueGanhaVelocidadeEAceleracao()
    {
        // 700 → 1.400 → 2.800: dobra a cada semana, o crescimento ACELERA.
        var runway = RunwayCalculator.Estimate(TresSemanas(700, 1400, 2800), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Accelerating));
            Assert.That(runway.LowerWeeks, Is.EqualTo(Opcoes.AcceleratingLower));
            Assert.That(runway.Reason, Does.Contain("ritmo se sustenta"));
        });
    }

    [Test]
    public void CurvaQueSobeMasPerdeVelocidadeEDesaceleracao()
    {
        // 700 → 2.100 (+200%) → 2.520 (+20%): ainda sobe, mas o pico ficou para trás.
        // É o caso perigoso: o card de hoje mostraria "crescendo", e a caixa chegaria tarde.
        var runway = RunwayCalculator.Estimate(TresSemanas(700, 2100, 2520), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Decelerating));
            Assert.That(runway.LowerWeeks, Is.EqualTo(Opcoes.DeceleratingLower));
            Assert.That(runway.Reason, Does.Contain("o ritmo caiu"));
        });
    }

    [Test]
    public void CurvaCaindoEQueda()
    {
        var runway = RunwayCalculator.Estimate(TresSemanas(2800, 1400, 700), Opcoes);

        Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Falling));
        Assert.That(runway.LowerWeeks, Is.EqualTo(Opcoes.FallingLower));
    }

    [Test]
    public void CurvaEstavelNaoViraNemSubidaNemQueda()
    {
        // Ruído de um dia não pode virar diagnóstico de fase.
        var runway = RunwayCalculator.Estimate(Plana(21, 100), Opcoes);

        Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Linear));
    }

    [Test]
    public void SubindoSemTresSemanasNaoViraEstimativa()
    {
        // 14 dias bastam para ver a DIREÇÃO, não a aceleração. E um produto que subiu 200%
        // pode estar explodindo ou já ter passado do pico — é essa diferença que decide
        // importar. Inventar uma fase aqui daria um piso de runway sem lastro.
        var subindo = Serie([.. Enumerable.Repeat(50, 7), .. Enumerable.Repeat(150, 7)]);

        var runway = RunwayCalculator.Estimate(subindo, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Unknown));
            Assert.That(runway.LowerWeeks, Is.Null);
            Assert.That(runway.Reason, Does.Contain("três semanas"));
            Assert.That(runway.FitsLeadTime(5, 30), Is.Null, "vira 'não sei', nunca 'não dá'");
        });
    }

    [Test]
    public void CurvaPlanaContinuaSendoClassificavelComDuasSemanas()
    {
        // Aqui não há dúvida: plana é plana, e isso se vê com 14 dias.
        var runway = RunwayCalculator.Estimate(Plana(14, 100), Opcoes);

        Assert.That(runway.Phase, Is.EqualTo(DemandPhase.Linear));
        Assert.That(runway.LowerWeeks, Is.EqualTo(Opcoes.LinearLower));
    }

    // ----------------------------------------------------------------------------------
    // O portão de lead time — onde o dinheiro é decidido
    // ----------------------------------------------------------------------------------

    [Test]
    public void ImportarSoPassaSeORunwayCobrePrazoMaisJanela()
    {
        // Acelerando: piso de 6 semanas = 42 dias.
        var runway = RunwayCalculator.Estimate(TresSemanas(700, 1400, 2800), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(runway.FitsLeadTime(leadTimeDays: 5, minimumSellingWindowDays: 30), Is.True, "revenda nacional cabe");
            Assert.That(runway.FitsLeadTime(leadTimeDays: 12, minimumSellingWindowDays: 30), Is.True, "aéreo rápido cabe");
            Assert.That(runway.FitsLeadTime(leadTimeDays: 60, minimumSellingWindowDays: 30), Is.False, "marítimo não dá tempo");
        });
    }

    [Test]
    public void ProdutoDesacelerandoNaoDaTempoDeImportar()
    {
        // O caso que o sistema de hoje deixaria passar: cresce 20% na semana, card verde,
        // e o piso de runway é uma semana. Importar isto é comprar estoque morto.
        var runway = RunwayCalculator.Estimate(TresSemanas(700, 2100, 2520), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(runway.FitsLeadTime(30, 30), Is.False, "aéreo: não dá tempo");
            Assert.That(runway.FitsLeadTime(3, 30), Is.False, "nem a revenda nacional, com janela de 30 dias");
        });
    }

    [Test]
    public void OPortaoUsaOLimiteInferior_NuncaOSuperior()
    {
        // Acelerando vai de 6 a 16 semanas. Um prazo de 100 dias cabe no teto (112 dias) e
        // NÃO cabe no piso (42). Se o gate usasse o teto, a compra passaria — e é exatamente
        // esse otimismo que custa dinheiro.
        var runway = RunwayCalculator.Estimate(TresSemanas(700, 1400, 2800), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(runway.UpperWeeks * 7, Is.GreaterThan(100), "o teto cobriria o prazo");
            Assert.That(runway.FitsLeadTime(100, 0), Is.False, "mas o portão recusa, porque usa o piso");
        });
    }

    [Test]
    public void FaixaNuncaViraPonto()
    {
        // Toda fase estimável devolve os dois lados. Um número só esconderia a incerteza.
        foreach (var serie in new[] { TresSemanas(700, 1400, 2800), TresSemanas(700, 2100, 2520), TresSemanas(2800, 1400, 700) })
        {
            var runway = RunwayCalculator.Estimate(serie, Opcoes);

            Assert.That(runway.LowerWeeks, Is.Not.Null, runway.Reason);
            Assert.That(runway.UpperWeeks, Is.GreaterThan(runway.LowerWeeks), runway.Reason);
        }
    }
}
