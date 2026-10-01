using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Infrastructure.Persistence;

namespace LeitorMercadoLivre.Infrastructure.Search;

/// <summary>
/// Um método por filtro, todos sobre <see cref="IQueryable{T}"/>: nada é materializado aqui, e
/// a consulta inteira vira um SQL só.
/// <para>
/// A consulta anda sobre <see cref="ProductAnalysis"/> do começo ao fim. O que depende do
/// produto entra como <c>EXISTS</c> — juntar análise e produto num tipo intermediário é
/// justamente o que o tradutor do EF não consegue agrupar depois.
/// </para>
/// <para>
/// Métrica desconhecida é <c>null</c> e NÃO entra em filtro numérico daquela métrica. Um produto
/// sem preço medido não é "barato" nem "caro" — ele simplesmente não responde à pergunta.
/// </para>
/// </summary>
public static class OpportunityQueryExtensions
{
    /// <summary>
    /// A última análise de cada produto. O id é sequencial e crescente, então o maior id por
    /// produto é a análise mais recente — e a subconsulta correlacionada
    /// (<c>id = (SELECT max(id) ... WHERE product_id = ...)</c>) é a forma que o EF traduz.
    /// </summary>
    public static IQueryable<ProductAnalysis> LatestPerProduct(this LeitorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return db.Analyses.Where(analysis => analysis.Id == db.Analyses
            .Where(other => other.ProductId == analysis.ProductId)
            .Max(other => other.Id));
    }

    /// <summary>Um produto só. Quando vem, os demais filtros continuam valendo por cima.</summary>
    public static IQueryable<ProductAnalysis> WhereProduct(this IQueryable<ProductAnalysis> rows, string? productId) =>
        string.IsNullOrEmpty(productId) ? rows : rows.Where(analysis => analysis.ProductId == productId);

    public static IQueryable<ProductAnalysis> WhereCategory(
        this IQueryable<ProductAnalysis> rows, LeitorDbContext db, string? categoryId)
    {
        ArgumentNullException.ThrowIfNull(db);

        return string.IsNullOrEmpty(categoryId)
            ? rows
            : rows.Where(analysis => db.Products.Any(p => p.Id == analysis.ProductId && p.CategoryId == categoryId));
    }

    public static IQueryable<ProductAnalysis> WherePrice(this IQueryable<ProductAnalysis> rows, decimal? min, decimal? max)
    {
        if (min is { } from) rows = rows.Where(analysis => analysis.MarketPrice >= from);
        if (max is { } to) rows = rows.Where(analysis => analysis.MarketPrice <= to);
        return rows;
    }

    public static IQueryable<ProductAnalysis> WhereOpportunity(this IQueryable<ProductAnalysis> rows, decimal? min, decimal? max)
    {
        if (min is { } from) rows = rows.Where(analysis => analysis.OpportunityIndex >= from);
        if (max is { } to) rows = rows.Where(analysis => analysis.OpportunityIndex <= to);
        return rows;
    }

    /// <summary>
    /// As opções de crescimento são pisos, não faixas: "+50%" quer dizer 50 ou mais.
    /// "Explodindo" inclui a demanda nova com volume — quem estreou forte não tem percentual
    /// para comparar, mas é exatamente o caso que o filtro procura.
    /// </summary>
    public static IQueryable<ProductAnalysis> WhereGrowth(this IQueryable<ProductAnalysis> rows, string? option, DemandOptions demand)
    {
        ArgumentNullException.ThrowIfNull(demand);

        return option switch
        {
            GrowthOption.TenPercent => rows.Where(analysis => analysis.VisitGrowthPercent >= 10m),
            GrowthOption.FiftyPercent => rows.Where(analysis => analysis.VisitGrowthPercent >= 50m),
            GrowthOption.HundredPercent => rows.Where(analysis => analysis.VisitGrowthPercent >= 100m),
            GrowthOption.Exploding => rows.Where(analysis =>
                analysis.VisitGrowthPercent >= demand.ExplodingGrowthPercent ||
                (analysis.IsNewDemand && analysis.LastWeekVisits >= demand.MinVisitsForNewDemand)),
            _ => rows
        };
    }

    public static IQueryable<ProductAnalysis> WhereCompetition(this IQueryable<ProductAnalysis> rows, IReadOnlyList<string> bands)
    {
        if (bands is null || bands.Count == 0) return rows;

        var upTo5 = bands.Contains(CompetitionBand.UpTo5);
        var from6 = bands.Contains(CompetitionBand.From6To15);
        var from16 = bands.Contains(CompetitionBand.From16To30);
        var from31 = bands.Contains(CompetitionBand.From31);

        return rows.Where(analysis =>
            (upTo5 && analysis.SellersFound <= 5) ||
            (from6 && analysis.SellersFound >= 6 && analysis.SellersFound <= 15) ||
            (from16 && analysis.SellersFound >= 16 && analysis.SellersFound <= 30) ||
            (from31 && analysis.SellersFound >= 31));
    }

