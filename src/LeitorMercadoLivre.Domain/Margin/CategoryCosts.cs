namespace LeitorMercadoLivre.Domain.Margin;

/// <summary>
/// Custos por unidade que dependem do que o produto é, não do preço dele.
/// <para>
/// Dois custos que a margem ignorava e que decidem se produto barato vale a pena:
/// <b>embalagem</b> (que em item de R$ 20 pesa tanto quanto a comissão) e <b>reserva de
/// quebra</b> (vidro quebra, eletrônico volta, roupa é trocada — e o que quebra você pagou
/// e não vendeu).
/// </para>
/// </summary>
/// <param name="PackagingPerUnit">Custo de embalar uma unidade, em R$.</param>
/// <param name="BreakageReserve">
/// Fração das unidades que não vira venda: quebra, extravio, devolução.
/// </param>
public sealed record CategoryCost(decimal PackagingPerUnit, decimal BreakageReserve)
{
    public static readonly CategoryCost None = new(0m, 0m);

    /// <summary>
    /// O custo real de UMA unidade vendida, dada a quebra.
    /// <para>
    /// Se 5% quebram, cada 100 unidades compradas viram 95 vendidas — então o custo de cada
    /// venda é o de <c>1 / 0,95</c> unidades, não o de uma. É a mesma lógica do imposto "por
    /// dentro": dividir, não multiplicar. Multiplicar por 1,05 subestimaria a perda.
    /// </para>
    /// </summary>
    public decimal ApplyBreakage(decimal unitCost)
    {
        if (BreakageReserve <= 0m) return unitCost;

        // Quebra de 100% não tem custo definido: não sobra unidade para vender.
        if (BreakageReserve >= 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BreakageReserve), "Reserva de quebra de 100% não deixa unidade para vender.");
        }

        return unitCost / (1m - BreakageReserve);
    }

    public void Validate()
    {
        if (PackagingPerUnit < 0) throw new ArgumentException("Embalagem por unidade não pode ser negativa.");
        if (BreakageReserve < 0 || BreakageReserve >= 1)
        {
            throw new ArgumentException("Reserva de quebra é uma fração e deve ficar em [0, 1).");
        }
    }
}

/// <summary>Os custos de cada categoria, com o padrão para quem não tem regra.</summary>
public sealed class CategoryCostTable(IReadOnlyDictionary<string, CategoryCost> costs)
{
    /// <summary>
    /// Categoria sem regra devolve zero — e zero aqui significa "não estimamos", não "não
    /// existe". Melhor uma margem sem o custo do que uma margem com um custo inventado.
    /// </summary>
    public CategoryCost For(string? categoryId) =>
        categoryId is not null && costs.TryGetValue(categoryId, out var cost) ? cost : CategoryCost.None;
}
