using System.Globalization;
using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Infrastructure.Search;

/// <summary>Uma categoria que de fato tem produto analisado, com quantos são.</summary>
public sealed record CategoryOption(string Id, string Label, int Count);

/// <summary>Uma opção de filtro pronta para virar botão: valor interno + texto da tela.</summary>
public sealed record FilterOption(string Value, string Label);

/// <summary>
/// O que o painel precisa para montar os controles. Vem do servidor de propósito: os limites
/// saem da configuração vigente e dos dados reais, e nenhum número fica escrito no front (I8).
/// </summary>
public sealed record FilterMeta(
    IReadOnlyList<CategoryOption> Categories,
    decimal? PriceMin,
    decimal? PriceMax,
    int TotalProducts,
    IReadOnlyList<FilterOption> Growth,
    IReadOnlyList<FilterOption> Competition,
    IReadOnlyList<FilterOption> NewSellers,
    IReadOnlyList<FilterOption> Conditions,
    IReadOnlyList<FilterOption> Confidence,
    IReadOnlyList<FilterOption> Sort);

/// <summary>
/// A busca de "Explorar oportunidades". Trabalha sobre a ÚLTIMA análise de cada produto — o
/// que a tela pergunta é "como está hoje", não "como esteve em algum ciclo".
/// <para>
/// Tudo acontece em SQL: filtro, contagem, ordenação e recorte da página. O que volta para a
/// API é só a página pedida, então o custo não cresce com o tamanho do catálogo.
/// </para>
/// </summary>
public sealed class OpportunitySearchService(BusinessConfigurationStore configuration, TimeProvider clock)
{
    /// <summary>Uma página de análises já filtrada e ordenada, com o total que passou no filtro.</summary>
    public async Task<PagedResponse<ProductAnalysis>> SearchAsync(
        LeitorDbContext db, OpportunityFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);

        var rows = Apply(db, filter).AsNoTracking();

        // Duas idas ao banco: o total do filtro inteiro e a página. O COUNT é o que alimenta o
        // contador "N produtos encontrados", e ele precisa ignorar a paginação.
        var total = await rows.CountAsync(cancellationToken);

        var analyses = await rows
            .OrderBySort(filter.Sort)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<ProductAnalysis>(analyses, total, filter.Page, filter.PageSize);
    }

    /// <summary>
    /// As opções dos controles. As categorias são só as que têm produto analisado — oferecer
    /// uma categoria vazia é prometer um resultado que não existe.
    /// </summary>
    public async Task<FilterMeta> GetMetaAsync(LeitorDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        // "Tem alguma análise" e "tem a última análise" selecionam os mesmos produtos: a última
        // é uma por produto. A forma simples é a que o banco resolve com um EXISTS só.
        var categories = await db.Products.AsNoTracking()
            .Where(product => db.Analyses.Any(analysis => analysis.ProductId == product.Id))
            .GroupBy(product => new { product.CategoryId, product.CategoryName })
            .Select(group => new CategoryOption(
                group.Key.CategoryId,
                group.Key.CategoryName ?? group.Key.CategoryId,
                group.Count()))
            .ToListAsync(cancellationToken);

        // Ordem alfabética em português (acento depois da letra sem acento) — quem ordena é a
        // cultura corrente, não a collation do banco.
        categories.Sort((left, right) => string.Compare(left.Label, right.Label, StringComparison.CurrentCulture));

        var prices = db.LatestPerProduct().AsNoTracking().Select(analysis => analysis.MarketPrice);
        var priceMin = await prices.MinAsync(cancellationToken);
        var priceMax = await prices.MaxAsync(cancellationToken);

        var demand = Demand();

        return new FilterMeta(
            categories,
            priceMin,
            priceMax,
            categories.Sum(option => option.Count),
            [
                new FilterOption(GrowthOption.TenPercent, "+10% ou mais"),
                new FilterOption(GrowthOption.FiftyPercent, "+50% ou mais"),
                new FilterOption(GrowthOption.HundredPercent, "+100% ou mais"),
                // O rótulo carrega o número da configuração: se o limiar mudar, a tela muda junto.
                new FilterOption(GrowthOption.Exploding, $"Explodindo (+{Trim(demand.ExplodingGrowthPercent)}% ou estreia forte)")
            ],
            [
                new FilterOption(CompetitionBand.UpTo5, "Até 5 vendedores"),
                new FilterOption(CompetitionBand.From6To15, "6 a 15 vendedores"),
                new FilterOption(CompetitionBand.From16To30, "16 a 30 vendedores"),
                new FilterOption(CompetitionBand.From31, "31 ou mais vendedores")
            ],
            [
                new FilterOption(NewSellerBand.None, "Nenhum entrante"),
                new FilterOption(NewSellerBand.UpTo2, "1 ou 2 entrantes"),
                new FilterOption(NewSellerBand.From3, "3 ou mais entrantes")
            ],
            [
                new FilterOption(ItemCondition.New, "Tem anúncio novo"),
                new FilterOption(ItemCondition.Used, "Tem anúncio usado")
            ],
            [
                new FilterOption(SampleConfidence.High, "Alta"),
                new FilterOption(SampleConfidence.Medium, "Média"),
                new FilterOption(SampleConfidence.Low, "Baixa")
            ],
            [
                new FilterOption(SortOption.OpportunityDesc, "Maior índice de oportunidade"),
                new FilterOption(SortOption.GrowthDesc, "Maior crescimento de visitas"),
                new FilterOption(SortOption.CompetitionAsc, "Menos concorrência"),
                new FilterOption(SortOption.PriceAsc, "Menor preço"),
                new FilterOption(SortOption.PriceDesc, "Maior preço")
            ]);
    }

    /// <summary>
    /// Encadeia os filtros. Cada um devolve a consulta intacta quando não foi preenchido, então
    /// "nenhum filtro" é literalmente o universo inteiro — sem caminho especial.
    /// </summary>
    private IQueryable<ProductAnalysis> Apply(LeitorDbContext db, OpportunityFilter filter) =>
        db.LatestPerProduct()
            .WhereProduct(filter.ProductId)
            .WhereCategory(db, filter.CategoryId)
            .WherePrice(filter.PriceMin, filter.PriceMax)
            .WhereOpportunity(filter.OpportunityMin, filter.OpportunityMax)
            .WhereGrowth(filter.Growth, Demand())
            .WhereCompetition(filter.Competition)
            .WhereNewSellers(filter.NewSellers)
            .WhereCondition(filter.Conditions)
            .WhereFreeShipping(filter.FreeShipping)
            .WhereConfidence(filter.Confidence)
            .WhereSupplier(db, filter.HasSupplier);

    /// <summary>
    /// Os limiares de demanda de hoje. Sem configuração vigente valem os padrões do domínio: a
    /// busca continua respondendo, só sem calibração — quem reclama da configuração é o painel
    /// do administrador, não a tela de explorar.
    /// </summary>
    private DemandOptions Demand() =>
        configuration.ResolveAt(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)).Configuration?.Demand ?? new DemandOptions();

    private static string Trim(decimal value) => value == decimal.Truncate(value)
        ? decimal.Truncate(value).ToString(CultureInfo.InvariantCulture)
        : value.ToString(CultureInfo.InvariantCulture);
}