    /// <summary>
    /// Sem ciclo anterior, <c>new_sellers</c> é nulo — e nulo fica fora de todas as faixas,
    /// inclusive de "Nenhum". "Não medimos" não é "medimos e deu zero".
    /// </summary>
    public static IQueryable<ProductAnalysis> WhereNewSellers(this IQueryable<ProductAnalysis> rows, IReadOnlyList<string> bands)
    {
        if (bands is null || bands.Count == 0) return rows;

        var none = bands.Contains(NewSellerBand.None);
        var upTo2 = bands.Contains(NewSellerBand.UpTo2);
        var from3 = bands.Contains(NewSellerBand.From3);

        return rows.Where(analysis =>
            (none && analysis.NewSellers == 0) ||
            (upTo2 && analysis.NewSellers >= 1 && analysis.NewSellers <= 2) ||
            (from3 && analysis.NewSellers >= 3));
    }

    /// <summary>
    /// "Existe ao menos um anúncio nessa condição". O card é o produto, e um produto costuma
    /// ter anúncios novos e usados ao mesmo tempo — exigir que TODOS fossem novos esconderia
    /// justamente os produtos que interessam.
    /// </summary>
    public static IQueryable<ProductAnalysis> WhereCondition(this IQueryable<ProductAnalysis> rows, IReadOnlyList<string> conditions)
    {
        if (conditions is null || conditions.Count == 0) return rows;

        var wantsNew = conditions.Contains(ItemCondition.New);
        var wantsUsed = conditions.Contains(ItemCondition.Used);

        return rows.Where(analysis =>
            (wantsNew && analysis.HasNewCondition == true) ||
            (wantsUsed && analysis.HasUsedCondition == true));
    }

    public static IQueryable<ProductAnalysis> WhereFreeShipping(this IQueryable<ProductAnalysis> rows, bool? freeShipping) =>
        freeShipping == true ? rows.Where(analysis => analysis.HasFreeShipping == true) : rows;

    public static IQueryable<ProductAnalysis> WhereConfidence(this IQueryable<ProductAnalysis> rows, IReadOnlyList<string> levels)
    {
        if (levels is null || levels.Count == 0) return rows;

        // O domínio grava "High"/"Medium"/"Low"; a API fala em minúsculo.
        var stored = levels.Select(level => level switch
        {
            SampleConfidence.Low => nameof(Domain.Pricing.PriceConfidence.Low),
            SampleConfidence.Medium => nameof(Domain.Pricing.PriceConfidence.Medium),
            _ => nameof(Domain.Pricing.PriceConfidence.High)
        }).ToList();

        return rows.Where(analysis => analysis.PriceConfidence != null && stored.Contains(analysis.PriceConfidence));
    }

    /// <summary>Fornecedor é relação um-para-muitos: <c>EXISTS</c>, nunca comparação por nome.</summary>
    public static IQueryable<ProductAnalysis> WhereSupplier(this IQueryable<ProductAnalysis> rows, LeitorDbContext db, bool? hasSupplier)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (hasSupplier is not { } wanted) return rows;

        return wanted
            ? rows.Where(analysis => db.Suppliers.Any(s => s.ProductId == analysis.ProductId && s.Active))
            : rows.Where(analysis => !db.Suppliers.Any(s => s.ProductId == analysis.ProductId && s.Active));
    }

    /// <summary>
    /// Ordenação por lista fixa — o cliente manda a chave, nunca o texto do ORDER BY.
    /// <para>
    /// Nulo sempre por último: um produto sem preço medido não pode encabeçar "menor preço".
    /// E todo critério termina desempatando pelo id, senão duas páginas podem repetir ou pular
    /// itens quando vários produtos empatam no mesmo valor.
    /// </para>
    /// </summary>
    public static IOrderedQueryable<ProductAnalysis> OrderBySort(this IQueryable<ProductAnalysis> rows, string sort) => sort switch
    {
        SortOption.GrowthDesc => rows
            .OrderBy(analysis => analysis.VisitGrowthPercent == null)
            .ThenByDescending(analysis => analysis.VisitGrowthPercent)
            .ThenByDescending(analysis => analysis.Id),
        SortOption.CompetitionAsc => rows
            .OrderBy(analysis => analysis.SellersFound)
            .ThenByDescending(analysis => analysis.Id),
        SortOption.PriceAsc => rows
            .OrderBy(analysis => analysis.MarketPrice == null)
            .ThenBy(analysis => analysis.MarketPrice)
            .ThenByDescending(analysis => analysis.Id),
        SortOption.PriceDesc => rows
            .OrderBy(analysis => analysis.MarketPrice == null)
            .ThenByDescending(analysis => analysis.MarketPrice)
            .ThenByDescending(analysis => analysis.Id),
        _ => rows
            .OrderBy(analysis => analysis.OpportunityIndex == null)
            .ThenByDescending(analysis => analysis.OpportunityIndex)
            .ThenByDescending(analysis => analysis.Id)
    };
}
