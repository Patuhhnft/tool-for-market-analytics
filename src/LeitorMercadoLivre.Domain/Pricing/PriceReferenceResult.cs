namespace LeitorMercadoLivre.Domain.Pricing;

public enum PriceStatus
{
    /// <summary>Preço calculado; <see cref="PriceReferenceResult.MarketPrice"/> é utilizável.</summary>
    Calculated,

    /// <summary>Menos vendedores que o mínimo. Não exibir preço nem margem, só o aviso.</summary>
    InsufficientData,

    /// <summary>Sem âncora de catálogo: a amostra não é comprovadamente do mesmo produto.</summary>
    NoProductAnchor
}

public enum PriceSource
{
    Mode,
    Median
}

public enum PriceConfidence
{
    Low,
    Medium,
    High
}

[Flags]
public enum PriceFlags
{
    None = 0,
    SmallSample = 1,
    DispersedMarket = 2,

    /// <summary>
    /// A própria marca ocupa boa parte da prateleira do produto.
    /// <para>
    /// Medido em 28/09/2026 no caso que motivou isto (potes Rishon): a loja oficial tinha
    /// <b>10 dos 19 anúncios</b>, mas era <b>1 de 10 vendedores</b> — e nem era a mais barata
    /// (R$ 97,62 contra R$ 93,87 do revendedor mais barato). Ou seja, o que atrapalha ali não
    /// é preço: é a marca ocupar metade do que o comprador vê.
    /// </para>
    /// <para>
    /// Por isso o selo mede PRESENÇA, não preço. A primeira versão marcava quando a marca
    /// estava no preço de mercado ou abaixo, e acendeu em 28 de 36 produtos — um aviso que
    /// dispara em 78% dos casos não avisa nada.
    /// </para>
    /// <para>
    /// O produto não é excluído: continua no quadro, marcado, para o usuário decidir.
    /// </para>
    /// </summary>
    BrandDominated = 4
}

public enum DiscardReason
{
    NotNew,
    Inactive,
    NoStock,
    DifferentProduct,
    InvalidPack,

    /// <summary>
    /// Anúncio mais caro de um vendedor que já tem outro na amostra. É este descarte que
    /// impede um vendedor com vários anúncios de fabricar uma moda sozinho.
    /// </summary>
    DuplicateSeller
}

public sealed record DiscardedListing(string ListingId, string SellerId, DiscardReason Reason);

/// <summary>Valores do ciclo anterior, para as variações semanais.</summary>
public sealed record PriceReferenceHistory(decimal? MarketPrice, decimal? ModeStrength);

/// <summary>
/// Saída completa do modelo. Tudo aqui é persistido no snapshot: sem as entradas, os
/// intermediários e o hash da amostra não dá para reproduzir o número meses depois.
/// </summary>
public sealed record PriceReferenceResult
{
    public required PriceStatus Status { get; init; }

    public required int SampleSize { get; init; }

    public required int SellersFound { get; init; }

    /// <summary>
    /// Quantos dos vendedores da amostra são loja oficial. Zero na maioria dos produtos.
    /// </summary>
    public int OfficialStoreSellers { get; init; }

    /// <summary>
    /// Fração dos ANÚNCIOS que são de loja oficial, antes da deduplicação por vendedor.
    /// <para>
    /// Conta anúncio, não vendedor, de propósito: a marca costuma ser um vendedor só com
    /// muitos anúncios, e é a prateleira que o comprador vê. Nos potes Rishon dava 1 vendedor
    /// de 10, mas 10 anúncios de 19.
    /// </para>
    /// </summary>
    public decimal OfficialStoreShare { get; init; }

    /// <summary>
    /// O menor preço praticado por uma loja oficial, quando existe. É o piso contra o qual o
    /// revendedor teria de brigar — e é ele que explica uma margem que não fecha.
    /// </summary>
    public decimal? OfficialStorePrice { get; init; }

    public required IReadOnlyList<DiscardedListing> Discarded { get; init; }

    public required string SampleHash { get; init; }

    public PriceFlags Flags { get; init; } = PriceFlags.None;

    public decimal? MarketPrice { get; init; }

    public PriceSource? Source { get; init; }

    public decimal? Mode { get; init; }

    public decimal? Median { get; init; }

    /// <summary>Fração de vendedores da lista limpa no balde modal.</summary>
    public decimal? ModeStrength { get; init; }

    /// <summary>Contagem absoluta de vendedores no balde modal — o que a tela exibe ao lado do percentual.</summary>
    public int ModalBucketSellers { get; init; }

    public decimal? CleanRangeLow { get; init; }

    public decimal? CleanRangeHigh { get; init; }

    public decimal? SuggestedShowcasePrice { get; init; }

    public decimal? DispersionIndex { get; init; }

    public PriceConfidence? Confidence { get; init; }

    public decimal? WeeklyMarketPriceChange { get; init; }

    public decimal? WeeklyModeStrengthChange { get; init; }

    public bool HasFlag(PriceFlags flag) => (Flags & flag) == flag;
}
