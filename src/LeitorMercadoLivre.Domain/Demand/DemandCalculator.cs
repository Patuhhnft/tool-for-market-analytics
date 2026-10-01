namespace LeitorMercadoLivre.Domain.Demand;

public sealed class DemandOptions
{
    public const string SectionName = "Demand";

    /// <summary>Delta semanal mínimo, em visitas, para crescimento contar como relevante. PROVISÓRIO: calibrar.</summary>
    public int LowDemandFloor { get; init; } = 300;

    /// <summary>Meia-escala do componente de volume: com delta = D, v_norm = 0,5. PROVISÓRIO: calibrar.</summary>
    public decimal VolumeHalfScale { get; init; } = 500m;

    /// <summary>Meia-escala da taxa: com taxa = k, t_norm = 0,5.</summary>
    public decimal RateHalfScale { get; init; } = 0.5m;

    /// <summary>Semanas consecutivas de alta que valem persistência máxima.</summary>
    public int PersistenceWindowWeeks { get; init; } = 4;

    /// <summary>Meia-escala do proxy por posição: subir tantas posições em 7 dias dá 0,5.</summary>
    public decimal RankingHalfScale { get; init; } = 5m;

    /// <summary>
    /// Visitas mínimas na semana anterior para a TAXA valer como número. Abaixo disso o
    /// percentual fica nulo: 2 → 10 visitas é +400% e não significa nada.
    /// </summary>
    public int MinBaseVisitsForRate { get; init; } = 50;

    /// <summary>A partir deste crescimento (em %), o produto conta como "explodindo".</summary>
    public decimal ExplodingGrowthPercent { get; init; } = 300m;

    /// <summary>
    /// Visitas na última semana para demanda NOVA (semana anterior zerada) contar como
    /// explodindo. Sem percentual para comparar, o que resta é o volume absoluto.
    /// </summary>
    public int MinVisitsForNewDemand { get; init; } = 500;

    public void Validate()
    {
        if (LowDemandFloor < 0) throw new ArgumentException("Demand:LowDemandFloor não pode ser negativo.");
        if (VolumeHalfScale <= 0 || RateHalfScale <= 0 || RankingHalfScale <= 0)
        {
            throw new ArgumentException("Demand: as meia-escalas precisam ser positivas.");
        }

        if (PersistenceWindowWeeks < 1) throw new ArgumentException("Demand:PersistenceWindowWeeks precisa ser ao menos 1.");
        if (MinBaseVisitsForRate < 0 || MinVisitsForNewDemand < 0) throw new ArgumentException("Demand: os pisos de visitas não podem ser negativos.");
        if (ExplodingGrowthPercent <= 0) throw new ArgumentException("Demand:ExplodingGrowthPercent precisa ser positivo.");
    }
}

public enum DemandSource
{
    /// <summary>Série de visitas — demanda medida.</summary>
    Visits,

    /// <summary>Variação de posição no ranking — proxy ordinal, confiança menor.</summary>
    Ranking,

    /// <summary>Nem série suficiente nem histórico de posição.</summary>
    Insufficient
}

public sealed record DailyCount(DateOnly Date, int Visits);

public sealed record DemandResult
{
    public required DemandSource Source { get; init; }

    /// <summary>g_demanda em [0, 1].</summary>
    public required decimal Score { get; init; }

    /// <summary>f_persistência em [0, 1].</summary>
    public required decimal Persistence { get; init; }

    public int? LastWeekVisits { get; init; }

    public int? PreviousWeekVisits { get; init; }

    public int? AbsoluteDelta { get; init; }

    public decimal? Rate { get; init; }

    /// <summary>Segunda diferença das semanas: a alta está acelerando (+) ou freando (−).</summary>
    public int? Acceleration { get; init; }

    public int RisingWeeks { get; init; }

    public bool LowDemand { get; init; }

    public bool Falling { get; init; }

    public int? CurrentPosition { get; init; }

    public int? PositionGain { get; init; }

