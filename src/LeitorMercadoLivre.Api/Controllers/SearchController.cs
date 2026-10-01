using Hangfire;
using LeitorMercadoLivre.Api.Security;
using LeitorMercadoLivre.Infrastructure.MercadoLivre;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using LeitorMercadoLivre.Infrastructure.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Api.Controllers;

/// <summary>Um candidato do catálogo do Mercado Livre, ainda não analisado por nós.</summary>
public sealed record CatalogCandidate(string Id, string? Name, string? DomainId, string? Thumbnail, bool AlreadyKnown);

public sealed record CatalogSearchResult(IReadOnlyList<CatalogCandidate> Items, string? Unavailable);

public sealed record AnalysisQueued(string ProductId, string JobId, string Message);

/// <summary>
/// A barra de pesquisa da área "Explorar". Duas fontes, com custos muito diferentes:
/// <list type="bullet">
/// <item><b>Histórico</b> — SQL sobre o que já coletamos. Zero chamada, responde a cada tecla.</item>
/// <item><b>Catálogo</b> — o Mercado Livre. Custa chamada, então só sob pedido explícito.</item>
/// </list>
/// <para>
/// Colar link ou ID de anúncio (MLB de item) <b>não funciona</b> e não há contorno: medido em
/// 26/09/2026, <c>/items/{id}</c> responde 403 e <c>/items/bulk</c> devolve 200 no envelope com
/// 403 dentro de cada item. Buscar o ID do anúncio no catálogo devolve zero. Quem quiser
/// analisar um anúncio precisa colar o TÍTULO dele.
/// </para>
/// </summary>
[ApiController]
[Route("api/busca")]
public sealed class SearchController(
    IDbContextFactory<LeitorDbContext> contexts,
    HistorySearchService history,
    CatalogSearchClient catalog,
    IBackgroundJobClient jobs) : ControllerBase
{
    /// <summary>
    /// Busca no que já foi coletado. Não chama o Mercado Livre, então pode rodar a cada tecla.
    /// </summary>
    [HttpGet("historico")]
    public async Task<IReadOnlyList<HistoryHit>> History([FromQuery] string? q, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await history.SearchAsync(db, q, cancellationToken);
    }

    /// <summary>
    /// Procura no catálogo do Mercado Livre. <b>Custa uma chamada da conta</b>, por isso é uma
    /// ação explícita, nunca disparada pela digitação.
    /// <para>
    /// Devolve 200 com <c>unavailable</c> preenchido quando não dá para buscar agora (token
    /// vencendo, Worker parado) — a tela precisa explicar isso, e não mostrar "nada encontrado",
    /// que seria mentira.
    /// </para>
    /// </summary>
    [HttpGet("catalogo")]
    public async Task<ActionResult<CatalogSearchResult>> Catalog([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q)) return new CatalogSearchResult([], null);

        IReadOnlyList<CatalogHit> hits;
        try
        {
            hits = await catalog.SearchAsync(q, cancellationToken);
        }
        catch (CatalogUnavailableException exception)
        {
            return new CatalogSearchResult([], exception.Message);
        }

        // Marcar o que já conhecemos evita a pergunta "isto é novo?" e evita gastar uma
        // análise em quem já tem uma.
        var ids = hits.Select(hit => hit.Id).ToList();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var known = await db.Products.AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .Select(product => product.Id)
            .ToHashSetAsync(cancellationToken);

        return new CatalogSearchResult(
            [.. hits.Select(hit => new CatalogCandidate(hit.Id, hit.Name, hit.DomainId, hit.Thumbnail, known.Contains(hit.Id)))],
            null);
    }

    /// <summary>
    /// Põe o produto na fila para ser analisado. Quem executa é o Worker — a API nunca fala com
    /// o Mercado Livre, para não disputar o refresh token de uso único com a coleta.
    /// <para>
    /// Custa ~23 chamadas, e uma análise de menos de 6 horas é reaproveitada sem gastar nada.
    /// </para>
    /// </summary>
    [HttpPost("analisar/{productId}")]
    [AdminOnly, RequireOperator]
    public ActionResult<AnalysisQueued> Analyze(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId)) return BadRequest();

        var jobId = jobs.Enqueue<OnDemandAnalysisJob>(job => job.RunAsync(productId, CancellationToken.None));

        return Accepted(new AnalysisQueued(
            productId,
            jobId,
            "Análise enfileirada. O Worker precisa estar rodando; o card aparece em instantes."));
    }
}
