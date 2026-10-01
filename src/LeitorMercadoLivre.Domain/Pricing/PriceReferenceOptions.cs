namespace LeitorMercadoLivre.Domain.Pricing;

/// <summary>
/// Parâmetros do modelo de preço de mercado. Nenhum deles pode ficar fixo no código:
/// todos vêm de configuração (seção <see cref="SectionName"/>).
/// </summary>
public sealed class PriceReferenceOptions
{
    public const string SectionName = "PriceReference";

    /// <summary>Até este valor o balde é de R$ 1; acima dele, de <see cref="CoarseBucketStep"/>.</summary>
    public decimal WholeBucketLimit { get; init; } = 200m;

    public decimal CoarseBucketStep { get; init; } = 5m;

    /// <summary>Fração descartada em CADA ponta antes de procurar a moda.</summary>
    public decimal TrimRatio { get; init; } = 0.15m;

    public int MinSellersForTrim { get; init; } = 7;

    public int MinSellersToCalculate { get; init; } = 3;

    /// <summary>Fração mínima de vendedores no balde modal para a moda valer.</summary>
    public decimal MinModeStrength { get; init; } = 0.20m;

    /// <summary>
    /// Piso ABSOLUTO de vendedores no balde modal. Percentual sozinho mente em amostra
    /// pequena: 20% de 7 vendedores são 2 pessoas, e duas pessoas cobrando o mesmo valor
    /// são coincidência, não preço de mercado.
    /// </summary>
    public int MinModalBucketSellers { get; init; } = 3;

    public decimal ShowcaseDiscount { get; init; } = 0.10m;

    public int HighConfidenceMinSellers { get; init; } = 15;

    public decimal HighConfidenceMaxDispersion { get; init; } = 0.25m;

    public int LowConfidenceSellerThreshold { get; init; } = 7;

    public decimal LowConfidenceDispersionThreshold { get; init; } = 0.50m;

    /// <summary>
    /// Fração dos anúncios que a loja oficial precisa ocupar para o produto contar como
    /// dominado pela marca. Padrão: um terço da prateleira.
    /// <para>
    /// Calibrável de propósito: um limiar baixo demais faz o selo acender em quase tudo e
    /// virar ruído — foi o que aconteceu com a primeira versão, que media preço em vez de
    /// presença e acendeu em 28 de 36 produtos.
    /// </para>
    /// </summary>
    public decimal BrandDominationShare { get; init; } = 1m / 3m;

    /// <summary>
    /// Falha cedo e alto: um parâmetro absurdo vindo do appsettings tem que derrubar o
    /// ciclo, não produzir um preço silenciosamente errado.
    /// </summary>
    public void Validate()
    {
        if (WholeBucketLimit <= 0) Fail(nameof(WholeBucketLimit), "deve ser maior que zero.");
        if (CoarseBucketStep <= 0) Fail(nameof(CoarseBucketStep), "deve ser maior que zero.");
        if (TrimRatio < 0 || TrimRatio >= 0.5m) Fail(nameof(TrimRatio), "deve ficar em [0, 0,5): cortar metade de cada lado não deixa amostra.");
        if (MinSellersToCalculate < 1) Fail(nameof(MinSellersToCalculate), "deve ser pelo menos 1.");
        if (MinSellersForTrim < MinSellersToCalculate) Fail(nameof(MinSellersForTrim), "não pode ser menor que MinSellersToCalculate.");
        if (MinModeStrength < 0 || MinModeStrength > 1) Fail(nameof(MinModeStrength), "é uma fração e deve ficar em [0, 1].");
        if (MinModalBucketSellers < 1) Fail(nameof(MinModalBucketSellers), "deve ser pelo menos 1.");
        if (ShowcaseDiscount < 0) Fail(nameof(ShowcaseDiscount), "não pode ser negativo.");
        if (BrandDominationShare <= 0 || BrandDominationShare > 1) Fail(nameof(BrandDominationShare), "é uma fração e deve ficar em (0, 1].");
    }

    private static void Fail(string parameter, string reason) =>
        throw new ArgumentException($"PriceReference:{parameter} {reason}", parameter);
}
