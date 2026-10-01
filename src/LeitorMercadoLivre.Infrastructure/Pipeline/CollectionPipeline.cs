using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Domain.Margin;
using LeitorMercadoLivre.Domain.Opportunity;
using LeitorMercadoLivre.Domain.Pricing;
using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.MercadoLivre;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.Pipeline;

public sealed class CollectionOptions
{
    public const string SectionName = "Coleta";

    /// <summary>Duração da janela de coleta. O ciclo é único por janela (I10).</summary>
    public int IntervalHours { get; set; } = 6;
}

/// <summary>
/// O que a análise sob demanda produziu. <paramref name="Reused"/> diz que nada foi gasto:
/// a análise já existia e estava nova o bastante.
/// </summary>
public sealed record OnDemandResult(string ProductId, bool Reused, DateTimeOffset? AnalyzedAt, string? Error);

public sealed record CycleSummary(
    long CycleId,
    string Status,
    bool Skipped,
    int Candidates,
    int Finalists,
    int Analyses,
    IReadOnlyList<string> Errors);

/// <summary>
/// O ciclo de coleta (§8), de ponta a ponta:
/// ranking → anúncios dos candidatos → corte → série de visitas, preço, margem, índice.
/// <para>
/// Idempotente por janela (I10): rodar de novo a mesma janela apaga o que o ciclo gravou e
/// refaz, em vez de duplicar. Um produto com erro não derruba o ciclo — o erro fica
/// registrado e os outros seguem. Credencial ausente ou configuração inválida, sim,
/// derrubam: sem elas nenhum número do ciclo seria confiável.
/// </para>
/// </summary>
public sealed class CollectionPipeline(
    IDbContextFactory<LeitorDbContext> contexts,
    MercadoLivreClient mercadoLivre,
    SaleScheduleProvider feeTables,
    MarginAssessor margins,
    BusinessConfigurationStore configuration,
    IOptions<MercadoLivreApiOptions> apiOptions,
    IOptions<CollectionOptions> collectionOptions,
    TimeProvider clock,
    ILogger<CollectionPipeline> logger)
{
    public async Task<CycleSummary> RunAsync(bool force, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var resolved = configuration.ResolveAt(today);
        var config = resolved.Configuration
            ?? throw new InvalidOperationException("Configuração inválida: " + string.Join(" ", resolved.Problems));

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var cycle = await OpenCycleAsync(db, now, force, cancellationToken);

        if (cycle is null)
        {
            logger.LogInformation("Janela já coletada; nada a fazer.");
            return new CycleSummary(0, CycleStatus.Completed, true, 0, 0, 0, []);
        }

        var errors = new List<string>();
        var parameters = config.Parameters.Entry;

        try
        {
            var candidates = await CollectRankingAsync(db, cycle, parameters, now, errors, cancellationToken);
            var picked = PickCandidates(candidates, parameters.CandidatesPerCycle);

            var listings = await CollectListingsAsync(db, cycle, picked, now, errors, cancellationToken);
            var finalists = PickFinalists(listings, candidates, parameters.FinalistsPerCycle);

            var analyses = 0;
            foreach (var productId in finalists)
            {
                try
                {
                    await AnalyzeAsync(db, cycle, productId, listings[productId], candidates[productId], config, today, now, cancellationToken);
                    analyses++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not MercadoLivreAuthException)
                {
                    logger.LogWarning(exception, "Produto {ProductId} ficou fora do ciclo.", productId);
                    errors.Add($"{productId}: {exception.Message}");
                    db.ChangeTracker.Clear();
                }
            }

            cycle = await db.Cycles.SingleAsync(c => c.Id == cycle.Id, cancellationToken);
            cycle.Status = CycleStatus.Completed;
            cycle.FinishedAt = clock.GetUtcNow();
            cycle.StatsJson = AnalysisJson.Write(new { candidates = candidates.Count, collected = listings.Count, finalists = finalists.Count, analyses, errors });
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Ciclo {CycleId} concluído: {Analyses} análises, {Errors} erros.", cycle.Id, analyses, errors.Count);
            return new CycleSummary(cycle.Id, cycle.Status, false, candidates.Count, finalists.Count, analyses, errors);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            var failed = await db.Cycles.SingleAsync(c => c.Id == cycle.Id, CancellationToken.None);
            failed.Status = CycleStatus.Failed;
            failed.FinishedAt = clock.GetUtcNow();
            failed.Error = exception.Message;
            failed.StatsJson = AnalysisJson.Write(new { errors });
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    // ------------------------------------------------------------------------------------
    // Ciclo
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Analisa UM produto fora do ciclo agendado — o que a barra de pesquisa dispara.
    /// <para>
    /// Custa as mesmas ~23 chamadas de um produto do ciclo, então uma análise recente é
    /// reaproveitada: o limite de requisições é da conta do Mercado Livre, e ninguém precisa
    /// remedir o que foi medido há minutos.
    /// </para>
    /// <para>
    /// Roda no Worker, nunca na API: o refresh token é de uso único, e dois processos
    /// renovando ao mesmo tempo derrubariam a coleta.
    /// </para>
    /// </summary>
    public async Task<OnDemandResult> AnalyzeOnDemandAsync(
        string productId, TimeSpan maxAge, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var resolved = configuration.ResolveAt(today);
        var config = resolved.Configuration
            ?? throw new InvalidOperationException("Configuração inválida: " + string.Join(" ", resolved.Problems));

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var recent = await db.Analyses.AsNoTracking()
            .Where(a => a.ProductId == productId)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (recent is not null && now - recent.CalculatedAt < maxAge)
        {
            logger.LogInformation("Análise de {Produto} tem menos de {Idade}; reaproveitada.", productId, maxAge);
            return new OnDemandResult(productId, true, recent.CalculatedAt, null);
        }

        IReadOnlyList<ProductListing> fetched;
        try
        {
            fetched = await mercadoLivre.GetProductListingsAsync(productId, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return new OnDemandResult(productId, false, null, $"não foi possível ler os anúncios: {exception.Message}");
        }

        if (fetched.Count == 0)
        {
            return new OnDemandResult(productId, false, null, "o produto não tem anúncios ativos — sem anúncio não há preço de mercado (I5).");
        }

        // Ciclo próprio, com status "on-demand": fica no histórico sem se passar por coleta e
        // sem sequestrar o quadro, que mostra o último ciclo CONCLUÍDO.
        var cycle = new CollectionCycle
        {
            WindowStart = now,
            StartedAt = now,
            FinishedAt = now,
            Status = CycleStatus.OnDemand,
            StatsJson = $"{{\"produto\":\"{productId}\"}}"
        };
        db.Cycles.Add(cycle);

        var category = fetched
            .Select(listing => listing.CategoryId)
            .Where(id => !string.IsNullOrEmpty(id))
            .GroupBy(id => id!)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? "";

        var product = await db.Products.FindAsync([productId], cancellationToken);
        if (product is null)
        {
            db.Products.Add(new CatalogProduct
            {
                Id = productId,
                CategoryId = category,
                // Sem o nome da categoria o card mostra "MLB1051" cru. Uma chamada a mais
                // por produto novo é barata perto das ~23 que a análise já gasta.
                CategoryName = await CategoryNameOrNullAsync(category, cancellationToken),
                Name = await NameOrNullAsync(productId, cancellationToken),
                FirstSeenAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            product.LastSeenAt = now;
            product.Name ??= await NameOrNullAsync(productId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        var snapshots = fetched.Select(listing => ToSnapshot(cycle, productId, listing, now)).ToList();
        db.Listings.AddRange(snapshots);
        await db.SaveChangesAsync(cancellationToken);

        // Sem posição de ranking: o produto não veio de lá. A demanda sai das visitas.
        await AnalyzeAsync(db, cycle, productId, snapshots, new Candidate(productId, category, 0, null), config, today, now, cancellationToken);

        return new OnDemandResult(productId, false, now, null);
    }

    /// <summary>Um anúncio da API vira linha de snapshot. Mesma conversão no ciclo e na busca.</summary>
    private static ListingSnapshot ToSnapshot(CollectionCycle cycle, string productId, ProductListing listing, DateTimeOffset now) =>
        new()
        {
            CycleId = cycle.Id,
            ProductId = productId,
            ItemId = listing.ItemId,
            SellerId = listing.SellerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Price = listing.Price,
            ListingTypeId = listing.ListingTypeId,
            CategoryId = listing.CategoryId,
            Condition = listing.Condition,
            FreeShipping = listing.Shipping?.FreeShipping ?? false,
            LogisticType = listing.Shipping?.LogisticType,
            OfficialStoreId = listing.OfficialStoreId,
            MinPurchaseUnit = listing.MinPurchaseUnit,
            CollectedAt = now
        };

    private async Task<string?> CategoryNameOrNullAsync(string categoryId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(categoryId)) return null;

        try
        {
            var all = await mercadoLivre.GetCategoriesAsync(cancellationToken);
            return all.FirstOrDefault(category => category.Id == categoryId)?.Name;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<string?> NameOrNullAsync(string productId, CancellationToken cancellationToken)
    {
        try
        {
            return await mercadoLivre.GetProductNameAsync(productId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Sem nome o card mostra o ID; não é motivo para perder a análise inteira.
            return null;
        }
    }

    private async Task<CollectionCycle?> OpenCycleAsync(LeitorDbContext db, DateTimeOffset now, bool force, CancellationToken cancellationToken)
    {
        var hours = Math.Max(1, collectionOptions.Value.IntervalHours);
        var window = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour / hours * hours, 0, 0, TimeSpan.Zero);

        var cycle = await db.Cycles.SingleOrDefaultAsync(c => c.WindowStart == window, cancellationToken);

        if (cycle is { Status: CycleStatus.Completed } && !force) return null;

        if (cycle is null)
        {
            cycle = new CollectionCycle { WindowStart = window };
            db.Cycles.Add(cycle);
        }
        else
        {
            // Refazer a janela: some o que ela gravou, para não duplicar (I10).
            await db.Analyses.Where(a => a.CycleId == cycle.Id).ExecuteDeleteAsync(cancellationToken);
            await db.Listings.Where(l => l.CycleId == cycle.Id).ExecuteDeleteAsync(cancellationToken);
            await db.Highlights.Where(h => h.CycleId == cycle.Id).ExecuteDeleteAsync(cancellationToken);
        }

        cycle.StartedAt = now;
        cycle.FinishedAt = null;
        cycle.Status = CycleStatus.Running;
        cycle.Error = null;
        await db.SaveChangesAsync(cancellationToken);
        return cycle;
    }

    // ------------------------------------------------------------------------------------
    // 1. Ranking
    // ------------------------------------------------------------------------------------

    private sealed record Candidate(string ProductId, string CategoryId, int Position, int? PositionGain);

    private async Task<Dictionary<string, Candidate>> CollectRankingAsync(
        LeitorDbContext db, CollectionCycle cycle, ParametersEntry parameters, DateTimeOffset now, List<string> errors, CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, Candidate>();
        var weekAgo = now.AddDays(-7);

        // Nomes das categorias: uma chamada por ciclo, para o painel exibir "Celulares e
        // Telefones" em vez de MLB1051. Se falhar, o ciclo segue com o código.
        Dictionary<string, string> categoryNames = [];
        try
        {
            categoryNames = (await mercadoLivre.GetCategoriesAsync(cancellationToken))
                .ToDictionary(category => category.Id, category => category.Name, StringComparer.Ordinal);
        }
        catch (HttpRequestException exception)
        {
            errors.Add($"nomes de categoria: {exception.Message}");
        }

        foreach (var category in parameters.Categories.Except(parameters.ExcludedCategories))
        {
            IReadOnlyList<HighlightEntry> ranking;
            try
            {
                ranking = await mercadoLivre.GetHighlightsAsync(category, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                errors.Add($"ranking {category}: {exception.Message}");
                continue;
            }

            // USER_PRODUCT não é âncora de catálogo (I5): fica fora, com contagem registrada.
            var skipped = ranking.Count(entry => !entry.IsCatalogProduct);
            if (skipped > 0) logger.LogInformation("{Category}: {Skipped} itens USER_PRODUCT ignorados.", category, skipped);

            foreach (var entry in ranking.Where(entry => entry.IsCatalogProduct))
            {
                var categoryName = categoryNames.GetValueOrDefault(category);
                var product = await db.Products.FindAsync([entry.Id], cancellationToken);
                if (product is null)
                {
                    db.Products.Add(new CatalogProduct
                    {
                        Id = entry.Id, CategoryId = category, CategoryName = categoryName, FirstSeenAt = now, LastSeenAt = now
                    });
                }
                else
                {
                    product.LastSeenAt = now;
                    product.CategoryName ??= categoryName;
                }

                db.Highlights.Add(new HighlightSnapshot
                {
                    CycleId = cycle.Id, CategoryId = category, ProductId = entry.Id, Position = entry.Position, CollectedAt = now
                });

                var positionWeekAgo = await db.Highlights
                    .Where(h => h.ProductId == entry.Id && h.CategoryId == category && h.CollectedAt <= weekAgo)
                    .OrderByDescending(h => h.CollectedAt)
                    .Select(h => (int?)h.Position)
                    .FirstOrDefaultAsync(cancellationToken);

                var candidate = new Candidate(entry.Id, category, entry.Position, positionWeekAgo - entry.Position);

                // Produto em mais de uma categoria: fica a melhor posição.
                if (!candidates.TryGetValue(entry.Id, out var existing) || candidate.Position < existing.Position)
                {
                    candidates[entry.Id] = candidate;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return candidates;
    }

    /// <summary>Quem sobe no ranking passa na frente; entre os demais, a melhor posição.</summary>
    private static List<Candidate> PickCandidates(Dictionary<string, Candidate> candidates, int count) =>
    [
        .. candidates.Values
            .OrderByDescending(c => c.PositionGain ?? int.MinValue)
            .ThenBy(c => c.Position)
            .ThenBy(c => c.ProductId, StringComparer.Ordinal)
            .Take(count)
    ];

    // ------------------------------------------------------------------------------------
    // 2. Anúncios dos candidatos
    // ------------------------------------------------------------------------------------

    private async Task<Dictionary<string, List<ListingSnapshot>>> CollectListingsAsync(
        LeitorDbContext db, CollectionCycle cycle, List<Candidate> picked, DateTimeOffset now, List<string> errors, CancellationToken cancellationToken)
    {
        var collected = new Dictionary<string, List<ListingSnapshot>>();

        foreach (var candidate in picked)
        {
            IReadOnlyList<ProductListing> listings;
            try
            {
                listings = await mercadoLivre.GetProductListingsAsync(candidate.ProductId, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                errors.Add($"anúncios {candidate.ProductId}: {exception.Message}");
                continue;
            }

            var product = await db.Products.SingleAsync(p => p.Id == candidate.ProductId, cancellationToken);
            product.Name ??= await mercadoLivre.GetProductNameAsync(candidate.ProductId, cancellationToken);

            var known = await db.SeenSellers
                .Where(s => s.ProductId == candidate.ProductId)
                .Select(s => s.SellerId)
                .ToHashSetAsync(cancellationToken);

            var snapshots = new List<ListingSnapshot>();
            foreach (var listing in listings.DistinctBy(l => l.ItemId))
            {
                var snapshot = ToSnapshot(cycle, candidate.ProductId, listing, now);
                snapshots.Add(snapshot);

                if (known.Add(snapshot.SellerId))
                {
                    db.SeenSellers.Add(new SeenSeller { ProductId = candidate.ProductId, SellerId = snapshot.SellerId, FirstSeenAt = now });
                }
            }

            db.Listings.AddRange(snapshots);
            collected[candidate.ProductId] = snapshots;
        }

        await db.SaveChangesAsync(cancellationToken);
        return collected;
    }

    // ------------------------------------------------------------------------------------
    // 3. Corte
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Finalistas pela subida no ranking de mais vendidos — dado que a coleta do §8.1 já trouxe,
    /// sem gastar chamada nenhuma.
    /// <para>
    /// Antes isto usava visitas acumuladas de TODOS os anúncios. Não dá mais: o /visits/items
    /// aceita um id por chamada, então varrer ~2.500 anúncios custaria 2.500 requisições por
    /// ciclo só para ordenar. O corte é apenas triagem; a demanda que vai para o card continua
    /// saindo da série diária de visitas, medida depois, só para os finalistas.
    /// </para>
    /// </summary>
    private static List<string> PickFinalists(
        Dictionary<string, List<ListingSnapshot>> listings, Dictionary<string, Candidate> candidates, int count) =>
    [
        .. listings.Keys
            .Select(productId => candidates[productId])
            .OrderByDescending(candidate => candidate.PositionGain ?? int.MinValue)
            .ThenBy(candidate => candidate.Position)
            .ThenBy(candidate => candidate.ProductId, StringComparer.Ordinal)
            .Take(count)
            .Select(candidate => candidate.ProductId)
    ];

    // ------------------------------------------------------------------------------------
    // 4. Análise de um finalista
    // ------------------------------------------------------------------------------------

    private async Task AnalyzeAsync(
        LeitorDbContext db,
        CollectionCycle cycle,
        string productId,
        List<ListingSnapshot> listings,
        Candidate ranking,
        EffectiveConfiguration config,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var parameters = config.Parameters.Entry;
        var versions = MarginAssessor.Versions(config);

        // Preço de mercado. Todos os anúncios vêm de /products/{id}/items, então a âncora de
        // catálogo (I5) é o próprio produto; e como kit com outra quantidade é OUTRO produto de
        // catálogo, a unidade por pacote é 1.
        // O official_store_id já vinha sendo coletado e ficava parado no banco. Ele é o que
        // distingue "a marca vende isto" de "há um concorrente a mais": contra a loja oficial
        // o revendedor não compete em preço, porque ela compra de si mesma.
        var sellerListings = listings
            .Select(l => new SellerListing(
                l.SellerId, l.ItemId, l.Price, productId, 1, l.Condition == "new",
                IsOfficialStore: l.OfficialStoreId is not null))
            .ToList();

        var previousWeek = await db.Analyses
            .Where(a => a.ProductId == productId && a.CalculatedAt <= now.AddDays(-7))
            .OrderByDescending(a => a.CalculatedAt)
            .Select(a => a.PriceJson)
            .FirstOrDefaultAsync(cancellationToken);

        var previousPrice = AnalysisJson.Read<PricePayload>(previousWeek)?.Result;
        var price = PriceReferenceCalculator.Calculate(
            productId, sellerListings, config.PriceReference,
            previousPrice is null ? null : new PriceReferenceHistory(previousPrice.MarketPrice, previousPrice.ModeStrength));

        // A comissão depende da categoria FOLHA do anúncio (ex.: MLB1055), não da categoria de
        // topo do ranking (MLB1051). Vale a mais comum entre os anúncios do produto.
        var feeCategory = listings
            .Where(l => l.CategoryId is not null)
            .GroupBy(l => l.CategoryId!)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

        // Tarifas da categoria: consultadas para TODO finalista, com ou sem fornecedor, e gravadas
        // com a análise. É o que permite recalcular a margem depois sem chamar o Mercado Livre.
        var fees = feeCategory is null ? null : await feeTables.GetFeesAsync(parameters, feeCategory, cancellationToken);

        // Demanda.
        var (demand, seriesItems, fallback) = await MeasureDemandAsync(db, productId, listings, ranking, parameters, config.Demand, today, now, cancellationToken);

        // Concorrência. Vendedores do ciclo atual que não estavam no ciclo ANTERIOR do mesmo
        // produto. Sem ciclo anterior o número é desconhecido — null, não zero: "nunca medimos"
        // e "medimos e não houve entrante" são coisas diferentes na hora de filtrar.
        var currentSellers = listings.Select(l => l.SellerId).Distinct().ToList();
        var sellers = currentSellers.Count;

        var previousCycleId = await db.Listings
            .Where(l => l.ProductId == productId && l.CycleId != cycle.Id)
            .OrderByDescending(l => l.CollectedAt)
            .Select(l => (long?)l.CycleId)
            .FirstOrDefaultAsync(cancellationToken);

        int? newSellers = null;
        if (previousCycleId is { } previousCycle)
        {
            var sellersBefore = await db.Listings
                .Where(l => l.ProductId == productId && l.CycleId == previousCycle)
                .Select(l => l.SellerId)
                .Distinct()
                .ToListAsync(cancellationToken);

            newSellers = currentSellers.Except(sellersBefore, StringComparer.Ordinal).Count();
        }

        // Margem — só existe com fornecedor e com preço de mercado.
        var marginPayload = await margins.AssessAsync(db, productId, price, feeCategory, ranking.CategoryId, fees, config, today, cancellationToken);
        var worstMargin = marginPayload?.Margin?.WorstInRange.Margin;

        // Teto de compra — não depende de fornecedor. É o que dá ao card um número acionável
        // desde o primeiro ciclo: quanto vale a pena pagar por uma unidade.
        var ceilingPayload = MarginAssessor.Ceiling(price, feeCategory, ranking.CategoryId, fees, config, now);
        // Sem ciclo anterior não há crescimento de oferta a medir: entra como zero no índice,
        // que é o valor neutro (f_oferta = 1), e não como suposição de que ninguém entrou.
        var opportunity = MarginAssessor.Opportunity(demand, price, sellers, newSellers ?? 0, marginPayload, config);

        // Condição e frete são propriedades do ANÚNCIO; o card é o produto. A semântica é
        // "existe ao menos um assim", que é o que interessa a quem procura oportunidade.
        var growth = VisitGrowth.From(demand, config.Demand);

        // Eixo do tempo: quanto de janela resta, e por quais canais ainda dá tempo de comprar.
        var runway = MarginAssessor.Runway(demand, config);

        // Certificação e kit: não custam chamada, saem da categoria e das tarifas já gravadas.
        var risks = MarginAssessor.Risks(price, ranking.CategoryId, fees, marginPayload, config);

        db.Analyses.Add(new ProductAnalysis
        {
            CycleId = cycle.Id,
            ProductId = productId,
            CalculatedAt = now,
            PriceStatus = price.Status.ToString(),
            MarketPrice = price.MarketPrice,
            PriceConfidence = price.Confidence?.ToString(),
            SellersFound = price.SellersFound,
            NewSellers = newSellers,
            VisitGrowthPercent = growth.Percent,
            IsNewDemand = growth.IsNewDemand,
            LastWeekVisits = growth.LastWeekVisits,
            HasFreeShipping = listings.Exists(l => l.FreeShipping),
            HasNewCondition = listings.Exists(l => l.Condition == "new"),
            HasUsedCondition = listings.Exists(l => l.Condition == "used"),
            DemandScore = demand.Score,
            DemandSource = demand.Source.ToString(),
            LowDemand = demand.LowDemand,
            WorstMargin = worstMargin,
            // Sem preço calculado não há como medir pressão de preço nem margem: o índice
            // ficaria otimista por falta de dado (I6). O produto vai para o fim da lista.
            OpportunityIndex = price.Status == PriceStatus.Calculated ? opportunity.Index : null,
            Quadrant = opportunity.Quadrant.ToString(),
            Explanation = WhySellingNow.Explain(demand, price, sellers, newSellers, growth),
            PriceJson = AnalysisJson.Write(new PricePayload(price, feeCategory, fees)),
            DemandJson = AnalysisJson.Write(new DemandPayload(demand, seriesItems, fallback, growth)),
            MarginJson = marginPayload is null ? null : AnalysisJson.Write(marginPayload),
            CeilingJson = ceilingPayload is null ? null : AnalysisJson.Write(ceilingPayload),
            RunwayJson = AnalysisJson.Write(runway),
            RiskJson = AnalysisJson.Write(risks),
            RunwayWeeksLower = runway.Runway.LowerWeeks,
            DemandPhase = runway.Runway.Phase.ToString(),
            WorthImporting = runway.Runway.LowerWeeks is null ? null : runway.WorthImporting,
            OpportunityJson = AnalysisJson.Write(new OpportunityPayload(opportunity, versions))
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(DemandResult Demand, IReadOnlyList<string> Items, string? Fallback)> MeasureDemandAsync(
        LeitorDbContext db,
        string productId,
        List<ListingSnapshot> listings,
        Candidate ranking,
        ParametersEntry parameters,
        DemandOptions options,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Quais anúncios representam o produto? Os mais visitados. Como cada consulta de
        // visitas custa uma chamada, mede-se uma amostra limitada (ordenada por item_id, que é
        // estável entre ciclos) e a série diária fica com os melhores dela.
        var sample = listings
            .OrderBy(l => l.ItemId, StringComparer.Ordinal)
            .Take(parameters.VisitSampleSize)
            .ToList();

        foreach (var listing in sample)
        {
            try
            {
                var totals = await mercadoLivre.GetTotalVisitsAsync([listing.ItemId], cancellationToken);
                if (totals.TryGetValue(listing.ItemId, out var visits)) listing.TotalVisits = visits;
            }
            catch (HttpRequestException)
            {
                // Anúncio sem total fica atrás na escolha, mas não derruba o produto.
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var items = sample
            .OrderByDescending(l => l.TotalVisits ?? 0)
            .ThenBy(l => l.ItemId, StringComparer.Ordinal)
            .Take(parameters.ListingsPerProductForSeries)
            .Select(l => l.ItemId)
            .ToList();

        var window = apiOptions.Value.VisitWindowDays;
        var fetchedFrom = today.AddDays(-window);
        var fetchedTo = today.AddDays(-1);

        foreach (var item in items)
        {
            var days = await mercadoLivre.GetDailyVisitsAsync(item, window, cancellationToken);
            await StoreVisitsAsync(db, item, days, fetchedFrom, fetchedTo, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Só entra na série o trecho coberto por TODOS os anúncios escolhidos — fora da
        // cobertura, dia sem linha é desconhecido, não zero.
        var coverage = await db.VisitCoverage.Where(c => items.Contains(c.ItemId)).ToListAsync(cancellationToken);
        string? fallback = null;

        if (items.Count > 0 && coverage.Count == items.Count)
        {
            var maxWeeks = options.PersistenceWindowWeeks + 1;
            var from = coverage.Max(c => c.CoveredFrom);
            var to = coverage.Min(c => c.CoveredTo);
            var start = new[] { from, to.AddDays(-(maxWeeks * 7) + 1) }.Max();
            var length = to.DayNumber - start.DayNumber + 1;

            if (length >= 14)
            {
                var rows = await db.Visits
                    .Where(v => items.Contains(v.ItemId) && v.Date >= start && v.Date <= to)
                    .Select(v => new DailyCount(v.Date, v.Visits))
                    .ToListAsync(cancellationToken);

                var series = DemandCalculator.NormalizeSeries(rows, to.AddDays(1), length);
                var fromVisits = DemandCalculator.FromVisits(series, options);
                if (fromVisits.Source == DemandSource.Visits) return (fromVisits, items, null);
            }

            fallback = $"série coberta de só {Math.Max(0, length)} dias; são necessários 14";
        }
        else
        {
            fallback = "sem série de visitas para os anúncios do produto";
        }

        // Proxy por posição no ranking, com a fonte marcada.
        var weekAgo = now.AddDays(-7);
        var positionWeekAgo = await db.Highlights
            .Where(h => h.ProductId == productId && h.CategoryId == ranking.CategoryId && h.CollectedAt <= weekAgo)
            .OrderByDescending(h => h.CollectedAt)
            .Select(h => (int?)h.Position)
            .FirstOrDefaultAsync(cancellationToken);

        return (DemandCalculator.FromRanking(ranking.Position, positionWeekAgo, options), items, fallback);
    }

    private static async Task StoreVisitsAsync(
        LeitorDbContext db, string itemId, IReadOnlyList<DailyCount> days, DateOnly fetchedFrom, DateOnly fetchedTo, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Visits
            .Where(v => v.ItemId == itemId && v.Date >= fetchedFrom && v.Date <= fetchedTo)
            .ToDictionaryAsync(v => v.Date, cancellationToken);

        foreach (var day in days.Where(d => d.Date >= fetchedFrom && d.Date <= fetchedTo))
        {
            if (existing.TryGetValue(day.Date, out var row))
            {
                row.Visits = day.Visits;
                row.UpdatedAt = now;
            }
            else
            {
                db.Visits.Add(new DailyVisits { ItemId = itemId, Date = day.Date, Visits = day.Visits, UpdatedAt = now });
            }
        }

        var coverage = await db.VisitCoverage.FindAsync([itemId], cancellationToken);
        if (coverage is null)
        {
            db.VisitCoverage.Add(new VisitCoverage { ItemId = itemId, CoveredFrom = fetchedFrom, CoveredTo = fetchedTo, UpdatedAt = now });
        }
        else if (fetchedFrom <= coverage.CoveredTo.AddDays(1))
        {
            // Emenda com o que já estava coberto: a faixa cresce.
            coverage.CoveredFrom = fetchedFrom < coverage.CoveredFrom ? fetchedFrom : coverage.CoveredFrom;
            coverage.CoveredTo = fetchedTo > coverage.CoveredTo ? fetchedTo : coverage.CoveredTo;
            coverage.UpdatedAt = now;
        }
        else
        {
            // Buraco entre a cobertura antiga e a nova: recomeça. Buraco nunca vira zero.
            coverage.CoveredFrom = fetchedFrom;
            coverage.CoveredTo = fetchedTo;
            coverage.UpdatedAt = now;
        }
    }
}
