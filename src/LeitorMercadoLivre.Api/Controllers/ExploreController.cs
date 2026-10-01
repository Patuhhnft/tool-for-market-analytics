using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Api.Controllers;

/// <summary>
/// Uma página de cards com o total do filtro. O card é exatamente o mesmo de
/// <c>/api/products</c> — a tela de explorar muda quais produtos aparecem, nunca o que um
/// produto mostra.
/// </summary>
public sealed record ExploreResult(
    IReadOnlyList<ProductCard> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious,
    OpportunityFilter AppliedFilter,
    CycleInfo? LastCycle);

/// <summary>
/// "Explorar oportunidades": busca filtrada sobre a última análise de cada produto.
/// <para>
/// Área separada de propósito. <c>/api/products</c> continua sendo o quadro do último ciclo,
/// com o mesmo formato de sempre — nada aqui altera aquele contrato.
/// </para>
/// </summary>
[ApiController]
[Route("api/oportunidades/explorar")]
public sealed class ExploreController(
    IDbContextFactory<LeitorDbContext> contexts,
    OpportunitySearchService search,
    BusinessConfigurationStore configuration,
    TimeProvider clock) : ControllerBase
{
    /// <summary>
    /// Os produtos que passam no filtro, paginados. Filtro vazio devolve tudo — a tela abre
    /// mostrando o catálogo inteiro, e cada controle só tira coisa dali.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ExploreResult>> Search([FromQuery] OpportunityFilterQuery query, CancellationToken cancellationToken)
    {
        // Todos os problemas de uma vez: quem está montando a URL na mão corrige tudo numa
        // passada, em vez de descobrir um erro por requisição.
        var (filter, errors) = OpportunityFilter.Parse(query);
        if (filter is null)
        {
            foreach (var error in errors) ModelState.AddModelError("filtro", error);
            return ValidationProblem(ModelState);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var page = await search.SearchAsync(db, filter, cancellationToken);
        var cards = await ProductsController.BuildCardsAsync(
            db, [.. page.Items], ProductsController.Thresholds(configuration, clock), cancellationToken);

        // A ordenação veio do SQL; BuildCardsAsync preserva a ordem da lista que recebe.
        var lastCycle = await db.Cycles.AsNoTracking()
            .Where(c => c.Status == CycleStatus.Completed)
            .OrderByDescending(c => c.WindowStart)
            .FirstOrDefaultAsync(cancellationToken);

        return new ExploreResult(
            cards,
            page.Total,
            page.Page,
            page.PageSize,
            page.TotalPages,
            page.HasNext,
            page.HasPrevious,
            filter,
            ProductsController.ToInfo(lastCycle));
    }

    /// <summary>
    /// As opções dos controles: categorias que existem, faixa real de preço e os rótulos. Vem
    /// do servidor para o front não guardar limiar nenhum (I8).
    /// </summary>
    [HttpGet("filtros-meta")]
    public async Task<FilterMeta> Meta(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await search.GetMetaAsync(db, cancellationToken);
    }
}
