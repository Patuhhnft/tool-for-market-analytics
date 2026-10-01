namespace LeitorMercadoLivre.Domain.Opportunity;

public sealed class OpportunityOptions
{
    public const string SectionName = "Opportunity";

    public decimal TargetMargin { get; init; } = 0.30m;

    /// <summary>Pesos da pressão de preço: queda do preço, dispersão, queda da força da moda. Somam 1.</summary>
    public decimal PriceDropWeight { get; init; } = 0.4m;

    public decimal DispersionWeight { get; init; } = 0.3m;

    public decimal ModeStrengthDropWeight { get; init; } = 0.3m;

    /// <summary>Abaixo disto a demanda é considerada estável, não subindo.</summary>
    public decimal RisingDemandThreshold { get; init; } = 0.3m;

    /// <summary>Acima disto a concorrência é considerada aquecida.</summary>
    public decimal HotCompetitionThreshold { get; init; } = 0.3m;

    public void Validate()
    {
        var sum = PriceDropWeight + DispersionWeight + ModeStrengthDropWeight;
        if (sum != 1m) throw new ArgumentException($"Opportunity: os pesos da pressão de preço somam {sum}, precisam somar 1.");
        if (TargetMargin <= 0 || TargetMargin >= 1) throw new ArgumentException("Opportunity:TargetMargin deve ficar em (0, 1).");
    }
}

/// <summary>Fase do ciclo de vida do produto.</summary>
public enum Quadrant
{
    /// <summary>Demanda subindo, concorrência calma: a hora de entrar.</summary>
    OpenWindow,

    /// <summary>Demanda subindo, concorrência chegando junto.</summary>
    Race,

    /// <summary>Demanda parou de subir.</summary>
    Saturating,

    /// <summary>Demanda caindo.</summary>
    EndOfCycle
}

public sealed record OpportunityInput(
    decimal DemandScore,
    decimal Persistence,
    bool DemandFalling,
    int Sellers,
    int NewSellersThisWeek,
    decimal? WeeklyPriceChange,
    decimal? DispersionIndex,
    decimal? WeeklyModeStrengthChange,
    decimal? WorstMargin);

public sealed record OpportunityResult
{
    /// <summary>Média geométrica dos fatores disponíveis, em [0, 1].</summary>
    public required decimal Index { get; init; }

    public required decimal DemandFactor { get; init; }

    public required decimal SupplyFactor { get; init; }

    public required decimal PricePressure { get; init; }

    public required decimal PersistenceFactor { get; init; }

    /// <summary><c>null</c> quando não há margem calculada — sem fornecedor, não existe custo.</summary>
    public decimal? MarginFactor { get; init; }

    public bool IncludesMargin => MarginFactor is not null;

    public required Quadrant Quadrant { get; init; }
}

/// <summary>
/// Índice de oportunidade (§7): média geométrica de fatores em [0, 1] (I9).
/// <para>
/// É ORDEM DE PRIORIDADE, não placar de lucro: 0,62 não é "62% de alguma coisa". Quem
/// promete dinheiro é o bloco de margem.
/// </para>
/// <para>
/// Sem fornecedor não existe margem, e o fator de margem fica de fora — a média passa a ser
/// de quatro fatores e o resultado sai marcado (<see cref="OpportunityResult.IncludesMargin"/>).
/// A alternativa, esconder o índice até haver fornecedor, tiraria do painel justamente a
/// ordem que diz em qual produto vale a pena ir atrás de fornecedor.
/// </para>
/// </summary>
public static class OpportunityCalculator
{
    public static OpportunityResult Calculate(OpportunityInput input, OpportunityOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        options.Validate();

        var demand = Clamp(input.DemandScore);
        var persistence = Clamp(input.Persistence);

        // Crescimento da oferta: entrantes da semana sobre a base que já estava lá.
        var incumbents = Math.Max(input.Sellers - input.NewSellersThisWeek, 1);
        var supplyGrowth = Math.Max(0m, (decimal)input.NewSellersThisWeek / incumbents);
        var supply = Clamp(1m / (1m + supplyGrowth));

        // Sem o clamp, pressão acima de 1 inverteria o sinal de (1 − pressão).
        var pressure = Clamp(
            (options.PriceDropWeight * Drop(input.WeeklyPriceChange))
            + (options.DispersionWeight * Clamp(input.DispersionIndex ?? 0m))
            + (options.ModeStrengthDropWeight * Drop(input.WeeklyModeStrengthChange)));

        // Margem zero ou negativa zera o índice DE PROPÓSITO: não é oportunidade.
        decimal? margin = input.WorstMargin is { } worst
            ? (worst <= 0m ? 0m : Clamp(worst / options.TargetMargin))
            : null;

        List<decimal> factors = [demand, supply, 1m - pressure, persistence];
        if (margin is { } marginFactor) factors.Add(marginFactor);

        return new OpportunityResult
        {
            Index = GeometricMean(factors),
            DemandFactor = demand,
            SupplyFactor = supply,
            PricePressure = pressure,
            PersistenceFactor = persistence,
            MarginFactor = margin,
            Quadrant = Classify(input.DemandFalling, demand, Math.Max(1m - supply, pressure), options)
        };
    }

    /// <summary>
    /// Ciclo de vida: janela aberta → corrida → saturando → fim de ciclo. Demanda caindo é
    /// fim de ciclo independente da concorrência; demanda que parou de subir é saturação.
    /// </summary>
    private static Quadrant Classify(bool falling, decimal demand, decimal competition, OpportunityOptions options)
    {
        if (falling) return Quadrant.EndOfCycle;
        if (demand < options.RisingDemandThreshold) return Quadrant.Saturating;

        return competition >= options.HotCompetitionThreshold ? Quadrant.Race : Quadrant.OpenWindow;
    }

    /// <summary>
    /// Qualquer fator zero zera o índice — é a propriedade que interessa. A raiz evita que
    /// cinco fatores medianos esmaguem o índice perto de zero, como o produto puro faria.
    /// </summary>
    private static decimal GeometricMean(List<decimal> factors)
    {
        if (factors.Exists(factor => factor == 0m)) return 0m;

        // Potência fracionária não existe em decimal; o índice é adimensional, não dinheiro.
        var logSum = factors.Sum(factor => Math.Log((double)factor));
        return Clamp(Math.Round((decimal)Math.Exp(logSum / factors.Count), 10));
    }

    private static decimal Drop(decimal? weeklyChange) => weeklyChange is { } change ? Clamp(-change) : 0m;

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 1m);
}
