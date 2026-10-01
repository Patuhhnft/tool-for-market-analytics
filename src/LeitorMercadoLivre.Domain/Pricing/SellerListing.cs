namespace LeitorMercadoLivre.Domain.Pricing;

/// <summary>
/// Um anúncio como veio da API, antes da normalização da Etapa 0.
/// </summary>
/// <param name="ProductAnchorId">
/// Âncora de catálogo do anúncio. Sem ela não existe amostra confiável: uma lista
/// montada por semelhança de título mistura produtos diferentes e a moda resultante
/// não significa nada.
/// </param>
/// <param name="UnitsPerPack">
/// Unidades no kit/pacote. O preço entra na amostra dividido por este número.
/// </param>
/// <param name="IsOfficialStore">
/// O anúncio é de uma loja oficial (a própria marca, ou distribuidor autorizado).
/// <para>
/// Não é detalhe de exibição: contra a marca dona do produto o revendedor não compete em
/// preço — ela compra de si mesma. Tratar a loja oficial como mais um concorrente derruba o
/// preço de referência e faz a margem parecer pior do que é para quem revende.
/// </para>
/// </param>
public sealed record SellerListing(
    string SellerId,
    string ListingId,
    decimal Price,
    string? ProductAnchorId = null,
    int UnitsPerPack = 1,
    bool IsNew = true,
    bool IsActive = true,
    bool HasStock = true,
    bool IsOfficialStore = false);