    /// <summary>A série exatamente como entrou na conta: completa, com zeros, sem o dia corrente.</summary>
    public IReadOnlyList<DailyCount> Series { get; init; } = [];
}

/// <summary>
/// Como o crescimento de visitas se apresenta para filtro e exibição.
/// </summary>
/// <param name="Percent">
/// Crescimento em pontos percentuais, ou <c>null</c> quando não é mensurável: sem semana
/// anterior, base zerada, ou base abaixo do piso. Nunca infinito.
/// </param>
/// <param name="IsNewDemand">Semana anterior sem visita alguma — não há percentual, há estreia.</param>
/// <param name="SmallBase">
/// Houve semana anterior, mas pequena demais para o percentual significar algo. Diferente de
/// <see cref="IsNewDemand"/>: ali o percentual não existe, aqui ele existe e é ruído.
/// </param>
/// <param name="StartedOn">
/// Primeiro dia com visita da série, quando a demanda é nova. É a informação que o percentual
/// escondia: o interessante não é "cresceu muito", é "começou nesta data".
/// </param>
public sealed record VisitGrowth(
    decimal? Percent,
    bool IsNewDemand,
    int? LastWeekVisits,
    bool SmallBase = false,
    DateOnly? StartedOn = null)
{
    public static VisitGrowth From(DemandResult demand, DemandOptions options)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(options);

        if (demand.Source != DemandSource.Visits) return new VisitGrowth(null, false, demand.LastWeekVisits);

        var previous = demand.PreviousWeekVisits ?? 0;
        var last = demand.LastWeekVisits ?? 0;

        // Semana anterior zerada: percentual seria divisão por zero. Vira estreia, com a data
        // em que a curva saiu do zero — que diz mais a quem decide do que qualquer percentual.
        if (previous == 0)
        {
            return new VisitGrowth(null, last > 0, demand.LastWeekVisits, StartedOn: FirstDayWithVisits(demand));
        }

        // Base pequena demais: a taxa existe, mas não significa nada (I11).
        if (previous < options.MinBaseVisitsForRate)
        {
            return new VisitGrowth(null, false, demand.LastWeekVisits, SmallBase: true);
        }

        return new VisitGrowth(Math.Round((demand.Rate ?? 0m) * 100m, 4), false, demand.LastWeekVisits);
    }

    private static DateOnly? FirstDayWithVisits(DemandResult demand) =>
        demand.Series.FirstOrDefault(day => day.Visits > 0)?.Date;

    /// <summary>Cresceu o bastante para "explodindo" — por percentual, ou por volume na estreia.</summary>
    public bool IsExploding(DemandOptions options) =>
        Percent >= options.ExplodingGrowthPercent ||
        (IsNewDemand && LastWeekVisits >= options.MinVisitsForNewDemand);
}

/// <summary>
/// Demanda de um produto a partir das visitas dos seus anúncios (§7).
/// <para>
/// log, raiz e potência não existem em <c>decimal</c>, então os componentes normalizados
/// passam por <c>double</c> e voltam. É uma exceção consciente à I1: g_demanda é um escore
/// adimensional, não dinheiro, e nada monetário atravessa esse trecho.
/// </para>
/// </summary>
public static class DemandCalculator
{
    /// <summary>
    /// Monta a série que entra na conta. A API omite os dias sem visita e devolve o dia
    /// corrente pela metade: aqui a janela é preenchida com zero e termina ONTEM.
    /// </summary>
    public static IReadOnlyList<DailyCount> NormalizeSeries(IEnumerable<DailyCount> raw, DateOnly today, int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var byDate = raw
            .Where(point => point.Date < today)
            .GroupBy(point => point.Date)
            .ToDictionary(group => group.Key, group => group.Sum(point => point.Visits));

        var firstDay = today.AddDays(-days);

        return
        [
            .. Enumerable.Range(0, days)
                .Select(offset => firstDay.AddDays(offset))
                .Select(date => new DailyCount(date, byDate.GetValueOrDefault(date)))
        ];
    }

