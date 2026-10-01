using LeitorMercadoLivre.Api.Security;
using LeitorMercadoLivre.Domain.Opportunity;
using LeitorMercadoLivre.Domain.Pricing;
using LeitorMercadoLivre.Domain.Suppliers;
using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Api.Controllers;

public sealed record CycleInfo(long Id, DateTimeOffset WindowStart, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, string? Error);

public sealed record SupplierView(
    long Id,
    string Name,
    decimal UnitPrice,
    string Currency,
    int MinimumOrder,
    int ShipmentQuantity,
    decimal InternationalFreightTotal,
    string? Contact,
    string? WhatsApp,
    string? WhatsAppLink,
    string? Url,
    string Source,
    DateTimeOffset UpdatedAt);

public sealed record ProductCard(
    string ProductId,
    string? Name,
    string CategoryId,
    long CycleId,
    DateTimeOffset CalculatedAt,
    string? Quadrant,
    decimal? OpportunityIndex,
    bool LowDemand,
    string Explanation,
    int Sellers,
    int NewSellersThisWeek,
    PriceReferenceResult Price,
    // "up", "down" ou "flat" conforme a tolerância da configuração; null sem semana anterior.
    string? PriceTrend,
    // Dispersão alta ou força da moda caindo além da tolerância: selo "preço instável".
    bool PriceUnstable,
    string? FeeCategoryId,
    DemandPayload Demand,
    MarginPayload? Margin,
    // Quanto vale a pena pagar por uma unidade. Vem separado da margem porque não depende de
    // fornecedor: costuma existir justamente quando a margem ainda não existe.
    CeilingPayload? Ceiling,
    // Quanto de janela resta e por quais canais ainda dá tempo de comprar. É o que separa
    // "vender agora" de "dá para importar" — a decisão que o card de hoje não respondia.
    RunwayPayload? Runway,
    // Certificação exigida e margem em kit — risco de perda total e saída para ticket baixo.
    ProductRiskPayload? Risks,
    OpportunityResult? Opportunity,
    IReadOnlyList<SupplierView> Suppliers);

public sealed record ProductBoard(CycleInfo? Cycle, CycleInfo? LastAttempt, IReadOnlyList<ProductCard> Products);

