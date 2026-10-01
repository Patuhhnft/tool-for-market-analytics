using System.Globalization;

namespace LeitorMercadoLivre.Infrastructure.Search;

// Valores internos em inglês e minúsculo; o texto em português vive só no painel. É o que
// permite guardar o filtro no navegador e mandá-lo na URL sem depender do idioma da tela.

public static class GrowthOption
{
    public const string TenPercent = "10";
    public const string FiftyPercent = "50";
    public const string HundredPercent = "100";
    public const string Exploding = "exploding";

    public static readonly string[] All = [TenPercent, FiftyPercent, HundredPercent, Exploding];
}

public static class CompetitionBand
{
    public const string UpTo5 = "upto5";
    public const string From6To15 = "6to15";
    public const string From16To30 = "16to30";
    public const string From31 = "31plus";

    public static readonly string[] All = [UpTo5, From6To15, From16To30, From31];
}

public static class NewSellerBand
{
    public const string None = "none";
    public const string UpTo2 = "upto2";
    public const string From3 = "3plus";

    public static readonly string[] All = [None, UpTo2, From3];
}

public static class ItemCondition
{
    public const string New = "new";
    public const string Used = "used";

    public static readonly string[] All = [New, Used];
}

public static class SampleConfidence
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static readonly string[] All = [Low, Medium, High];
}

public static class SortOption
{
    public const string OpportunityDesc = "opportunity_desc";
    public const string GrowthDesc = "growth_desc";
    public const string CompetitionAsc = "competition_asc";
    public const string PriceAsc = "price_asc";
    public const string PriceDesc = "price_desc";

    public static readonly string[] All = [OpportunityDesc, GrowthDesc, CompetitionAsc, PriceAsc, PriceDesc];
}

