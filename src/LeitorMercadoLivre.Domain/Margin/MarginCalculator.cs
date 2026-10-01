namespace LeitorMercadoLivre.Domain.Margin;

/// <summary>
/// Quanto sobra. Puro, sem I/O, sem arredondamento (I3).
/// <para>
/// Todo o raciocínio exato daqui se apoia num fato: entre dois pontos de quebra da
/// <see cref="SaleCostSchedule"/>, o lucro é uma reta crescente no preço, e a margem
/// (lucro ÷ preço) também cresce. Logo o pior caso de cada trecho está no começo dele, e
/// o ponto de equilíbrio de cada trecho tem fórmula fechada. Nada precisa ser varrido.
/// </para>
/// </summary>
public static class MarginCalculator
{
    public static MarginResult Calculate(
        decimal unitCost,
        MarketPricePoints prices,
        SaleCostSchedule schedule,
        MarginOptions options)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (unitCost <= 0) throw new ArgumentOutOfRangeException(nameof(unitCost), "Custo unitário precisa ser positivo.");
        if (prices.Floor <= 0 || prices.Floor > prices.Market || prices.Market > prices.Ceiling)
        {
            throw new ArgumentException("Os preços precisam respeitar 0 < piso ≤ mercado ≤ teto.", nameof(prices));
        }

        var market = At(prices.Market, unitCost, schedule);
        var (breakEven, lossZones) = AnalyzeProfitCurve(unitCost, schedule);
        var candidates = RangeCandidates(prices, schedule);

