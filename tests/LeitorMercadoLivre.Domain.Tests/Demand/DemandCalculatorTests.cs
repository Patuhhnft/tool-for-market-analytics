using LeitorMercadoLivre.Domain.Demand;

namespace LeitorMercadoLivre.Domain.Tests.Demand;

[TestFixture]
public class DemandCalculatorTests
{
    private static readonly DemandOptions Opcoes = new();
    private static readonly DateOnly Hoje = new(2026, 9, 24);

    /// <summary>Série de 7 dias por semana, com o total de cada semana concentrado no último dia dela.</summary>
    private static List<DailyCount> Semanas(params int[] totais)
    {
        var inicio = Hoje.AddDays(-7 * totais.Length);
        return
        [
            .. Enumerable.Range(0, totais.Length * 7).Select(dia =>
                new DailyCount(inicio.AddDays(dia), dia % 7 == 6 ? totais[dia / 7] : 0))
        ];
    }

    // ----------------------------------------------------------------------------------
    // Higiene da série — a resposta REAL de /visits/time_window no diagnóstico de 24/09
    // ----------------------------------------------------------------------------------

    private static readonly DailyCount[] RespostaReal =
    [
        new(new DateOnly(2026, 9, 21), 17),
        new(new DateOnly(2026, 9, 23), 389),
        new(new DateOnly(2026, 9, 24), 11),
        new(new DateOnly(2026, 9, 22), 386)
    ];

    [Test]
    public void DiasOmitidosEntramComoZero()
    {
        // Pedimos 7 dias, vieram 4 (fora de ordem). Os dias 17 a 20 não vieram porque não
        // tiveram visita — e têm de existir na série, com zero.
        var serie = DemandCalculator.NormalizeSeries(RespostaReal, Hoje, days: 7);

        Assert.Multiple(() =>
        {
            Assert.That(serie, Has.Count.EqualTo(7));
            Assert.That(serie[0].Date, Is.EqualTo(new DateOnly(2026, 9, 17)));
            Assert.That(serie.Take(4).Select(dia => dia.Visits), Is.All.EqualTo(0));
            Assert.That(serie.Select(dia => dia.Visits).Skip(4), Is.EqualTo(new[] { 17, 386, 389 }));
        });
    }

    [Test]
    public void DiaCorrenteFicaForaPorqueVemPelaMetade()
    {
        // A API somou 803 incluindo as 11 visitas de hoje, que são de poucas horas.
        var serie = DemandCalculator.NormalizeSeries(RespostaReal, Hoje, days: 7);

        Assert.Multiple(() =>
        {
            Assert.That(serie[^1].Date, Is.EqualTo(new DateOnly(2026, 9, 23)), "termina ontem");
            Assert.That(serie.Sum(dia => dia.Visits), Is.EqualTo(792), "803 − 11 de hoje");
        });
    }

    [Test]
    public void ManhaENoiteDaoAMesmaSerie()
    {
        // Sem excluir o dia corrente, o produto pareceria em colapso de manhã e em alta à noite.
        var deManha = RespostaReal.Append(new DailyCount(Hoje, 3));
        var aNoite = RespostaReal.Append(new DailyCount(Hoje, 500));

        Assert.That(
            DemandCalculator.NormalizeSeries(deManha, Hoje, 7),
            Is.EqualTo(DemandCalculator.NormalizeSeries(aNoite, Hoje, 7)));
    }

    // ----------------------------------------------------------------------------------
    // Crescimento
    // ----------------------------------------------------------------------------------

    [Test]
    public void CrescimentoConferidoAMao()
    {
        // 1.000 → 1.600: delta 600, taxa 0,6
        // v = 600 ÷ 1.100 = 0,5455 · t = ln 1,6 ÷ (ln 1,6 + ln 1,5) = 0,5369
        // g = √(0,5455 × 0,5369) = 0,5411
        var demanda = DemandCalculator.FromVisits(Semanas(1000, 1600), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(demanda.Source, Is.EqualTo(DemandSource.Visits));
            Assert.That(demanda.AbsoluteDelta, Is.EqualTo(600));
            Assert.That(demanda.Rate, Is.EqualTo(0.6m));
            Assert.That(demanda.Score, Is.EqualTo(0.5411m).Within(0.0001m));
            Assert.That(demanda.RisingWeeks, Is.EqualTo(1));
            Assert.That(demanda.Persistence, Is.EqualTo(0.25m), "1 de 4 semanas");
            Assert.That(demanda.LowDemand, Is.False);
        });
    }

    [Test]
    public void BasePequenaNaoGanhaDeVolumeReal()
    {
        // I11. 2 → 20 são +900%; 5.000 → 8.000 são +60%. Por taxa, o primeiro ganharia.
        var foguetinho = DemandCalculator.FromVisits(Semanas(2, 20), Opcoes);
        var mercado = DemandCalculator.FromVisits(Semanas(5000, 8000), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(foguetinho.Rate, Is.GreaterThan(mercado.Rate!.Value));
            Assert.That(mercado.Score, Is.GreaterThan(foguetinho.Score));
            Assert.That(foguetinho.LowDemand, Is.True, "delta 18 abaixo do piso de 300");
            Assert.That(mercado.LowDemand, Is.False);
        });
    }

    [Test]
    public void VolumeAltoEEstagnadoNaoViraCrescimento()
    {
        var parado = DemandCalculator.FromVisits(Semanas(5000, 5010), Opcoes);

        Assert.That(parado.Score, Is.LessThan(0.05m));
    }

    [Test]
    public void DemandaCaindoZeraOEscore()
    {
        var caindo = DemandCalculator.FromVisits(Semanas(1000, 600), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(caindo.Score, Is.Zero);
            Assert.That(caindo.Falling, Is.True);
            Assert.That(caindo.AbsoluteDelta, Is.EqualTo(-400));
        });
    }

    [Test]
    public void AceleracaoEPersistenciaSaemDaMesmaSerie()
    {
        // 100 → 300 → 700: deltas 200 e 400, aceleração +200, duas semanas seguidas de alta.
        var demanda = DemandCalculator.FromVisits(Semanas(100, 300, 700), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(demanda.Acceleration, Is.EqualTo(200));
            Assert.That(demanda.RisingWeeks, Is.EqualTo(2));
            Assert.That(demanda.Persistence, Is.EqualTo(0.5m));
        });
    }

    [Test]
    public void UmaSemanaSoNaoBastaParaMedirCrescimento()
    {
        var curta = DemandCalculator.FromVisits(Semanas(800), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(curta.Source, Is.EqualTo(DemandSource.Insufficient));
            Assert.That(curta.Score, Is.Zero);
            Assert.That(curta.LastWeekVisits, Is.EqualTo(800));
        });
    }

    // ----------------------------------------------------------------------------------
    // Fallback por posição no ranking
    // ----------------------------------------------------------------------------------

    [Test]
    public void SubirPosicoesViraProxyComFonteMarcada()
    {
        // 12º → 7º: ganho 5 = meia-escala → 0,5
        var demanda = DemandCalculator.FromRanking(currentPosition: 7, positionWeekAgo: 12, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(demanda.Source, Is.EqualTo(DemandSource.Ranking));
            Assert.That(demanda.PositionGain, Is.EqualTo(5));
            Assert.That(demanda.Score, Is.EqualTo(0.5m));
            Assert.That(demanda.LowDemand, Is.True, "posição não comprova volume");
        });
    }

    [Test]
    public void SemPosicaoAnteriorNaoHaProxy()
    {
        Assert.That(DemandCalculator.FromRanking(7, null, Opcoes).Source, Is.EqualTo(DemandSource.Insufficient));
    }
}