/// <summary>
/// Os filtros da área "Explorar oportunidades", já validados. Tudo opcional e combinável: AND
/// entre filtros diferentes, OR entre as opções de um mesmo filtro de múltipla escolha.
/// </summary>
public sealed record OpportunityFilter
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Um produto específico. É o que a barra de pesquisa aplica ao escolher um resultado:
    /// em vez de uma tela nova, a lista de sempre filtrada por ele — mesmo card, mesma lógica.
    /// </summary>
    public string? ProductId { get; init; }

    public string? CategoryId { get; init; }

    public decimal? PriceMin { get; init; }

    public decimal? PriceMax { get; init; }

    public decimal? OpportunityMin { get; init; }

    public decimal? OpportunityMax { get; init; }

    public string? Growth { get; init; }

    public IReadOnlyList<string> Competition { get; init; } = [];

    public IReadOnlyList<string> NewSellers { get; init; } = [];

    public IReadOnlyList<string> Conditions { get; init; } = [];

    /// <summary>Só filtra quando é <c>true</c>: "sem frete grátis" não é um filtro pedido.</summary>
    public bool? FreeShipping { get; init; }

    public IReadOnlyList<string> Confidence { get; init; } = [];

    public bool? HasSupplier { get; init; }

    public string Sort { get; init; } = SortOption.OpportunityDesc;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>
    /// Valida e normaliza o que chegou na query string. Devolve os problemas em vez de lançar:
    /// o controller transforma em 400 com a lista inteira, e não um erro por vez.
    /// </summary>
    public static (OpportunityFilter? Filter, IReadOnlyList<string> Errors) Parse(OpportunityFilterQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var errors = new List<string>();

        var growth = Normalize(query.Crescimento);
        if (growth is not null && !GrowthOption.All.Contains(growth))
        {
            errors.Add($"crescimento='{query.Crescimento}' não é um valor conhecido. Use: {string.Join(", ", GrowthOption.All)}.");
        }

        var competition = NormalizeList(query.Concorrencia, CompetitionBand.All, "concorrencia", errors);
        var newSellers = NormalizeList(query.NovosVendedores, NewSellerBand.All, "novosVendedores", errors);
        var conditions = NormalizeList(query.Condicao, ItemCondition.All, "condicao", errors);
        var confidence = NormalizeList(query.Confianca, SampleConfidence.All, "confianca", errors);

        var sort = Normalize(query.Ordenar) ?? SortOption.OpportunityDesc;
        if (!SortOption.All.Contains(sort))
        {
            errors.Add($"ordenar='{query.Ordenar}' não é um valor conhecido. Use: {string.Join(", ", SortOption.All)}.");
        }

        if (query.PrecoMin is < 0) errors.Add("precoMin não pode ser negativo.");
        if (query.PrecoMax is < 0) errors.Add("precoMax não pode ser negativo.");
        if (query.PrecoMin is { } min && query.PrecoMax is { } max && min > max)
        {
            errors.Add($"precoMin ({Format(min)}) é maior que precoMax ({Format(max)}).");
        }

        if (query.OportunidadeMin is { } indexMin && indexMin is < 0 or > 1) errors.Add("oportunidadeMin precisa ficar entre 0 e 1.");
        if (query.OportunidadeMax is { } indexMax && indexMax is < 0 or > 1) errors.Add("oportunidadeMax precisa ficar entre 0 e 1.");
        if (query.OportunidadeMin is { } a && query.OportunidadeMax is { } b && a > b)
        {
            errors.Add($"oportunidadeMin ({Format(a)}) é maior que oportunidadeMax ({Format(b)}).");
        }

        var page = query.Page ?? 1;
        if (page < 1) errors.Add("page precisa ser 1 ou maior.");

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize < 1) errors.Add("pageSize precisa ser 1 ou maior.");
        if (pageSize > MaxPageSize) errors.Add($"pageSize máximo é {MaxPageSize}.");

        if (errors.Count > 0) return (null, errors);

        return (new OpportunityFilter
        {
            ProductId = string.IsNullOrWhiteSpace(query.Produto) ? null : query.Produto.Trim().ToUpperInvariant(),
            CategoryId = string.IsNullOrWhiteSpace(query.Categoria) ? null : query.Categoria.Trim().ToUpperInvariant(),
            PriceMin = query.PrecoMin,
            PriceMax = query.PrecoMax,
            OpportunityMin = query.OportunidadeMin,
            OpportunityMax = query.OportunidadeMax,
            Growth = growth,
            Competition = competition,
            NewSellers = newSellers,
            Conditions = conditions,
            FreeShipping = query.FreteGratis,
            Confidence = confidence,
            HasSupplier = query.TemFornecedor,
            Sort = sort,
            Page = page,
            PageSize = pageSize
        }, []);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static List<string> NormalizeList(string[]? values, string[] allowed, string name, List<string> errors)
    {
        if (values is null) return [];

        var result = new List<string>();
        foreach (var raw in values)
        {
            var value = Normalize(raw);
            if (value is null) continue;

            if (!allowed.Contains(value)) errors.Add($"{name}='{raw}' não é um valor conhecido. Use: {string.Join(", ", allowed)}.");
            else if (!result.Contains(value)) result.Add(value);
        }

        return result;
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// O que chega cru na query string. Separado do filtro validado para o binding do ASP.NET não
/// precisar conhecer as regras — e para valor inválido virar 400 explicado, não 500.
/// </summary>
public sealed class OpportunityFilterQuery
{
    public string? Produto { get; set; }

    public string? Categoria { get; set; }

    public decimal? PrecoMin { get; set; }

    public decimal? PrecoMax { get; set; }

    public decimal? OportunidadeMin { get; set; }

    public decimal? OportunidadeMax { get; set; }

    public string? Crescimento { get; set; }

    public string[]? Concorrencia { get; set; }

    public string[]? NovosVendedores { get; set; }

    public string[]? Condicao { get; set; }

    public bool? FreteGratis { get; set; }

    public string[]? Confianca { get; set; }

    public bool? TemFornecedor { get; set; }

    public string? Ordenar { get; set; }

    public int? Page { get; set; }

    public int? PageSize { get; set; }
}