        return new MarginResult
        {
            UnitCost = unitCost,
            Floor = At(prices.Floor, unitCost, schedule),
            Market = market,
            Ceiling = At(prices.Ceiling, unitCost, schedule),
            WorstInRange = candidates
                .Select(price => At(price, unitCost, schedule))
                .OrderBy(point => point.Margin)
                .ThenBy(point => point.Price)
                .First(),
            RoiAtMarket = market.Profit / unitCost,
            BreakEvenPrice = breakEven,
            LossZones = lossZones,
            MaxUnitCostAtMarket = MaxUnitCostFor(prices.Market, schedule, options.TargetMargin),
            MaxUnitCostInRange = MaxUnitCostAcrossRange(prices, schedule, options.TargetMargin).Max,
            TargetMargin = options.TargetMargin
        };
    }

    /// <summary>
    /// Quanto posso pagar por uma unidade, SEM fornecedor nenhum.
    /// <para>
    /// A margem-alvo é o que define o teto: <c>margem(P) ≥ alvo ⇔ custo ≤ P × (1 − alvo) −
    /// custo_venda(P)</c>. Nada nessa conta depende de quem vende para mim — só do preço que o
    /// mercado pratica, das tarifas do Mercado Livre e do alvo. Por isso o teto existe desde o
    /// primeiro ciclo, e a margem só existe depois de haver fornecedor.
    /// </para>
    /// </summary>
    public static PurchaseCeiling Ceiling(MarketPricePoints prices, SaleCostSchedule schedule, MarginOptions options)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (prices.Floor <= 0 || prices.Floor > prices.Market || prices.Market > prices.Ceiling)
        {
            throw new ArgumentException("Os preços precisam respeitar 0 < piso ≤ mercado ≤ teto.", nameof(prices));
        }

        var (max, at) = MaxUnitCostAcrossRange(prices, schedule, options.TargetMargin);

        return new PurchaseCeiling(
            MaxUnitCostFor(prices.Market, schedule, options.TargetMargin),
            max,
            at,
            options.TargetMargin,
            prices);
    }

    /// <summary>Cada preço passa pela função de custo INTEIRA — nunca herda o custo de outro preço.</summary>
    private static PricePointMargin At(decimal price, decimal unitCost, SaleCostSchedule schedule)
    {
        var saleCost = schedule.At(price);
        var profit = price - unitCost - saleCost.Total;
        return new PricePointMargin(price, saleCost, profit, profit / price);
    }

    /// <summary>
    /// Onde a pior margem da faixa pode estar: no piso, ou no começo de qualquer trecho que
    /// comece dentro da faixa. Como a margem cresce dentro de cada trecho, não há outro lugar.
    /// </summary>
    private static List<decimal> RangeCandidates(MarketPricePoints prices, SaleCostSchedule schedule) =>
    [
        prices.Floor,
        .. schedule.Breakpoints.Where(point => point > prices.Floor && point <= prices.Ceiling),
        prices.Market,
        prices.Ceiling
    ];

    /// <summary>margem(P) ≥ alvo  ⇔  custo ≤ P × (1 − alvo) − custo_venda(P).</summary>
    private static decimal MaxUnitCostFor(decimal price, SaleCostSchedule schedule, decimal targetMargin) =>
        (price * (1 - targetMargin)) - schedule.At(price).Total;

    /// <summary>
    /// O maior custo que respeita a margem-alvo em todo preço de [piso, teto], e o preço onde
    /// a restrição aperta.
    /// <para>
    /// Em cada trecho, o custo máximo admissível é a reta P × (1 − alvo − taxa) − fixo. Ela
    /// costuma crescer, mas com alvo alto o bastante passa a decrescer — e aí o mínimo mora
    /// na ponta DIREITA do trecho. Por isso as duas pontas são avaliadas, a direita com a
    /// reta do próprio trecho (o limite antes do ponto de quebra).
    /// </para>
    /// </summary>
    private static (decimal Max, decimal At) MaxUnitCostAcrossRange(
        MarketPricePoints prices, SaleCostSchedule schedule, decimal targetMargin)
    {
        List<decimal> starts =
        [
            prices.Floor,
            .. schedule.Breakpoints.Where(point => point > prices.Floor && point <= prices.Ceiling)
        ];

        var tightest = decimal.MaxValue;
        var bindingAt = prices.Floor;

        for (var index = 0; index < starts.Count; index++)
        {
            var from = starts[index];
            var to = index + 1 < starts.Count ? starts[index + 1] : prices.Ceiling;
            var (variableRate, fixedAmount) = schedule.LinearTermsAt(from);

            decimal Admissible(decimal price) => (price * (1 - targetMargin - variableRate)) - fixedAmount;

            foreach (var price in (decimal[])[from, to])
            {
                var admissible = Admissible(price);
                if (admissible >= tightest) continue;

                tightest = admissible;
                bindingAt = price;
            }
        }

        return (tightest, bindingAt);
    }

    /// <summary>
    /// Percorre os trechos da função de custo. Em cada um, lucro(P) = P × (1 − taxa) − (custo + fixo),
    /// e o equilíbrio do trecho é (custo + fixo) ÷ (1 − taxa).
    /// </summary>
    private static (decimal? BreakEven, List<PriceRange> LossZones) AnalyzeProfitCurve(
        decimal unitCost,
        SaleCostSchedule schedule)
    {
        List<decimal> starts = [0m, .. schedule.Breakpoints.Where(point => point > 0m)];
        decimal? breakEven = null;
        var lossZones = new List<PriceRange>();

        for (var index = 0; index < starts.Count; index++)
        {
            var from = starts[index];
            var to = index + 1 < starts.Count ? starts[index + 1] : decimal.MaxValue;

            var (variableRate, fixedAmount) = schedule.LinearTermsAt(from);
            var slope = 1m - variableRate;

            // Taxas somando 100% ou mais: nenhum preço do trecho paga a conta.
            if (slope <= 0m)
            {
                if (breakEven is not null) lossZones.Add(new PriceRange(from, to));
                continue;
            }

            var segmentBreakEven = (unitCost + fixedAmount) / slope;

            if (breakEven is null)
            {
                // Primeiro trecho em que o lucro fica positivo. Se o equilíbrio cair antes do
                // começo do trecho, é porque o custo de venda caiu no ponto de quebra e o lucro
                // já nasce positivo aqui.
                if (segmentBreakEven < to) breakEven = Math.Max(segmentBreakEven, from);
                continue;
            }

            // Já passamos do equilíbrio: se o trecho começa no prejuízo, é zona de prejuízo.
            if (segmentBreakEven > from)
            {
                lossZones.Add(new PriceRange(from, Math.Min(segmentBreakEven, to)));
            }
        }

        return (breakEven, lossZones);
    }
}
