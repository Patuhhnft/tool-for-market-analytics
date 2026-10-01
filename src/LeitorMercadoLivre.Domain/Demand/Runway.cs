namespace LeitorMercadoLivre.Domain.Demand;

/// <summary>Onde o produto está na curva, lido do formato da série de visitas.</summary>
public enum DemandPhase
{
    /// <summary>Série curta ou irregular demais para afirmar qualquer coisa.</summary>
    Unknown,

    /// <summary>Subindo e ainda ganhando velocidade.</summary>
    Accelerating,

    /// <summary>Subindo em ritmo constante.</summary>
    Linear,

    /// <summary>Ainda acima da semana passada, mas já perdendo velocidade — o pico ficou para trás.</summary>
    Decelerating,

    /// <summary>Caindo.</summary>
    Falling
}

/// <summary>
/// Quanto tempo de janela ainda resta, em semanas, com a incerteza explícita.
/// <para>
/// <b>É uma faixa, nunca um ponto.</b> Com 19 dias de série não dá para ajustar uma logística
/// e dizer "faltam 7,3 semanas" — isso seria falsa precisão (I6). O que dá para fazer com
/// honestidade é classificar a FASE pelo formato da curva e associar a ela uma faixa de
/// runway calibrável.
/// </para>
/// <para>
/// Quem decide compra usa <see cref="LowerWeeks"/>, nunca a média: se a estimativa errar, que
/// erre para o lado de não comprar. Estoque encalhado custa mais que oportunidade perdida.
/// </para>
/// </summary>
public sealed record Runway(DemandPhase Phase, decimal? LowerWeeks, decimal? UpperWeeks, string Reason)
{
    /// <summary>
    /// Dá tempo de importar por um canal com este prazo?
    /// <para>
    /// Exige o prazo MAIS a janela mínima de venda: chegar no último dia da janela não é
    /// negócio, é estoque parado. Sem estimativa de runway, a resposta é <c>null</c> —
    /// "não sei" é diferente de "não dá".
    /// </para>
    /// </summary>
    public bool? FitsLeadTime(int leadTimeDays, int minimumSellingWindowDays)
    {
        if (LowerWeeks is not { } weeks) return null;

        var availableDays = weeks * 7m;
        return availableDays >= leadTimeDays + minimumSellingWindowDays;
    }
}

/// <summary>Faixas de runway por fase, em semanas. Calibráveis — nenhuma mora no código (I8).</summary>
public sealed record RunwayOptions
{
    /// <summary>Dias de série necessários para arriscar uma classificação.</summary>
    public int MinimumDays { get; init; } = 14;

    public decimal AcceleratingLower { get; init; } = 6m;
    public decimal AcceleratingUpper { get; init; } = 16m;
    public decimal LinearLower { get; init; } = 3m;
    public decimal LinearUpper { get; init; } = 8m;
    public decimal DeceleratingLower { get; init; } = 1m;
    public decimal DeceleratingUpper { get; init; } = 4m;
    public decimal FallingLower { get; init; } = 0m;
    public decimal FallingUpper { get; init; } = 2m;

    /// <summary>
    /// Variação entre metades abaixo da qual a curva conta como estável, em fração. Sem esta
    /// tolerância, ruído de um dia viraria "acelerando" ou "caindo".
    /// </summary>
    public decimal FlatTolerance { get; init; } = 0.05m;
}

