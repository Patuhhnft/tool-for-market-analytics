namespace LeitorMercadoLivre.Infrastructure.Diagnostics;

public sealed class MercadoLivreDiagnosticsOptions
{
    public const string SectionName = "MercadoLivre";

    public string BaseUrl { get; set; } = "https://api.mercadolibre.com";
    public string? AccessToken { get; set; }
    public string? ProductId { get; set; }
    public string? ItemIds { get; set; }
    public string? CategoryId { get; set; }

    /// <summary>Preço de amostra usado para consultar a tabela de comissão em listing_prices.</summary>
    public decimal ListingPriceSample { get; set; } = 100m;
}