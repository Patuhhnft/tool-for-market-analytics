using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Infrastructure.Search;

/// <summary>Um produto já conhecido, com o resumo da última análise dele.</summary>
public sealed record HistoryHit(
    string ProductId,
    string? Name,
    string CategoryId,
    string? CategoryName,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? AnalyzedAt,
    decimal? MarketPrice,
    decimal? OpportunityIndex,
    bool Analyzed);

/// <summary>
/// Busca no que já foi coletado. <b>Não chama o Mercado Livre</b> — é SQL sobre o nosso banco,
/// então custa zero chamada e responde a cada tecla digitada.
/// <para>
/// É a primeira parada da barra de pesquisa: antes de gastar chamada da conta do ML procurando
/// no catálogo, vale ver se o produto já está aqui.
/// </para>
/// </summary>
public sealed class HistorySearchService
{
    /// <summary>Acima disso a lista deixa de ajudar a escolher e vira rolagem.</summary>
    public const int MaxResults = 20;

    public async Task<IReadOnlyList<HistoryHit>> SearchAsync(
        LeitorDbContext db, string? query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var term = query?.Trim();
        if (string.IsNullOrEmpty(term)) return [];

        // Colar um MLB inteiro é o gesto natural de quem já conhece o produto: casa exato,
        // sem depender de o nome ter sido coletado.
        var byId = term.ToUpperInvariant();

        var products = db.Products.AsNoTracking().Where(product =>
            product.Id == byId ||
            (product.Name != null && EF.Functions.ILike(product.Name, $"%{term}%")));

        // Uma consulta só: o produto e a análise mais recente dele, quando existe.
        var rows = await products
            .Select(product => new
            {
                Product = product,
                Latest = db.Analyses
                    .Where(analysis => analysis.ProductId == product.Id)
                    .OrderByDescending(analysis => analysis.Id)
                    .Select(analysis => new { analysis.CalculatedAt, analysis.MarketPrice, analysis.OpportunityIndex })
                    .FirstOrDefault()
            })
            // Quem tem análise primeiro: é sobre eles que há o que dizer.
            .OrderByDescending(row => row.Latest != null)
            .ThenByDescending(row => row.Latest!.OpportunityIndex)
            .ThenByDescending(row => row.Product.LastSeenAt)
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new HistoryHit(
                row.Product.Id,
                row.Product.Name,
                row.Product.CategoryId,
                row.Product.CategoryName,
                row.Product.LastSeenAt,
                row.Latest?.CalculatedAt,
                row.Latest?.MarketPrice,
                row.Latest?.OpportunityIndex,
                row.Latest is not null))
        ];
    }
}
