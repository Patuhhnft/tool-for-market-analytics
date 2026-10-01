namespace LeitorMercadoLivre.Domain.Margin;

/// <summary>
/// Faixa de tarifa do Mercado Livre: vale para preço ESTRITAMENTE abaixo de
/// <paramref name="UpToPrice"/>. A última faixa precisa ser aberta (<see cref="decimal.MaxValue"/>).
/// </summary>
/// <param name="PercentageRate">Comissão sobre o preço, como fração (0,16 = 16%).</param>
/// <param name="FixedFee">Tarifa fixa por unidade, em R$ (a dos itens baratos).</param>
public sealed record SaleFeeBand(decimal UpToPrice, decimal PercentageRate, decimal FixedFee);

/// <summary>Quanto custa vender uma unidade a um preço, termo a termo.</summary>
/// <param name="Packaging">
/// Caixa, plástico-bolha, fita. Em item de R$ 20 pesa tanto quanto a comissão, e a margem
/// ignorava isso por completo.
/// </param>
public sealed record SaleCost(
    decimal Price,
    decimal Commission,
    decimal FixedFee,
    decimal AbsorbedFreight,
    decimal RegimeTax,
    decimal Packaging = 0m)
{
    public decimal Total => Commission + FixedFee + AbsorbedFreight + RegimeTax + Packaging;
}

/// <summary>
/// Custo de venda como FUNÇÃO DO PREÇO.
/// <para>
/// Ele não é uma porcentagem fixa: a comissão depende da faixa, a tarifa fixa só existe
/// abaixo de um valor, e no limiar de frete grátis a estrutura vira — abaixo, o comprador
/// paga o frete; a partir dele, o vendedor absorve. Um mesmo produto pode estar dos dois
/// lados do limiar dentro da própria faixa praticada. Por isso cada preço passa pela
/// função inteira, e reaproveitar o custo de um preço em outro é proibido.
/// </para>
/// </summary>
public sealed class SaleCostSchedule
{
    private readonly SaleFeeBand[] bands;

    public SaleCostSchedule(
        IEnumerable<SaleFeeBand> bands,
        decimal freeShippingThreshold,
        decimal absorbedFreightPerUnit,
        decimal regimeTaxRate,
        decimal packagingPerUnit = 0m)
    {
        ArgumentNullException.ThrowIfNull(bands);
        this.bands = [.. bands.OrderBy(band => band.UpToPrice)];

        if (this.bands.Length == 0 || this.bands[^1].UpToPrice != decimal.MaxValue)
        {
            throw new ArgumentException("A última faixa de tarifa precisa ser aberta (decimal.MaxValue).", nameof(bands));
        }

        foreach (var band in this.bands)
        {
            if (band.PercentageRate < 0 || band.PercentageRate >= 1 || band.FixedFee < 0)
            {
                throw new ArgumentException($"Faixa até {band.UpToPrice} inválida.", nameof(bands));
            }
        }

        if (freeShippingThreshold <= 0) throw new ArgumentException("Limiar de frete grátis precisa ser positivo.", nameof(freeShippingThreshold));
        if (absorbedFreightPerUnit < 0) throw new ArgumentException("Frete absorvido não pode ser negativo.", nameof(absorbedFreightPerUnit));
        if (regimeTaxRate < 0 || regimeTaxRate >= 1) throw new ArgumentException("Imposto do regime deve ficar em [0, 1).", nameof(regimeTaxRate));
        if (packagingPerUnit < 0) throw new ArgumentException("Embalagem por unidade não pode ser negativa.", nameof(packagingPerUnit));

        PackagingPerUnit = packagingPerUnit;

        FreeShippingThreshold = freeShippingThreshold;
        AbsorbedFreightPerUnit = absorbedFreightPerUnit;
        RegimeTaxRate = regimeTaxRate;
    }

    /// <summary>As faixas de tarifa, para serem gravadas com a análise e reconstruídas depois.</summary>
    public IReadOnlyList<SaleFeeBand> Bands => bands;

    public decimal FreeShippingThreshold { get; }

    /// <summary>
    /// Frete que o vendedor paga por unidade a partir do limiar. Zero para quem não aderiu
    /// ao Mercado Envios e não oferece frete grátis.
    /// </summary>
    public decimal AbsorbedFreightPerUnit { get; }

    /// <summary>
    /// Imposto do regime sobre a venda (ex.: alíquota efetiva do Simples). No MEI é zero:
    /// o DAS é fixo por mês e não varia com a venda.
    /// </summary>
    public decimal RegimeTaxRate { get; }

    /// <summary>
    /// Embalagem por unidade. Custo FIXO: não varia com o preço, então em produto barato ele
    /// morde uma fração muito maior da margem — é exatamente onde o vendedor se engana.
    /// </summary>
    public decimal PackagingPerUnit { get; }

    public SaleCost At(decimal price)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Preço negativo.");

        var band = BandFor(price);
        var absorbsFreight = price >= FreeShippingThreshold;

        return new SaleCost(
            price,
            price * band.PercentageRate,
            band.FixedFee,
            absorbsFreight ? AbsorbedFreightPerUnit : 0m,
            price * RegimeTaxRate,
            PackagingPerUnit);
    }

    /// <summary>
    /// Preços onde a estrutura de custo muda. Entre dois deles, o custo é uma reta — é o que
    /// permite achar ponto de equilíbrio e pior margem de forma exata, sem varrer centavos.
    /// </summary>
    internal IReadOnlyList<decimal> Breakpoints =>
    [
        .. bands
            .Select(band => band.UpToPrice)
            .Where(limit => limit != decimal.MaxValue)
            .Append(FreeShippingThreshold)
            .Distinct()
            .Order()
    ];

    /// <summary>
    /// Termos lineares do custo de venda válidos a partir de <paramref name="price"/> até o
    /// próximo ponto de quebra: custo = preço × taxa variável + valor fixo.
    /// </summary>
    internal (decimal VariableRate, decimal FixedAmount) LinearTermsAt(decimal price)
    {
        var cost = At(price);
        return (BandFor(price).PercentageRate + RegimeTaxRate, cost.FixedFee + cost.AbsorbedFreight + cost.Packaging);
    }

    private SaleFeeBand BandFor(decimal price) => bands.First(band => price < band.UpToPrice);
}