[ApiController]
[Route("api/products")]
public sealed class ProductsController(
    IDbContextFactory<LeitorDbContext> contexts,
    MarginAssessor margins,
    BusinessConfigurationStore configuration,
    TimeProvider clock) : ControllerBase
{
    /// <summary>
    /// Os cards do último ciclo concluído, na ordem do §7: primeiro quem passou do piso de
    /// demanda, depois os de demanda baixa; dentro de cada bloco, pelo índice.
    /// </summary>
    [HttpGet]
    public async Task<ProductBoard> List(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var lastAttempt = await db.Cycles.AsNoTracking().OrderByDescending(c => c.StartedAt).FirstOrDefaultAsync(cancellationToken);
        var completed = await db.Cycles.AsNoTracking()
            .Where(c => c.Status == CycleStatus.Completed)
            .OrderByDescending(c => c.WindowStart)
            .FirstOrDefaultAsync(cancellationToken);

        if (completed is null) return new ProductBoard(null, ToInfo(lastAttempt), []);

        var analyses = await db.Analyses.AsNoTracking().Where(a => a.CycleId == completed.Id).ToListAsync(cancellationToken);
        var cards = await BuildCardsAsync(db, analyses, Thresholds(), cancellationToken);

        return new ProductBoard(
            ToInfo(completed),
            ToInfo(lastAttempt),
            [
                .. cards
                    .OrderBy(card => card.LowDemand)
                    .ThenByDescending(card => card.OpportunityIndex ?? -1m)
                    .ThenBy(card => card.ProductId, StringComparer.Ordinal)
            ]);
    }

    [HttpGet("{productId}")]
    public async Task<ActionResult<ProductCard>> Get(string productId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var analysis = await db.Analyses.AsNoTracking()
            .Where(a => a.ProductId == productId)
            .OrderByDescending(a => a.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (analysis is null) return NotFound();
        return (await BuildCardsAsync(db, [analysis], Thresholds(), cancellationToken)).Single();
    }

    /// <summary>
    /// Recalcula margem e índice com o fornecedor e a configuração de agora, sem chamar o
    /// Mercado Livre. É o que o painel dispara ao cadastrar um fornecedor.
    /// </summary>
    [HttpPost("{productId}/recalculate")]
    [AdminOnly, RequireOperator]
    public async Task<ActionResult<ProductCard>> Recalculate(string productId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        try
        {
            if (!await margins.RefreshLatestAsync(db, productId, cancellationToken)) return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status409Conflict, title: "Recálculo impossível agora");
        }

        return await Get(productId, cancellationToken);
    }

    /// <summary>Limiares da interface, vindos da configuração vigente — nenhum mora no front (I8).</summary>
    internal sealed record DisplayThresholds(decimal TrendTolerance, decimal UnstableDispersion);

    private DisplayThresholds Thresholds() => Thresholds(configuration, clock);

    /// <summary>A mesma leitura serve a "Explorar oportunidades": um card só, uma regra só.</summary>
    internal static DisplayThresholds Thresholds(BusinessConfigurationStore configuration, TimeProvider clock)
    {
        var effective = configuration.ResolveAt(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)).Configuration;
        return new DisplayThresholds(
            effective?.Parameters.Entry.TrendTolerance ?? 0.01m,
            effective?.PriceReference.LowConfidenceDispersionThreshold ?? new PriceReferenceOptions().LowConfidenceDispersionThreshold);
    }

    private static string? Trend(decimal? change, decimal tolerance) => change switch
    {
        null => null,
        > 0 when change > tolerance => "up",
        < 0 when -change > tolerance => "down",
        _ => "flat"
    };

    internal static async Task<List<ProductCard>> BuildCardsAsync(
        LeitorDbContext db, List<ProductAnalysis> analyses, DisplayThresholds thresholds, CancellationToken cancellationToken)
    {
        var ids = analyses.Select(a => a.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var suppliers = (await db.Suppliers.AsNoTracking()
                .Where(s => ids.Contains(s.ProductId) && s.Active)
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.ProductId)
            .ToDictionary(group => group.Key, group => group.Select(ToView).ToList());

        return
        [
            .. analyses.Select(analysis =>
            {
                var price = AnalysisJson.Read<PricePayload>(analysis.PriceJson)!;
                var product = products.GetValueOrDefault(analysis.ProductId);

                return new ProductCard(
                    analysis.ProductId,
                    product?.Name,
                    product?.CategoryId ?? "",
                    analysis.CycleId,
                    analysis.CalculatedAt,
                    analysis.Quadrant,
                    analysis.OpportunityIndex,
                    analysis.LowDemand,
                    analysis.Explanation,
                    price.Result.SellersFound,
                    analysis.NewSellers ?? 0,
                    price.Result,
                    Trend(price.Result.WeeklyMarketPriceChange, thresholds.TrendTolerance),
                    price.Result.DispersionIndex > thresholds.UnstableDispersion ||
                        price.Result.WeeklyModeStrengthChange < -thresholds.TrendTolerance,
                    price.CategoryId,
                    AnalysisJson.Read<DemandPayload>(analysis.DemandJson)!,
                    AnalysisJson.Read<MarginPayload>(analysis.MarginJson),
                    AnalysisJson.Read<CeilingPayload>(analysis.CeilingJson),
                    AnalysisJson.Read<RunwayPayload>(analysis.RunwayJson),
                    AnalysisJson.Read<ProductRiskPayload>(analysis.RiskJson),
                    AnalysisJson.Read<OpportunityPayload>(analysis.OpportunityJson)?.Result,
                    suppliers.GetValueOrDefault(analysis.ProductId) ?? []);
            })
        ];
    }

    internal static SupplierView ToView(Supplier supplier) => new(
        supplier.Id, supplier.Name, supplier.UnitPrice, supplier.Currency, supplier.MinimumOrder,
        supplier.ShipmentQuantity, supplier.InternationalFreightTotal, supplier.Contact, supplier.WhatsApp,
        WhatsAppLink.From(supplier.WhatsApp), supplier.Url, supplier.Source, supplier.UpdatedAt);

    internal static CycleInfo? ToInfo(CollectionCycle? cycle) => cycle is null
        ? null
        : new CycleInfo(cycle.Id, cycle.WindowStart, cycle.StartedAt, cycle.FinishedAt, cycle.Status, cycle.Error);
}
