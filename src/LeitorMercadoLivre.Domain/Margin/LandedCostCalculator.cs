namespace LeitorMercadoLivre.Domain.Margin;

/// <summary>
/// Quanto custa pôr uma unidade na minha mão. Puro, sem I/O e sem relógio.
/// </summary>
public static class LandedCostCalculator
{
    public static LandedCost Calculate(AcquisitionInput input, decimal exchangeSpread)
    {
        Validate(input, exchangeSpread);

        var taxes = input.Taxes;
        var cifForeign = (input.SupplierUnitPrice * input.Quantity)
            + input.InternationalFreightTotal
            + input.InsuranceTotal;

        // Dois câmbios, de propósito. O que eu PAGO no exterior sai pelo câmbio efetivo
        // (PTAX + IOF + spread do meio de pagamento). A BASE dos tributos é o valor aduaneiro
        // pelo câmbio fiscal. Usar o efetivo na base inflaria o imposto; usar a PTAX pura no
        // pagamento esconderia 4 a 6% do custo — e esse segundo erro é o perigoso, porque
        // infla a margem exibida.
        var effectiveRate = input.FiscalExchangeRate * (1 + exchangeSpread);
        var paidAbroad = cifForeign * effectiveRate;
        var customsValue = cifForeign * input.FiscalExchangeRate;

        // A faixa é escolhida pelo valor aduaneiro em moeda estrangeira, como a lei define.
        var bracket = taxes.BracketFor(cifForeign);
        var deduction = bracket.DeductionForeign * input.FiscalExchangeRate;
        var importDuty = Math.Max(0m, (customsValue * bracket.Rate) - deduction);
        var ipi = (customsValue + importDuty) * taxes.Ipi;
        var pis = customsValue * taxes.Pis;
        var cofins = customsValue * taxes.Cofins;

        // ICMS "por dentro": o imposto integra a própria base. Multiplicar o valor direto
        // pela alíquota subestima o ICMS — com 18%, a diferença passa de 20% do imposto.
        var valueBeforeIcms = customsValue + importDuty + ipi + pis + cofins + input.CustomsFeesTotal;
        var icmsBase = valueBeforeIcms / (1 - taxes.Icms);
        var icms = icmsBase * taxes.Icms;

        var shipmentTotal = paidAbroad
            + importDuty + ipi + pis + cofins + icms
            + input.CustomsFeesTotal
            + input.DomesticFreightTotal;

        var unitCost = shipmentTotal / input.Quantity;

        return new LandedCost
        {
            Regime = taxes.Regime,
            CustomsValueForeign = cifForeign,
            ExceedsRegimeLimit = taxes.MaxShipmentValueForeign is { } limit && cifForeign > limit,
            EffectiveExchangeRate = effectiveRate,
            PaidAbroad = paidAbroad,
            CustomsValue = customsValue,
            ImportDuty = importDuty,
            Ipi = ipi,
            Pis = pis,
            Cofins = cofins,
            CustomsFees = input.CustomsFeesTotal,
            IcmsBase = icmsBase,
            Icms = icms,
            DomesticFreight = input.DomesticFreightTotal,
            ShipmentTotal = shipmentTotal,
            Quantity = input.Quantity,
            UnitCost = unitCost,
            Memo =
            [
                new("Pago no exterior", paidAbroad, "CIF × PTAX × (1 + spread)"),
                new("Valor aduaneiro", customsValue, "CIF × câmbio fiscal"),
                new("II", importDuty, $"máx(0; VA × {bracket.Rate:P2} − {bracket.DeductionForeign} × câmbio fiscal) · faixa até {(bracket.UpToForeign?.ToString() ?? "sem teto")}"),
                new("IPI", ipi, "(VA + II) × IPI"),
                new("PIS-Importação", pis, "VA × PIS"),
                new("Cofins-Importação", cofins, "VA × Cofins"),
                new("Despesas aduaneiras", input.CustomsFeesTotal, "informado"),
                new("Base do ICMS", icmsBase, "(VA + II + IPI + PIS + Cofins + despesas) ÷ (1 − ICMS)"),
                new("ICMS", icms, "base do ICMS × ICMS"),
                new("Frete nacional", input.DomesticFreightTotal, "informado"),
                new("Total da remessa", shipmentTotal, "soma"),
                new("Custo por unidade", unitCost, $"total ÷ {input.Quantity}")
            ]
        };
    }

    /// <summary>
    /// O maior preço unitário do fornecedor, em moeda estrangeira, cujo custo desembarcado
    /// ainda cabe em <paramref name="targetUnitCost"/>. É o número acionável de "quanto
    /// posso pagar", com toda a cadeia de câmbio e tributo embutida.
    /// <para>
    /// Resolvido por bisseção porque o custo é linear por partes no preço (o piso zero do II
    /// quebra a reta) e sempre crescente — bisseção acerta em qualquer regime sem que cada
    /// mudança de regra exija reescrever uma inversão algébrica.
    /// </para>
    /// </summary>
    /// <returns><c>null</c> quando nem de graça fecha: frete e tributo já estouram o alvo.</returns>
    public static decimal? MaxSupplierUnitPrice(AcquisitionInput input, decimal targetUnitCost, decimal exchangeSpread)
    {
        decimal CostAt(decimal price) =>
            Calculate(input with { SupplierUnitPrice = price }, exchangeSpread).UnitCost;

        if (CostAt(0m) > targetUnitCost)
        {
            return null;
        }

        var low = 0m;
        var high = Math.Max(1m, input.SupplierUnitPrice);

        for (var attempt = 0; attempt < 64 && CostAt(high) <= targetUnitCost; attempt++)
        {
            low = high;
            high *= 2;
        }

        for (var step = 0; step < 100 && high - low > 0.000001m; step++)
        {
            var middle = (low + high) / 2;

            if (CostAt(middle) <= targetUnitCost)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        // O limite inferior é sempre um preço cujo custo cabe no alvo: errar para baixo é
        // errar a favor da margem.
        return low;
    }

    private static void Validate(AcquisitionInput input, decimal exchangeSpread)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Taxes);
        input.Taxes.Validate();

        if (input.Quantity < 1) throw new ArgumentException("A remessa precisa ter pelo menos uma unidade.", nameof(input));
        if (input.SupplierUnitPrice < 0) throw new ArgumentException("Preço do fornecedor negativo.", nameof(input));
        if (input.FiscalExchangeRate <= 0) throw new ArgumentException("Câmbio fiscal precisa ser positivo.", nameof(input));
        if (input.InternationalFreightTotal < 0 || input.DomesticFreightTotal < 0 ||
            input.CustomsFeesTotal < 0 || input.InsuranceTotal < 0)
        {
            throw new ArgumentException("Frete, seguro e despesas não podem ser negativos.", nameof(input));
        }

        if (exchangeSpread < 0) throw new ArgumentException("Spread cambial não pode ser negativo.", nameof(exchangeSpread));
    }
}
