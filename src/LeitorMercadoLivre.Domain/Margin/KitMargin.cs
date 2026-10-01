namespace LeitorMercadoLivre.Domain.Margin;

/// <summary>
/// A margem vendendo N unidades num anúncio só.
/// </summary>
/// <param name="Units">Quantas unidades no kit.</param>
/// <param name="Price">Preço do kit: o preço unitário de mercado vezes as unidades.</param>
/// <param name="Margin">Margem no kit.</param>
/// <param name="MarginPerUnitSale">
/// A margem que o mesmo produto teria vendido avulso, para comparação direta.
/// </param>
public sealed record KitOption(int Units, decimal Price, decimal Margin, decimal MarginPerUnitSale)
{
    /// <summary>Quanto o kit ganha em pontos percentuais de margem.</summary>
    public decimal Gain => Margin - MarginPerUnitSale;
}

/// <summary>
/// Vender em kit para diluir o que é cobrado POR VENDA.
/// <para>
/// A tarifa fixa do Mercado Livre e a embalagem não dependem do preço: são cobradas uma vez
/// por venda. Num item de R$ 20, uma tarifa fixa de R$ 6,75 come um terço do preço. Vendendo
/// cinco unidades num anúncio de R$ 100, a mesma tarifa é cobrada uma vez só — e o que era
/// 34% do preço vira 7%.
/// </para>
/// <para>
/// Não é truque: é a razão de metade do Mercado Livre vender "kit com 10". O sistema
/// recomendava produtos baratos sem nunca dizer que avulso eles não fecham.
/// </para>
/// </summary>
public static class KitMarginCalculator
{
    /// <summary>
    /// A margem de cada tamanho de kit, no preço de mercado unitário.
    /// <para>
    /// Assume que N unidades valem N vezes o preço unitário — o que é otimista, porque kit
    /// costuma ter desconto. Por isso o número serve para COMPARAR tamanhos, não para prometer
    /// lucro: o card diz que é estimativa.
    /// </para>
    /// </summary>
    public static IReadOnlyList<KitOption> Evaluate(
        decimal unitCost,
        decimal unitMarketPrice,
        SaleCostSchedule schedule,
        IReadOnlyList<int> kitSizes)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (unitCost <= 0 || unitMarketPrice <= 0) return [];

        var single = MarginAt(unitCost, unitMarketPrice, 1, schedule);

        return
        [
            .. (kitSizes ?? [])
                .Where(units => units > 1)
                .Distinct()
                .Order()
                .Select(units =>
                {
                    var price = unitMarketPrice * units;
                    return new KitOption(units, price, MarginAt(unitCost, unitMarketPrice, units, schedule), single);
                })
        ];
    }

    /// <summary>
    /// Margem de um anúncio com <paramref name="units"/> unidades.
    /// <para>
    /// O custo de venda passa pela função INTEIRA no preço do kit — a comissão muda de faixa,
    /// e acima do limiar o vendedor passa a absorver frete. Reaproveitar o custo do preço
    /// unitário aqui daria um kit falsamente lucrativo.
    /// </para>
    /// </summary>
    private static decimal MarginAt(decimal unitCost, decimal unitPrice, int units, SaleCostSchedule schedule)
    {
        var price = unitPrice * units;
        var saleCost = schedule.At(price);

        // A embalagem é por VENDA, não por unidade: um kit vai numa caixa só. Já o custo do
        // produto é por unidade, e a comissão e o imposto são sobre o preço do kit.
        var profit = price - (unitCost * units) - saleCost.Total;

        return profit / price;
    }
}
