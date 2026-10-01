namespace LeitorMercadoLivre.Domain.Margin;

/// <summary>
/// Uma compra no exterior.
/// <para>
/// Tudo que pertence à remessa é informado no TOTAL da remessa e só é dividido por unidade
/// no fim: o tributo incide sobre a declaração inteira, e a dedução do II é por remessa,
/// não por peça. Dividir antes erraria a dedução.
/// </para>
/// </summary>
/// <param name="SupplierUnitPrice">Preço unitário do fornecedor, em moeda estrangeira.</param>
/// <param name="InternationalFreightTotal">Frete internacional da remessa, em moeda estrangeira.</param>
/// <param name="FiscalExchangeRate">Câmbio fiscal (PTAX de venda) usado como base dos tributos.</param>
/// <param name="DomesticFreightTotal">Frete nacional da remessa até mim, em R$.</param>
/// <param name="CustomsFeesTotal">Despesas aduaneiras (Siscomex etc.), em R$. Entram na base do ICMS.</param>
/// <param name="InsuranceTotal">Seguro internacional, em moeda estrangeira. Compõe o valor aduaneiro.</param>
public sealed record AcquisitionInput(
    decimal SupplierUnitPrice,
    int Quantity,
    decimal InternationalFreightTotal,
    decimal FiscalExchangeRate,
    decimal DomesticFreightTotal,
    ImportTaxRates Taxes,
    decimal CustomsFeesTotal = 0m,
    decimal InsuranceTotal = 0m);

/// <summary>Uma linha da memória de cálculo: o que é, quanto deu e de onde veio.</summary>
public sealed record CalculationLine(string Label, decimal Value, string Formula);

/// <summary>
/// Custo de pôr a mercadoria na minha mão, com a memória de cálculo linha a linha (I7).
/// Todos os valores em R$ e sem arredondamento (I3): arredonda quem exibe.
/// </summary>
public sealed record LandedCost
{
    public required string Regime { get; init; }

    public required decimal EffectiveExchangeRate { get; init; }

    /// <summary>O que sai do meu bolso para o exterior, pelo câmbio efetivo.</summary>
    public required decimal PaidAbroad { get; init; }

    /// <summary>Valor aduaneiro pelo câmbio fiscal — base dos tributos.</summary>
    public required decimal CustomsValue { get; init; }

    public required decimal ImportDuty { get; init; }

    public required decimal Ipi { get; init; }

    public required decimal Pis { get; init; }

    public required decimal Cofins { get; init; }

    public required decimal CustomsFees { get; init; }

    public required decimal IcmsBase { get; init; }

    public required decimal Icms { get; init; }

    public required decimal DomesticFreight { get; init; }

    public required decimal ShipmentTotal { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitCost { get; init; }

    public required IReadOnlyList<CalculationLine> Memo { get; init; }

    /// <summary>Valor aduaneiro da remessa, em moeda estrangeira.</summary>
    public required decimal CustomsValueForeign { get; init; }

    /// <summary>
    /// A remessa passou do teto do regime. O número calculado não vale: acima do teto a
    /// importação é outra, com outras alíquotas. O card mostra o aviso em vez da margem (I6).
    /// </summary>
    public required bool ExceedsRegimeLimit { get; init; }

    public decimal TotalTaxes => ImportDuty + Ipi + Pis + Cofins + Icms;
}