    public static DemandResult FromVisits(IReadOnlyList<DailyCount> series, DemandOptions options)
    {
        ArgumentNullException.ThrowIfNull(series);
        options.Validate();

        var weeks = WeeklyTotals(series);

        if (weeks.Count < 2)
        {
            return new DemandResult
            {
                Source = DemandSource.Insufficient,
                Score = 0m,
                Persistence = 0m,
                LastWeekVisits = weeks.Count == 1 ? weeks[0] : null,
                Series = series
            };
        }

        var last = weeks[^1];
        var previous = weeks[^2];
        var delta = last - previous;
        var rate = (decimal)delta / Math.Max(previous, 1);
        var rising = RisingWeeks(weeks);

        return new DemandResult
        {
            Source = DemandSource.Visits,
            Score = delta > 0 ? Score(delta, rate, options) : 0m,
            Persistence = Math.Min(1m, (decimal)rising / options.PersistenceWindowWeeks),
            LastWeekVisits = last,
            PreviousWeekVisits = previous,
            AbsoluteDelta = delta,
            Rate = rate,
            Acceleration = weeks.Count >= 3 ? (last - previous) - (previous - weeks[^3]) : null,
            RisingWeeks = rising,
            LowDemand = delta < options.LowDemandFloor,
            Falling = delta < 0,
            Series = series
        };
    }

    /// <summary>
    /// Fallback quando não há série de visitas: quantas posições o produto ganhou no ranking
    /// BEST_SELLER. Posição é ordinal — 40→30 não equivale a 10→1 — por isso a fonte fica
    /// marcada e o card leva selo próprio.
    /// </summary>
    public static DemandResult FromRanking(int currentPosition, int? positionWeekAgo, DemandOptions options)
    {
        options.Validate();

        if (positionWeekAgo is null)
        {
            return new DemandResult
            {
                Source = DemandSource.Insufficient,
                Score = 0m,
                Persistence = 0m,
                CurrentPosition = currentPosition
            };
        }

        var gain = positionWeekAgo.Value - currentPosition;

        return new DemandResult
        {
            Source = DemandSource.Ranking,
            Score = gain > 0 ? Saturate(gain, options.RankingHalfScale) : 0m,
            Persistence = 0m,
            CurrentPosition = currentPosition,
            PositionGain = gain,
            Falling = gain < 0,
            LowDemand = true
        };
    }

    /// <summary>g_demanda = √(v_norm × t_norm): exige volume E taxa (I11).</summary>
    private static decimal Score(int delta, decimal rate, DemandOptions options)
    {
        var volume = Saturate(delta, options.VolumeHalfScale);

        var logRate = Math.Log(1 + (double)rate);
        var logHalf = Math.Log(1 + (double)options.RateHalfScale);
        var rateComponent = logRate / (logRate + logHalf);

        return ToScore(Math.Sqrt((double)volume * rateComponent));
    }

    private static decimal Saturate(decimal value, decimal halfScale) => value / (value + halfScale);

    /// <summary>Blocos de 7 dias contados do fim; sobra incompleta no início é descartada.</summary>
    private static List<int> WeeklyTotals(IReadOnlyList<DailyCount> series)
    {
        var fullWeeks = series.Count / 7;
        var skip = series.Count - (fullWeeks * 7);

        return
        [
            .. Enumerable.Range(0, fullWeeks)
                .Select(week => series.Skip(skip + (week * 7)).Take(7).Sum(point => point.Visits))
        ];
    }

    /// <summary>Semanas seguidas de alta terminando na mais recente.</summary>
    private static int RisingWeeks(List<int> weeks)
    {
        var count = 0;
        for (var index = weeks.Count - 1; index > 0 && weeks[index] > weeks[index - 1]; index--)
        {
            count++;
        }

        return count;
    }

    private static decimal ToScore(double value) => Math.Clamp(Math.Round((decimal)value, 10), 0m, 1m);
}
