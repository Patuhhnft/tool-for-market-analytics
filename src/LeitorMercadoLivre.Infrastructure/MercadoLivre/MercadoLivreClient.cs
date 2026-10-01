using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeitorMercadoLivre.Domain.Demand;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.MercadoLivre;

public sealed record HighlightEntry(string Id, int Position, string Type)
{
    /// <summary>
    /// Só PRODUCT é âncora de catálogo (I5). USER_PRODUCT também aparece no ranking — no
    /// diagnóstico, a 20ª posição de MLB1051 era um — mas não serve para /products/{id}/items.
    /// </summary>
    public bool IsCatalogProduct => Type == "PRODUCT";
}

public sealed record ShippingInfo(bool FreeShipping, string? LogisticType, string? Mode);

public sealed record CategorySummary(string Id, string Name);

public sealed record ProductListing(
    string ItemId,
    long SellerId,
    decimal Price,
    string CategoryId,
    string Condition,
    string ListingTypeId,
    long? OfficialStoreId,
    ShippingInfo? Shipping,
    int MinPurchaseUnit = 1);

public sealed record SaleFeeDetails(decimal FixedFee, decimal GrossAmount, decimal PercentageFee);

public sealed record ListingPrice(string ListingTypeId, string ListingTypeName, decimal SaleFeeAmount, SaleFeeDetails SaleFeeDetails)
{
    /// <summary>A API devolve 16 para 16%. Aqui vira fração, que é o que o domínio usa.</summary>
    public decimal PercentageRate => SaleFeeDetails.PercentageFee / 100m;
}

/// <summary>
/// Cliente dos endpoints que o diagnóstico de 24/09/2026 confirmou acessíveis. Os que
/// devolveram 403/404 (/items, /trends) não estão aqui de propósito.
/// </summary>
public sealed class MercadoLivreClient(HttpClient http, IOptions<MercadoLivreApiOptions> options)
{
    private const int PageSize = 100;
    // Um id por chamada. O endpoint aceita o parametro "ids" no plural, mas responde
    // "maximum amount of items to query is 1" para qualquer lote maior (medido em 25/09/2026).
    // A divisao ao meio abaixo fica como rede de seguranca, caso o limite volte a mudar.
    private const int VisitsBatchSize = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private string Site => options.Value.SiteId;

    public async Task<IReadOnlyList<HighlightEntry>> GetHighlightsAsync(string categoryId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<HighlightsResponse>($"/highlights/{Site}/category/{Escape(categoryId)}", cancellationToken);
        return response?.Content ?? [];
    }

    /// <summary>
    /// Categorias de topo do site, com nome. Uma chamada por ciclo: a lista muda muito devagar,
    /// e sem ela o painel só teria o código MLB1051 para mostrar.
    /// </summary>
    public async Task<IReadOnlyList<CategorySummary>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await GetAsync<List<CategorySummary>>($"/sites/{Site}/categories", cancellationToken) ?? [];

    /// <summary>Nome do produto. <c>null</c> se o endpoint recusar: o card mostra o ID.</summary>
    public async Task<string?> GetProductNameAsync(string productId, CancellationToken cancellationToken)
    {
        var product = await GetAsync<ProductResponse>($"/products/{Escape(productId)}", cancellationToken, allowForbidden: true);
        return product?.Name;
    }

    /// <summary>Todos os anúncios do produto de catálogo, paginando até o fim.</summary>
    public async Task<IReadOnlyList<ProductListing>> GetProductListingsAsync(string productId, CancellationToken cancellationToken)
    {
        var listings = new List<ProductListing>();

        for (var offset = 0; ; offset += PageSize)
        {
            var page = await GetAsync<ProductItemsResponse>(
                $"/products/{Escape(productId)}/items?limit={PageSize}&offset={offset}", cancellationToken);

            if (page is null || page.Results.Count == 0) break;

            listings.AddRange(page.Results);
            if (listings.Count >= page.Paging.Total) break;
        }

        return listings;
    }

    /// <summary>Visitas acumuladas por anúncio, em lotes — a varredura barata.</summary>
    public async Task<IReadOnlyDictionary<string, int>> GetTotalVisitsAsync(IEnumerable<string> itemIds, CancellationToken cancellationToken)
    {
        var totals = new Dictionary<string, int>();

        foreach (var batch in itemIds.Distinct().Chunk(VisitsBatchSize))
        {
            await CollectVisitsAsync(batch, totals, cancellationToken);
        }

        return totals;
    }

    /// <summary>
    /// Em 400, o lote é dividido ao meio até passar — serve tanto para limite de tamanho quanto
    /// para um id específico que a API recuse. Um id sozinho que ainda falhe fica sem total.
    /// </summary>
    private async Task CollectVisitsAsync(string[] batch, Dictionary<string, int> totals, CancellationToken cancellationToken)
    {
        try
        {
            var ids = string.Join(',', batch.Select(Escape));
            var response = await GetAsync<Dictionary<string, int>>($"/visits/items?ids={ids}", cancellationToken);
            foreach (var (item, visits) in response ?? []) totals[item] = visits;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.BadRequest && batch.Length > 1)
        {
            var half = batch.Length / 2;
            await CollectVisitsAsync(batch[..half], totals, cancellationToken);
            await CollectVisitsAsync(batch[half..], totals, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.BadRequest)
        {
            // Id isolado recusado: fica sem total, e o produto dele cai no desempate seguinte.
        }
    }

    /// <summary>
    /// Série diária de visitas. Devolve só os dias que a API mandou — dias sem visita vêm
    /// omitidos, e completar a janela com zero é trabalho de <see cref="DemandCalculator.NormalizeSeries"/>.
    /// </summary>
    public async Task<IReadOnlyList<DailyCount>> GetDailyVisitsAsync(string itemId, int days, CancellationToken cancellationToken)
    {
        var response = await GetAsync<VisitWindowResponse>(
            $"/items/{Escape(itemId)}/visits/time_window?last={days}&unit=day", cancellationToken);

        return
        [
            .. (response?.Results ?? []).Select(day =>
                new DailyCount(DateOnly.FromDateTime(day.Date.UtcDateTime), day.Total))
        ];
    }

    /// <summary>Tarifas de venda por tipo de anúncio, num preço e categoria.</summary>
    public async Task<IReadOnlyList<ListingPrice>> GetListingPricesAsync(decimal price, string? categoryId, CancellationToken cancellationToken)
    {
        var query = $"price={price.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(categoryId)) query += $"&category_id={Escape(categoryId)}";

        return await GetAsync<List<ListingPrice>>($"/sites/{Site}/listing_prices?{query}", cancellationToken) ?? [];
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken, bool allowForbidden = false)
    {
        using var response = await http.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        if (allowForbidden && response.StatusCode == HttpStatusCode.Forbidden) return default;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Mercado Livre respondeu HTTP {(int)response.StatusCode} em {path}: {Truncate(body)}",
                inner: null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "...";

    private sealed record HighlightsResponse(List<HighlightEntry> Content);

    private sealed record ProductResponse(string Id, string? Name);

    private sealed record ProductItemsResponse(PagingInfo Paging, List<ProductListing> Results);

    private sealed record PagingInfo(int Total, int Offset, int Limit);

    private sealed record VisitWindowResponse(string ItemId, int TotalVisits, List<VisitDay> Results);

    private sealed record VisitDay(DateTimeOffset Date, int Total);
}
