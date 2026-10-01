using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.MercadoLivre;

/// <summary>Um produto de catálogo encontrado na busca.</summary>
public sealed record CatalogHit(string Id, string? Name, string? DomainId, string? Thumbnail);

/// <summary>Quando não dá para buscar agora, e por quê — em vez de uma lista vazia mentirosa.</summary>
public sealed class CatalogUnavailableException(string message) : Exception(message);

/// <summary>
/// Busca de produto no catálogo do Mercado Livre, para a API.
/// <para>
/// <b>Lê o token, nunca o renova.</b> Essa é a regra que justifica esta classe existir separada
/// do <see cref="MercadoLivreClient"/>: o refresh token é de USO ÚNICO, e o semáforo que
/// serializa a renovação vive dentro de um processo. Se a API e o Worker renovassem ao mesmo
/// tempo, o segundo a chegar receberia um token já invalidado e a coleta morreria.
/// </para>
/// <para>
/// Por isso, quando o token guardado está vencido, aqui não se renova: avisa-se que o Worker
/// precisa estar rodando. O Worker mantém o token fresco a cada ciclo.
/// </para>
/// </summary>
public sealed class CatalogSearchClient(
    HttpClient http,
    ICredentialStore store,
    IOptions<MercadoLivreApiOptions> options,
    TimeProvider clock)
{
    private const string Site = "MLB";

    /// <summary>Acima disso a lista deixa de ajudar a escolher.</summary>
    public const int MaxResults = 10;

    public async Task<IReadOnlyList<CatalogHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term)) return [];

        var token = await ReadTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            // Pede o dobro porque o filtro de inativos derruba boa parte: numa consulta genérica
            // chegou a derrubar os dez.
            $"/products/search?site_id={Site}&q={Uri.EscapeDataString(term)}&limit={MaxResults * 2}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // NÃO renovar aqui: é exatamente o que derrubaria a coleta.
            throw new CatalogUnavailableException(
                "O Mercado Livre recusou o token guardado. O Worker precisa estar rodando para renová-lo.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CatalogUnavailableException($"O Mercado Livre respondeu HTTP {(int)response.StatusCode} na busca de catálogo.");
        }

        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken);

        return
        [
            .. (payload?.Results ?? [])
                .Where(result => !string.IsNullOrEmpty(result.Id))
                // Produto inativo é beco sem saída: medido em 26/09/2026, /products/{id}/items
                // responde 404 nele E no filho dele. Oferecê-lo seria prometer uma análise que
                // termina em erro. Consulta genérica ("mouse gamer") volta quase só inativo;
                // consulta específica volta ativo — por isso filtrar, e não só avisar.
                .Where(result => string.Equals(result.Status, "active", StringComparison.OrdinalIgnoreCase))
                .Select(result => new CatalogHit(result.Id!, result.Name, result.DomainId, result.Pictures?.FirstOrDefault()?.Url))
                .Take(MaxResults)
        ];
    }

    private async Task<string> ReadTokenAsync(CancellationToken cancellationToken)
    {
        var credential = await store.LoadAsync(cancellationToken)
            ?? throw new CatalogUnavailableException(
                "Não há credencial do Mercado Livre guardada. Rode o Worker uma vez para autorizar.");

        // Margem de segurança: um token prestes a vencer falharia no meio da busca.
        if (credential.ExpiresAt <= clock.GetUtcNow() + options.Value.RefreshMargin)
        {
            throw new CatalogUnavailableException(
                "O token do Mercado Livre está vencendo e quem renova é o Worker. Deixe-o rodando e tente de novo em instantes.");
        }

        return credential.AccessToken;
    }

    private sealed record SearchResponse([property: JsonPropertyName("results")] List<SearchResult>? Results);

    private sealed record SearchResult(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("domain_id")] string? DomainId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("pictures")] List<Picture>? Pictures);

    private sealed record Picture([property: JsonPropertyName("url")] string? Url);
}
