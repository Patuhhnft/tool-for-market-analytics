using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Domain.Margin;
using LeitorMercadoLivre.Domain.Opportunity;
using LeitorMercadoLivre.Domain.Pricing;
using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pricing;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Infrastructure.Pipeline;

/// <summary>
/// Margem de um produto a partir do preço já coletado, do fornecedor e da configuração
/// vigente. Usado pelo ciclo e pelo "recalcular agora" — assim cadastrar um fornecedor mostra
/// a margem na hora, sem esperar 6 horas nem gastar uma coleta inteira de chamadas à API.
/// <para>
/// NÃO chama o Mercado Livre: as tarifas vêm da tabela que o ciclo consultou e gravou na
/// análise. Só o Worker fala com a ML. Se a API também falasse, os dois processos
/// disputariam o refresh token de uso único — e a segunda renovação derrubaria a coleta.
/// </para>
/// </summary>
public sealed class MarginAssessor(
    ExchangeRateService exchange,
    BusinessConfigurationStore configuration,
    TimeProvider clock)
{
    public async Task<MarginPayload?> AssessAsync(
        LeitorDbContext db,
        string productId,
        PriceReferenceResult price,
        string? feeCategory,
        string? productCategory,
        FeeSnapshot? fees,
        EffectiveConfiguration config,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers
            .Where(s => s.ProductId == productId && s.Active)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (supplier is null) return null;

        var versions = Versions(config);
        var assessedAt = clock.GetUtcNow();

        MarginPayload Unavailable(string reason, ExchangeInfo? rate = null, LandedCost? landed = null) =>
            new(supplier.Id, supplier.Name, supplier.UnitPrice, supplier.Currency, supplier.ShipmentQuantity,
                rate, landed, null, null, feeCategory, reason, versions, assessedAt);

        if (!string.Equals(supplier.Currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return Unavailable($"moeda {supplier.Currency} ainda não suportada: a PTAX consultada é a do dólar");
        }

        var points = MarketPricePoints.From(price);
        if (points is null)
        {
            return Unavailable("sem preço de mercado confiável para calcular margem");
        }

        var parameters = config.Parameters.Entry;

        if (fees is null)
        {
            return Unavailable("a tabela de tarifas da categoria não foi consultada na última coleta; a margem aparece no próximo ciclo");
        }

        if (!FeesStillMatch(fees, parameters))
        {
            return Unavailable("o tipo de anúncio ou as faixas de tarifa mudaram desde a coleta; a margem com a tabela nova aparece no próximo ciclo");
        }

        ExchangeRateQuote quote;
        try
        {
            quote = await exchange.GetUsdAsync(today, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return Unavailable($"PTAX indisponível agora ({exception.Message}); tente recalcular mais tarde");
        }

        var acquisition = new AcquisitionInput(
            supplier.UnitPrice,
            supplier.ShipmentQuantity,
            supplier.InternationalFreightTotal,
            quote.Sell,
            parameters.DomesticFreightTotal,
            config.ImportTaxRates,
            parameters.CustomsFeesTotal);

        var landed = LandedCostCalculator.Calculate(acquisition, config.Margin.ExchangeSpread);
        var rateInfo = new ExchangeInfo(quote.QuoteDate, quote.Sell, landed.EffectiveExchangeRate);

        if (landed.ExceedsRegimeLimit)
        {
            return Unavailable(
                $"a remessa (US$ {landed.CustomsValueForeign:N2}) passa do teto do regime '{config.ImportTaxRates.Regime}' " +
                $"(US$ {config.ImportTaxRates.MaxShipmentValueForeign:N2}): reduza a quantidade ou use importação formal",
                rateInfo, landed);
        }

        // Embalagem e quebra dependem do QUE o produto é. A embalagem entra no custo de venda;
        // a quebra encarece cada unidade VENDIDA, porque o que quebrou foi pago e não vendido.
        var categoryCost = config.CategoryCosts.For(productCategory);
        var schedule = SaleScheduleProvider.Build(fees, parameters, categoryCost.PackagingPerUnit);
        var unitCost = categoryCost.ApplyBreakage(landed.UnitCost);

        var margin = MarginCalculator.Calculate(unitCost, points, schedule, config.Margin);
        var maxSupplier = LandedCostCalculator.MaxSupplierUnitPrice(acquisition, margin.MaxUnitCostInRange, config.Margin.ExchangeSpread);

        return new MarginPayload(
            supplier.Id, supplier.Name, supplier.UnitPrice, supplier.Currency, supplier.ShipmentQuantity,
            rateInfo, landed, margin, maxSupplier, feeCategory, null, versions, assessedAt);
    }

    /// <summary>
    /// O teto de compra, que NÃO depende de fornecedor: preço de mercado, tarifas do Mercado
    /// Livre e margem-alvo bastam. É o que responde "quanto posso pagar por isto?" já no
    /// primeiro ciclo, antes de existir fornecedor cadastrado.
    /// <para>
    /// <c>null</c> quando a pergunta não tem resposta honesta: sem preço de mercado confiável,
    /// ou sem a tabela de tarifas da categoria. Falta de dado vira ausência, nunca um número
    /// inventado (I6).
    /// </para>
    /// </summary>
    public static CeilingPayload? Ceiling(
        PriceReferenceResult price,
        string? feeCategory,
        string? productCategory,
        FeeSnapshot? fees,
        EffectiveConfiguration config,
        DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(config);

        var points = MarketPricePoints.From(price);
        if (points is null || fees is null) return null;

        var parameters = config.Parameters.Entry;
        if (!FeesStillMatch(fees, parameters)) return null;

        // A embalagem entra aqui também: sem ela o teto diria que dá para pagar mais do que dá.
        var schedule = SaleScheduleProvider.Build(fees, parameters, config.CategoryCosts.For(productCategory).PackagingPerUnit);

        return new CeilingPayload(
            MarginCalculator.Ceiling(points, schedule, config.Margin),
            feeCategory,
            Versions(config),
            assessedAt);
    }

    /// <summary>
    /// Refaz o preço de mercado a partir dos anúncios guardados do ciclo da análise.
    /// <para>
    /// <c>null</c> quando não há anúncios guardados — análise antiga, ou vinda de um caminho
    /// que não os persistiu. Aí o preço do jsonb continua valendo: melhor manter o número
    /// auditável de antes do que apagá-lo.
    /// </para>
    /// </summary>
    private static async Task<PricePayload?> RecomputePriceAsync(
        LeitorDbContext db,
        ProductAnalysis analysis,
        PricePayload stored,
        EffectiveConfiguration config,
        CancellationToken cancellationToken)
    {
        var listings = await db.Listings.AsNoTracking()
            .Where(listing => listing.ProductId == analysis.ProductId && listing.CycleId == analysis.CycleId)
            .ToListAsync(cancellationToken);

        if (listings.Count == 0) return null;

        var sample = listings.ConvertAll(listing => new SellerListing(
            listing.SellerId, listing.ItemId, listing.Price, analysis.ProductId, 1,
            listing.Condition == "new",
            IsOfficialStore: listing.OfficialStoreId is not null));

        var result = PriceReferenceCalculator.Calculate(analysis.ProductId, sample, config.PriceReference);

        // As variações semanais são COPIADAS do valor guardado, não recalculadas. Elas dependem
        // do ciclo anterior, que não está sendo refeito aqui; reconstruir a base a partir da
        // variação daria um número aproximado, e aproximação em dinheiro é exatamente o que a
        // I3 proíbe. Preservar o que já foi medido é mais honesto que recalcular por cima.
        return stored with
        {
            Result = result with
            {
                WeeklyMarketPriceChange = stored.Result.WeeklyMarketPriceChange,
                WeeklyModeStrengthChange = stored.Result.WeeklyModeStrengthChange
            }
        };
    }

    /// <summary>
    /// Riscos e custos que dependem do que o produto é: certificação exigida pela categoria e
    /// a margem vendendo em kit.
    /// <para>
    /// Não custa chamada nenhuma — sai da categoria do anúncio e da tabela de tarifas que o
    /// ciclo já gravou.
    /// </para>
    /// </summary>
    /// <param name="productCategory">
    /// Categoria do PRODUTO, não a categoria-folha do anúncio.
    /// <para>
    /// A distinção custou um bug: a folha (<c>MLB6899</c>) é o que define a comissão, mas
    /// quem decide se o item exige selo do Inmetro ou quebra no transporte é a categoria do
    /// produto (<c>MLB1132</c>, Brinquedos). Procurar pela folha não achava nada.
    /// </para>
    /// </param>
    public static ProductRiskPayload Risks(
        PriceReferenceResult price,
        string? productCategory,
        FeeSnapshot? fees,
        MarginPayload? margin,
        EffectiveConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(config);

        var categoryCost = config.CategoryCosts.For(productCategory);

        // O kit só faz sentido com custo real: sem fornecedor não há o que diluir.
        IReadOnlyList<KitOption> kits = [];
        if (margin?.Margin is { } calculated && fees is not null &&
            FeesStillMatch(fees, config.Parameters.Entry) && price.MarketPrice is { } market)
        {
            kits = KitMarginCalculator.Evaluate(
                calculated.UnitCost,
                market,
                SaleScheduleProvider.Build(fees, config.Parameters.Entry, categoryCost.PackagingPerUnit),
                config.KitSizes);
        }

        return new ProductRiskPayload(
            config.Certifications.For(productCategory),
            categoryCost.PackagingPerUnit,
            categoryCost.BreakageReserve,
            kits);
    }

    /// <summary>
    /// Quanto tempo de janela resta e por quais canais ainda dá tempo de comprar.
    /// <para>
    /// Depende só da série de visitas e da configuração — nenhuma chamada nova ao Mercado
    /// Livre. O portao de cada canal usa o LIMITE INFERIOR do runway: se a estimativa errar,
    /// que erre para o lado de não comprar.
    /// </para>
    /// </summary>
    public static RunwayPayload Runway(DemandResult demand, EffectiveConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(config);

        var runway = RunwayCalculator.Estimate(demand.Series, config.Runway);

        return new RunwayPayload(
            runway,
            config.MinimumSellingWindowDays,
            [
                .. config.Channels.Select(channel => new ChannelVerdict(
                    channel.Name,
                    channel.LeadTimeDays,
                    channel.Imported,
                    runway.FitsLeadTime(channel.LeadTimeDays, config.MinimumSellingWindowDays)))
            ]);
    }

    /// <summary>
    /// A tabela de tarifas gravada na coleta ainda corresponde à configuração de agora? Se o
    /// tipo de anúncio ou as faixas mudaram, o número antigo não vale mais.
    /// </summary>
    private static bool FeesStillMatch(FeeSnapshot fees, ParametersEntry parameters) =>
        fees.ListingTypeId == parameters.ListingTypeId &&
        fees.Limits.SequenceEqual(parameters.FeeBandLimits.Where(limit => limit > 1m).Distinct().Order());

    /// <summary>
    /// Recalcula margem e índice da análise mais recente do produto, com o fornecedor e a
    /// configuração de agora. Não chama o Mercado Livre: preço, demanda e concorrência são os
    /// da última coleta. A versão da configuração e a PTAX usadas ficam gravadas (I7).
    /// </summary>
    public async Task<bool> RefreshLatestAsync(LeitorDbContext db, string productId, CancellationToken cancellationToken)
    {
        var analysis = await db.Analyses
            .Where(a => a.ProductId == productId)
            .OrderByDescending(a => a.CalculatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (analysis is null) return false;

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var config = configuration.ResolveAt(today).Configuration
            ?? throw new InvalidOperationException("Configuração inválida: corrija a pasta de configuração antes de recalcular.");

        var price = AnalysisJson.Read<PricePayload>(analysis.PriceJson)!;
        var demandPayload = AnalysisJson.Read<DemandPayload>(analysis.DemandJson)!;
        var demand = demandPayload.Result;

        // O preço é REFEITO a partir dos anúncios já guardados do ciclo, não reaproveitado do
        // jsonb. Custa zero chamada — os anúncios estão no nosso banco — e é o que faz uma
        // correção no cálculo valer para o histórico em vez de só para o próximo ciclo.
        //
        // Foi assim que o selo de loja oficial chegou às análises existentes: o official_store_id
        // já estava gravado em cada anúncio desde o primeiro ciclo, faltava alguém lê-lo.
        var recomputed = await RecomputePriceAsync(db, analysis, price, config, cancellationToken);
        if (recomputed is not null)
        {
            price = recomputed;
            analysis.PriceJson = AnalysisJson.Write(price);
            analysis.PriceStatus = price.Result.Status.ToString();
            analysis.MarketPrice = price.Result.MarketPrice;
            analysis.PriceConfidence = price.Result.Confidence?.ToString();
            analysis.SellersFound = price.Result.SellersFound;
        }

        // O crescimento é reinterpretado com os limiares de agora, e o texto refeito. Sem isto,
        // uma análise gravada antes só se corrigiria no próximo ciclo — e ciclo custa chamada.
        var growth = VisitGrowth.From(demand, config.Demand);

        var productCategory = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId).Select(p => p.CategoryId).FirstOrDefaultAsync(cancellationToken);

        var margin = await AssessAsync(db, productId, price.Result, price.CategoryId, productCategory, price.Fees, config, today, cancellationToken);
        var opportunity = Opportunity(demand, price.Result, analysis.SellersFound, analysis.NewSellers ?? 0, margin, config);

        // O teto acompanha: mudar a margem-alvo ou o imposto sobre a venda muda quanto vale a
        // pena pagar, mesmo sem fornecedor nenhum envolvido.
        var ceiling = Ceiling(price.Result, price.CategoryId, productCategory, price.Fees, config, clock.GetUtcNow());
        analysis.CeilingJson = ceiling is null ? null : AnalysisJson.Write(ceiling);

        var risks = Risks(price.Result, productCategory, price.Fees, margin, config);
        analysis.RiskJson = AnalysisJson.Write(risks);

        var runway = Runway(demand, config);
        analysis.RunwayJson = AnalysisJson.Write(runway);
        analysis.RunwayWeeksLower = runway.Runway.LowerWeeks;
        analysis.DemandPhase = runway.Runway.Phase.ToString();
        analysis.WorthImporting = runway.Runway.LowerWeeks is null ? null : runway.WorthImporting;

        analysis.DemandJson = AnalysisJson.Write(demandPayload with { Growth = growth });
        analysis.VisitGrowthPercent = growth.Percent;
        analysis.IsNewDemand = growth.IsNewDemand;
        analysis.Explanation = WhySellingNow.Explain(demand, price.Result, analysis.SellersFound, analysis.NewSellers, growth);
        analysis.MarginJson = margin is null ? null : AnalysisJson.Write(margin);
        analysis.WorstMargin = margin?.Margin?.WorstInRange.Margin;
        analysis.OpportunityIndex = price.Result.Status == PriceStatus.Calculated ? opportunity.Index : null;
        analysis.Quadrant = opportunity.Quadrant.ToString();
        analysis.OpportunityJson = AnalysisJson.Write(new OpportunityPayload(opportunity, Versions(config)));

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static OpportunityResult Opportunity(
        DemandResult demand,
        PriceReferenceResult price,
        int sellers,
        int newSellers,
        MarginPayload? margin,
        EffectiveConfiguration config) =>
        OpportunityCalculator.Calculate(
            new OpportunityInput(
                demand.Score, demand.Persistence, demand.Falling, sellers, newSellers,
                price.WeeklyMarketPriceChange, price.DispersionIndex, price.WeeklyModeStrengthChange,
                margin?.Margin?.WorstInRange.Margin),
            config.Opportunity);

    public static ConfigVersionInfo Versions(EffectiveConfiguration config) =>
        new(config.Parameters.File, config.Parameters.Entry.ValidFrom,
            config.TaxRule.Entry.Regime, config.TaxRule.Entry.ValidFrom, config.TaxRule.File, config.TaxRule.Entry.Source);
}