public static class RunwayCalculator
{
    /// <summary>
    /// Lê a fase pelo formato da série e devolve a faixa de runway correspondente.
    /// <para>
    /// O método é deliberadamente grosseiro: compara a segunda metade da série com a primeira
    /// (está subindo?) e a última semana com a anterior (ainda está ganhando velocidade?).
    /// Duas perguntas, quatro respostas possíveis. Um ajuste logístico sobre 19 pontos daria
    /// um número bonito e sem lastro.
    /// </para>
    /// </summary>
    public static Runway Estimate(IReadOnlyList<DailyCount> series, RunwayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (series is null || series.Count < options.MinimumDays)
        {
            return new Runway(DemandPhase.Unknown, null, null,
                $"série de {series?.Count ?? 0} dias, abaixo dos {options.MinimumDays} necessários para estimar");
        }

        var ordered = series.OrderBy(day => day.Date).ToList();
        var half = ordered.Count / 2;

        decimal first = ordered.Take(half).Sum(day => day.Visits);
        decimal second = ordered.Skip(ordered.Count - half).Sum(day => day.Visits);

        if (first <= 0 && second <= 0)
        {
            return new Runway(DemandPhase.Unknown, null, null, "série sem visita alguma");
        }

        // Crescimento entre metades: a direção geral.
        var trend = first <= 0 ? 1m : (second - first) / first;

        // Aceleração: a última semana cresceu mais que a semana anterior cresceu?
        var recent = Momentum(ordered);

        var (phase, reason) = Classify(trend, recent, options);

        return phase switch
        {
            DemandPhase.Accelerating => new Runway(phase, options.AcceleratingLower, options.AcceleratingUpper, reason),
            DemandPhase.Linear => new Runway(phase, options.LinearLower, options.LinearUpper, reason),
            DemandPhase.Decelerating => new Runway(phase, options.DeceleratingLower, options.DeceleratingUpper, reason),
            DemandPhase.Falling => new Runway(phase, options.FallingLower, options.FallingUpper, reason),
            _ => new Runway(phase, null, null, reason)
        };
    }

    /// <summary>
    /// A curva ainda ganha velocidade? Compara o crescimento da última semana com o da
    /// anterior — a segunda diferença, em versão de gente.
    /// </summary>
    private static decimal? Momentum(List<DailyCount> ordered)
    {
        if (ordered.Count < 21) return null;

        decimal week(int fromEnd) => ordered
            .Skip(Math.Max(0, ordered.Count - fromEnd * 7))
            .Take(7)
            .Sum(day => day.Visits);

        var last = week(1);
        var previous = week(2);
        var before = week(3);

        if (previous <= 0 || before <= 0) return null;

        var lastGrowth = (last - previous) / previous;
        var previousGrowth = (previous - before) / before;

        return lastGrowth - previousGrowth;
    }

    private static (DemandPhase Phase, string Reason) Classify(decimal trend, decimal? momentum, RunwayOptions options)
    {
        if (trend < -options.FlatTolerance)
        {
            return (DemandPhase.Falling, $"segunda metade da série {Percent(trend)} contra a primeira");
        }

        if (trend <= options.FlatTolerance)
        {
            return (DemandPhase.Linear, "série estável entre as duas metades");
        }

        // Está subindo. O que separa "ainda tem janela" de "o pico passou" é a TAXA de
        // crescimento se sustentar ou desabar.
        //
        // Taxa constante conta como aceleração, e isso não é detalhe: uma curva que dobra toda
        // semana cresce a 100% sempre, então a diferença entre as taxas é exatamente ZERO —
        // e ela é o exemplo canônico da fase exponencial, antes da inflexão. Tratar esse zero
        // como "ritmo constante" classificaria justamente o melhor caso como morno.
        return momentum switch
        {
            // Subindo, mas sem três semanas não dá para saber se ainda acelera ou já passou do
            // pico — e é exatamente essa diferença que decide importar ou não. Chutar "linear"
            // aqui daria um piso de runway inventado; "não sei" é a resposta honesta (I6), e
            // ela vira <c>null</c> no portão, nunca "não dá".
            null => (DemandPhase.Unknown,
                $"subiu {Percent(trend)}, mas são precisas três semanas de série para saber se ainda acelera"),
            >= 0 => (DemandPhase.Accelerating, $"subiu {Percent(trend)} e o ritmo se sustenta"),
            _ => (DemandPhase.Decelerating, $"subiu {Percent(trend)}, mas o ritmo caiu — o pico ficou para trás")
        };
    }

    private static string Percent(decimal fraction) =>
        (fraction >= 0 ? "+" : "−") + Math.Round(Math.Abs(fraction) * 100m, 0).ToString("0") + "%";
}
