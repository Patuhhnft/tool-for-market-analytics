using LeitorMercadoLivre.Domain.Pricing;

namespace LeitorMercadoLivre.Domain.Margin;

public sealed class MarginOptions
{
    public const string SectionName = "Margin";

    public decimal TargetMargin { get; init; } = 0.30m;

    /// <summary>IOF + spread do meio de pagamento, somado à PTAX no que é pago no exterior.</summary>
    public decimal ExchangeSpread { get; init; } = 0.06m;

    public void Validate()
    {
        if (TargetMargin < 0 || TargetMargin >= 1) throw new ArgumentException("Margin:TargetMargin deve ficar em [0, 1).", nameof(TargetMargin));
        if (ExchangeSpread < 0) throw new ArgumentException("Margin:ExchangeSpread não pode ser negativo.", nameof(ExchangeSpread));
    }
}

/// <summary>Os três preços em que a margem é calculada: piso (p15), mercado e teto (p85).</summary>
public sealed record MarketPricePoints(decimal Floor, decimal Market, decimal Ceiling)
{
    /// <summary>
    /// Sem preço de mercado calculado não existe margem: <c>null</c> em vez de um número
    /// montado em cima de amostra insuficiente ou sem âncora (I6).
    /// </summary>
    public static MarketPricePoints? From(PriceReferenceResult reference) =>
        reference is { Status: PriceStatus.Calculated, CleanRangeLow: { } floor, MarketPrice: { } market, CleanRangeHigh: { } ceiling }
            ? new MarketPricePoints(floor, market, ceiling)
            : null;
}

public sealed record PricePointMargin(decimal Price, SaleCost SaleCost, decimal Profit, decimal Margin);

/// <summary>
/// Quanto posso pagar por uma unidade. Depende SÓ do preço de mercado, das tarifas do
/// Mercado Livre e da margem-alvo — não do fornecedor.
/// <para>
/// É por isso que ele existe separado da margem: a margem responde "este fornecedor
/// compensa?" e precisa de um fornecedor cadastrado; o teto responde "quanto vale a pena
/// pagar?" e pode ser respondido no primeiro ciclo, antes de existir fornecedor algum. É
/// com ele que se vai negociar.
/// </para>
/// </summary>
/// <param name="AtMarket">Teto vendendo no preço de mercado.</param>
/// <param name="InRange">
/// Teto que aguenta QUALQUER preço da faixa praticada. É o número conservador, o que vale
/// mesmo se o mercado empurrar para o pior ponto — e é ele que se leva para a negociação.
/// </param>
/// <param name="BindingPrice">
/// O preço da faixa onde a restrição aperta. Costuma ser o piso, mas não sempre: o limiar de
/// frete grátis pode ser pior que o piso, e saber ONDE aperta explica o número.
/// </param>
public sealed record PurchaseCeiling(
    decimal AtMarket,
    decimal InRange,
    decimal BindingPrice,
    decimal TargetMargin,
    MarketPricePoints Prices)
{
    /// <summary>
    /// Teto negativo ou zero: nenhum preço de compra fecha a margem-alvo nesta faixa — as
    /// tarifas sozinhas já comem o alvo. Não é "compre barato", é "não dá".
    /// </summary>
    public bool Viable => InRange > 0m;
}

/// <summary>Intervalo de preço [From, To).</summary>
public sealed record PriceRange(decimal From, decimal To);

public sealed record MarginResult
{
    public required decimal UnitCost { get; init; }

    public required PricePointMargin Floor { get; init; }

    public required PricePointMargin Market { get; init; }

    public required PricePointMargin Ceiling { get; init; }

    /// <summary>
    /// A pior margem em TODA a faixa praticada, não só nos três pontos. Por causa do limiar
    /// de frete, ela pode estar no meio da faixa — e pode ser pior que a do piso.
    /// </summary>
    public required PricePointMargin WorstInRange { get; init; }

    public required decimal RoiAtMarket { get; init; }

    /// <summary>
    /// Menor preço a partir do qual há lucro. Normalmente é onde o lucro cruza zero; se o
    /// custo de venda cair num ponto de quebra, pode ser o próprio ponto. <c>null</c> se
    /// nenhum preço dá lucro.
    /// </summary>
    public required decimal? BreakEvenPrice { get; init; }

    /// <summary>
    /// Faixas ACIMA do ponto de equilíbrio em que se volta a ter prejuízo. Existem quando o
    /// frete absorvido no limiar come o lucro: vender um pouco mais caro dá prejuízo.
    /// </summary>
    public required IReadOnlyList<PriceRange> LossZones { get; init; }

    /// <summary>Maior custo unitário que ainda entrega a margem-alvo no preço de mercado.</summary>
    public required decimal MaxUnitCostAtMarket { get; init; }

    /// <summary>
    /// Maior custo unitário que entrega a margem-alvo em QUALQUER preço da faixa praticada.
    /// É o teto de compra seguro: vale mesmo se o mercado me empurrar para o pior ponto.
    /// </summary>
    public required decimal MaxUnitCostInRange { get; init; }

    public required decimal TargetMargin { get; init; }

    public bool WorstBelowTarget => WorstInRange.Margin < TargetMargin;
}
