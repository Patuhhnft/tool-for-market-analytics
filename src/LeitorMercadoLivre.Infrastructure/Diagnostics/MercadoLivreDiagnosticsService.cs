using System.Text.RegularExpressions;
using System.Globalization;
using System.Diagnostics;
using System.Net.Http.Headers;
using LeitorMercadoLivre.Domain.Diagnostics;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.Diagnostics;

public sealed class MercadoLivreDiagnosticsService(
    HttpClient httpClient,
    IOptions<MercadoLivreDiagnosticsOptions> options) : IMercadoLivreDiagnosticsService
{
    private const int DetailMaxLength = 2500;

    private readonly MercadoLivreDiagnosticsOptions settings = options.Value;

    public async Task<MercadoLivreDiagnosticsReport> RunAsync(CancellationToken cancellationToken)
    {
        var endpoints = new List<EndpointDiagnosticResult>
        {
            await ProbeAsync("Tendências gerais", "/trends/MLB", cancellationToken),
            await ProbeTrendsByCategoryAsync(cancellationToken),
            await ProbeAsync("Highlights da categoria", BuildHighlightsPath(), cancellationToken),
            await ProbeAsync("Preços de anúncio", BuildListingPricesPath(), cancellationToken),
            await ProbeBulkItemsAsync(cancellationToken),
            await ProbeSingleItemAsync(cancellationToken),
            await ProbeItemAttributesAsync(cancellationToken),
            await ProbeVisitsAsync(cancellationToken),
            await ProbeVisitsTimeWindowAsync(cancellationToken),
            await ProbeProductItemsAsync(cancellationToken)
        };

        return new MercadoLivreDiagnosticsReport(
            DateTimeOffset.UtcNow,
            !string.IsNullOrWhiteSpace(settings.AccessToken),
            endpoints);
    }

    private async Task<EndpointDiagnosticResult> ProbeAsync(
        string name,
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return Skipped(name, path, "Token ausente. Configure MELI_ACCESS_TOKEN ou MercadoLivre:AccessToken.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stopwatch.Stop();
            var detail = await ReadDetailAsync(response, cancellationToken);

            return new EndpointDiagnosticResult(
                name,
                request.Method.Method,
                path,
                true,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                response.IsSuccessStatusCode,
                detail);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            return new EndpointDiagnosticResult(name, request.Method.Method, path, true, null,
                stopwatch.ElapsedMilliseconds, false, exception.Message);
        }
    }

    private async Task<EndpointDiagnosticResult> ProbeBulkItemsAsync(CancellationToken cancellationToken)
    {
        const string name = "Itens em lote";

        var path = string.IsNullOrWhiteSpace(settings.ItemIds)
            ? "/items/bulk?ids={configure-item-ids}"
            : $"/items/bulk?ids={Uri.EscapeDataString(settings.ItemIds)}";

        if (string.IsNullOrWhiteSpace(settings.ItemIds))
        {
            return Skipped(name, path, "Configure MercadoLivre:ItemIds para testar /items/bulk.");
        }

        var result = await ProbeAsync(name, path, cancellationToken);

        // /items/bulk devolve 200 no envelope mesmo quando cada item falha; o status real de
        // cada um vem DENTRO do corpo. Sem olhar aqui, o relatório dá falso positivo.
        if (!result.Accessible || result.Detail is null)
        {
            return result;
        }

        var itemStatuses = ExtractBulkStatuses(result.Detail);
        if (itemStatuses.Count == 0 || itemStatuses.Exists(status => status < 400))
        {
            return result;
        }

        return result with
        {
            Accessible = false,
            Detail = $"Envelope HTTP 200, mas todos os itens falharam ({string.Join(", ", itemStatuses)}). {result.Detail}"
        };
    }

    private static List<int> ExtractBulkStatuses(string detail) =>
        Regex.Matches(detail, "\"status_code\"\\s*:\\s*(\\d{3})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

    private string? FirstItemId => settings.ItemIds
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    /// <summary>Item avulso: separa "o bulk é que está bloqueado" de "o item é que é proibido".</summary>
    private Task<EndpointDiagnosticResult> ProbeSingleItemAsync(CancellationToken cancellationToken)
    {
        const string name = "Item avulso";
        var itemId = FirstItemId;

        var path = itemId is null
            ? "/items/{configure-item-ids}"
            : $"/items/{Uri.EscapeDataString(itemId)}";

        return itemId is null
            ? Task.FromResult(Skipped(name, path, "Configure MercadoLivre:ItemIds para testar /items/{id}."))
            : ProbeAsync(name, path, cancellationToken);
    }

    /// <summary>
    /// Último recurso para o dado que decide o §7: a API às vezes libera um subconjunto de
    /// atributos mesmo quando nega o item inteiro. É aqui que se descobre se sold_quantity
    /// é alcançável para anúncio de terceiro.
    /// </summary>
    private Task<EndpointDiagnosticResult> ProbeItemAttributesAsync(CancellationToken cancellationToken)
    {
        const string name = "Item por atributos (sold_quantity)";
        const string attributes = "?attributes=id,price,sold_quantity,available_quantity";
        var itemId = FirstItemId;

        var path = itemId is null
            ? $"/items/{{configure-item-ids}}{attributes}"
            : $"/items/{Uri.EscapeDataString(itemId)}{attributes}";

        return itemId is null
            ? Task.FromResult(Skipped(name, path, "Configure MercadoLivre:ItemIds para testar o subconjunto de atributos."))
            : ProbeAsync(name, path, cancellationToken);
    }

    /// <summary>
    /// Com sold_quantity bloqueado, visita é o melhor proxy de demanda que resta antes de
    /// cair na posição do ranking: é número contínuo e por anúncio, não ordinal.
    /// </summary>
    private Task<EndpointDiagnosticResult> ProbeVisitsAsync(CancellationToken cancellationToken)
    {
        const string name = "Visitas por item";
        var itemId = FirstItemId;

        var path = itemId is null
            ? "/visits/items?ids={configure-item-ids}"
            : $"/visits/items?ids={Uri.EscapeDataString(itemId)}";

        return itemId is null
            ? Task.FromResult(Skipped(name, path, "Configure MercadoLivre:ItemIds para testar /visits/items."))
            : ProbeAsync(name, path, cancellationToken);
    }

    /// <summary>
    /// Série de visitas por dia. Se este responder, existe curva de demanda de verdade —
    /// com delta, aceleração e persistência, como o §7 pede — sem depender de venda.
    /// </summary>
    private Task<EndpointDiagnosticResult> ProbeVisitsTimeWindowAsync(CancellationToken cancellationToken)
    {
        const string name = "Visitas em janela (7 dias)";
        const string window = "/visits/time_window?last=7&unit=day";
        var itemId = FirstItemId;

        var path = itemId is null
            ? $"/items/{{configure-item-ids}}{window}"
            : $"/items/{Uri.EscapeDataString(itemId)}{window}";

        return itemId is null
            ? Task.FromResult(Skipped(name, path, "Configure MercadoLivre:ItemIds para testar a série de visitas."))
            : ProbeAsync(name, path, cancellationToken);
    }

    private Task<EndpointDiagnosticResult> ProbeProductItemsAsync(CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(settings.ProductId)
            ? "/products/{configure-product-id}/items"
            : $"/products/{Uri.EscapeDataString(settings.ProductId)}/items";

        return string.IsNullOrWhiteSpace(settings.ProductId)
            ? Task.FromResult(Skipped("Itens do produto", path, "Configure MercadoLivre:ProductId para testar /products/{id}/items."))
            : ProbeAsync("Itens do produto", path, cancellationToken);
    }

    /// <summary>
    /// /trends/MLB devolveu 404 "Not found public trends". Esta sonda testa a variante por
    /// categoria para separar as hipóteses: se aqui responder, o recurso existe e só não há
    /// ranking geral público; se aqui também der 404, o endpoint caiu para o nosso token.
    /// </summary>
    private Task<EndpointDiagnosticResult> ProbeTrendsByCategoryAsync(CancellationToken cancellationToken)
    {
        const string name = "Tendências por categoria";

        var path = string.IsNullOrWhiteSpace(settings.CategoryId)
            ? "/trends/MLB/{configure-category-id}"
            : $"/trends/MLB/{Uri.EscapeDataString(settings.CategoryId)}";

        return string.IsNullOrWhiteSpace(settings.CategoryId)
            ? Task.FromResult(Skipped(name, path, "Configure MercadoLivre:CategoryId para testar /trends/MLB/{categoria}."))
            : ProbeAsync(name, path, cancellationToken);
    }

    // O formato correto é /highlights/{site}/category/{id}; a querystring ?category= devolve 404.
    private string BuildHighlightsPath() => string.IsNullOrWhiteSpace(settings.CategoryId)
        ? "/highlights/MLB/category/{configure-category-id}"
        : $"/highlights/MLB/category/{Uri.EscapeDataString(settings.CategoryId)}";

    // listing_prices exige o parâmetro 'price'; sem ele a API responde 400. O valor é só uma
    // amostra para descobrir a tabela de comissão — a comissão real de cada produto é
    // consultada com o preço de mercado dele.
    private string BuildListingPricesPath() =>
        $"/sites/MLB/listing_prices?price={settings.ListingPriceSample.ToString(CultureInfo.InvariantCulture)}";

    private static EndpointDiagnosticResult Skipped(string name, string path, string detail) =>
        new(name, HttpMethod.Get.Method, path, false, null, null, false, detail);

    private static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return content.Length <= DetailMaxLength ? content : content[..DetailMaxLength] + "...";
    }
}