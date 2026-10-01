using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LeitorMercadoLivre.Domain.Margin;
using LeitorMercadoLivre.Infrastructure.Configuration;
using LeitorMercadoLivre.Infrastructure.MercadoLivre;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeitorMercadoLivre.Infrastructure.Pricing;

/// <summary>PTAX de venda do Banco Central (API Olinda, pública).</summary>
public sealed class PtaxClient(HttpClient http)
{
    /// <summary>Quantos dias voltar procurando cotação: cobre fim de semana prolongado e feriado.</summary>
    private const int MaxDaysBack = 10;

    /// <summary>
    /// A cotação do dia pedido ou, se não houver (fim de semana, feriado, antes das 13h), a
    /// do último dia útil anterior. O dia efetivamente usado volta junto, para a memória de cálculo.
    /// </summary>
    public async Task<(DateOnly QuoteDate, decimal Sell)?> GetUsdSellAsync(DateOnly date, CancellationToken cancellationToken)
    {
        for (var back = 0; back < MaxDaysBack; back++)
        {
            var day = date.AddDays(-back);
            var formatted = day.ToString("MM-dd-yyyy", CultureInfo.InvariantCulture);
            var url = "olinda/servico/PTAX/versao/v1/odata/CotacaoDolarDia(dataCotacao=@dataCotacao)" +
                      $"?@dataCotacao='{formatted}'&$format=json&$select=cotacaoVenda,dataHoraCotacao";

            var response = await http.GetFromJsonAsync<PtaxResponse>(url, cancellationToken);
            var quote = response?.Value.LastOrDefault();

            if (quote is not null) return (day, quote.CotacaoVenda);
        }

        return null;
    }

    private sealed record PtaxResponse([property: JsonPropertyName("value")] List<PtaxQuote> Value);

    private sealed record PtaxQuote([property: JsonPropertyName("cotacaoVenda")] decimal CotacaoVenda);
}

/// <summary>PTAX com cache no banco (TTL de um dia, §4).</summary>
public sealed class ExchangeRateService(IDbContextFactory<LeitorDbContext> contexts, PtaxClient ptax, TimeProvider clock)
{
    /// <summary>Cotação provisória (de dia anterior) é reconsultada depois deste prazo.</summary>
    private static readonly TimeSpan ProvisionalTtl = TimeSpan.FromHours(6);

    public async Task<ExchangeRateQuote> GetUsdAsync(DateOnly requested, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var cached = await db.ExchangeRates.FindAsync([(object)"USD", requested], cancellationToken);

        // A cotação do próprio dia é definitiva. A de um dia anterior é provisória: a PTAX de
        // hoje sai por volta das 13h e deve substituí-la.
        var fresh = cached is not null &&
            (cached.QuoteDate == requested || clock.GetUtcNow() - cached.FetchedAt < ProvisionalTtl);
        if (fresh) return cached!;

        var quote = await ptax.GetUsdSellAsync(requested, cancellationToken)
            ?? throw new InvalidOperationException($"Sem PTAX nos 10 dias anteriores a {requested:dd/MM/yyyy}.");

        cached ??= new ExchangeRateQuote { Currency = "USD", RequestedDate = requested };
        cached.QuoteDate = quote.QuoteDate;
        cached.Sell = quote.Sell;
        cached.FetchedAt = clock.GetUtcNow();

        if (db.Entry(cached).State == EntityState.Detached) db.ExchangeRates.Add(cached);
        await db.SaveChangesAsync(cancellationToken);
        return cached;
    }
}

/// <summary>
/// Monta a <see cref="SaleCostSchedule"/> perguntando ao próprio Mercado Livre.
/// <para>
/// Os pontos onde a tarifa muda (29, 50, 79) vêm da configuração; o VALOR de cada faixa vem
/// da API, consultando um preço dentro dela, com a categoria — a comissão varia por
/// categoria. Só o dado da API vai para o cache (TTL de um dia, §4); frete, limiar e imposto
/// vêm dos parâmetros vigentes a cada chamada, para uma mudança de configuração valer na hora.
/// </para>
/// </summary>
public sealed class SaleScheduleProvider(MercadoLivreClient client, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

    /// <summary>A tabela pronta: faixas da API (em cache) com frete, limiar e imposto dos parâmetros.</summary>
    public async Task<SaleCostSchedule> GetAsync(ParametersEntry parameters, string? categoryId, CancellationToken cancellationToken) =>
        Build(await GetFeesAsync(parameters, categoryId, cancellationToken), parameters);

    /// <summary>Monta a tabela a partir de faixas já consultadas — sem chamar a API.</summary>
    public static SaleCostSchedule Build(FeeSnapshot fees, ParametersEntry parameters, decimal packagingPerUnit = 0m) =>
        new(fees.Bands, parameters.FreeShippingThreshold, parameters.AbsorbedFreightPerUnit, parameters.SaleTaxRate, packagingPerUnit);

    /// <summary>As faixas de tarifa da categoria, consultadas no Mercado Livre (cache de um dia).</summary>
    public async Task<FeeSnapshot> GetFeesAsync(ParametersEntry parameters, string? categoryId, CancellationToken cancellationToken)
    {
        var limits = parameters.FeeBandLimits.Where(limit => limit > 1m).Distinct().Order().ToList();
        var key = $"sale-fees:{parameters.ListingTypeId}:{categoryId}:{string.Join('|', limits)}";

        if (!cache.TryGetValue(key, out List<SaleFeeBand>? bands) || bands is null)
        {
            bands = [];
            foreach (var limit in limits)
            {
                var fee = await FeeAtAsync(parameters.ListingTypeId, limit - 1m, categoryId, cancellationToken);
                bands.Add(new SaleFeeBand(limit, fee.PercentageRate, fee.SaleFeeDetails.FixedFee));
            }

            // Última faixa, aberta: consulta um preço confortavelmente acima do último ponto.
            var topLimit = limits.Count > 0 ? limits[^1] : parameters.FreeShippingThreshold;
            var above = await FeeAtAsync(parameters.ListingTypeId, topLimit + 21m, categoryId, cancellationToken);
            bands.Add(new SaleFeeBand(decimal.MaxValue, above.PercentageRate, above.SaleFeeDetails.FixedFee));

            cache.Set(key, bands, Ttl);
        }

        return new FeeSnapshot(parameters.ListingTypeId, limits, bands);
    }

    private async Task<ListingPrice> FeeAtAsync(string listingTypeId, decimal probe, string? categoryId, CancellationToken cancellationToken)
    {
        var prices = await client.GetListingPricesAsync(probe, categoryId, cancellationToken);

        return prices.FirstOrDefault(price => price.ListingTypeId == listingTypeId)
            ?? throw new InvalidOperationException(
                $"listing_prices não devolveu o tipo '{listingTypeId}' para R$ {probe} em {categoryId}.");
    }
}
