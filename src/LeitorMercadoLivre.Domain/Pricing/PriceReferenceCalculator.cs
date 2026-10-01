using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LeitorMercadoLivre.Domain.Pricing;

/// <summary>
/// Encontra o preço em que mais vendedores DIFERENTES convergem.
/// <para>
/// Média aritmética nunca: ela é puxada por qualquer anúncio absurdo nas pontas, e o que
/// interessa aqui não é o preço médio do mercado, é o preço que o mercado pratica.
/// </para>
/// <para>
/// Classe pura, sem I/O e sem relógio: as mesmas entradas produzem sempre a mesma saída,
/// que é o que torna o <see cref="PriceReferenceResult.SampleHash"/> útil para auditoria.
/// </para>
/// </summary>
public static class PriceReferenceCalculator
{
    public static PriceReferenceResult Calculate(
        string? productAnchorId,
        IReadOnlyCollection<SellerListing> listings,
        PriceReferenceOptions options,
        PriceReferenceHistory? previous = null)
    {
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var discarded = new List<DiscardedListing>();

        // Sem âncora de catálogo não há amostra: seria um monte de anúncio parecido, não o
        // mesmo produto. Melhor não ter preço do que ter um preço que mistura produtos.
        if (string.IsNullOrWhiteSpace(productAnchorId))
        {
            return Blocked(PriceStatus.NoProductAnchor, 0, discarded);
        }

        var sample = NormalizeSample(productAnchorId, listings, discarded);
        var sampleHash = ComputeSampleHash(sample);

        if (sample.Count < options.MinSellersToCalculate)
        {
            return Blocked(PriceStatus.InsufficientData, sample.Count, discarded, sampleHash);
        }

        // Etapa 1 — baldes. A partir daqui trabalha-se com o valor do balde, não com o
        // preço original: é o agrupamento que revela a convergência entre vendedores.
        var bucketed = sample
            .Select(entry => entry.UnitPrice.ToBucket(options))
            .OrderBy(bucket => bucket)
            .ToList();

        // Etapa 2 — corte dos extremos.
        var flags = PriceFlags.None;
        var trimPerSide = bucketed.Count >= options.MinSellersForTrim
            ? (int)decimal.Floor(options.TrimRatio * bucketed.Count)
            : 0;

        if (trimPerSide == 0 && bucketed.Count < options.MinSellersForTrim)
        {
            flags |= PriceFlags.SmallSample;
        }

        var clean = bucketed
            .Skip(trimPerSide)
            .Take(bucketed.Count - (2 * trimPerSide))
            .ToList();

        // Etapa 3 — moda por vendedor, validada pela mediana.
        var median = LowerMedian(clean);
        var cleanLow = clean[0];
        var cleanHigh = clean[^1];

        // Cada item da lista limpa é um vendedor distinto (a Etapa 0 já deduplicou),
        // então contar itens do balde é contar vendedores do balde.
        var modal = clean
            .GroupBy(bucket => bucket)
            .Select(group => new { Bucket = group.Key, Sellers = group.Count() })
            .OrderByDescending(group => group.Sellers)
            .ThenBy(group => Math.Abs(group.Bucket - median)) // empate: o mais perto da mediana
            .ThenBy(group => group.Bucket)                    // persistindo: o mais baixo, que protege a margem
            .First();

        var modeStrength = (decimal)modal.Sellers / clean.Count;

        var strongEnough = modeStrength >= options.MinModeStrength;
        var populousEnough = modal.Sellers >= options.MinModalBucketSellers;
        var insideCleanRange = modal.Bucket >= cleanLow && modal.Bucket <= cleanHigh;

        var useMode = strongEnough && populousEnough && insideCleanRange;
        var marketPrice = useMode ? modal.Bucket : median;

        if (!useMode)
        {
            flags |= PriceFlags.DispersedMarket;
        }

        var dispersion = median == 0m ? (decimal?)null : (cleanHigh - cleanLow) / median;

        // Quanto da prateleira é da própria marca?
        //
        // Isto não muda o preço de mercado — ele continua sendo o que o mercado pratica. O que
        // muda é a LEITURA: contra uma marca que ocupa metade dos anúncios, o revendedor briga
        // por visibilidade antes de brigar por preço, e o card precisa dizer isso antes de
        // alguém comprar estoque.
        //
        // A contagem é sobre os anúncios ORIGINAIS, não sobre a amostra deduplicada: a marca
        // costuma ser um vendedor só com muitos anúncios, e é a prateleira que o comprador vê.
        var allListings = listings as IReadOnlyCollection<SellerListing> ?? [.. listings];
        var officialListings = allListings.Count(listing => listing.IsOfficialStore);
        var officialShare = allListings.Count == 0 ? 0m : (decimal)officialListings / allListings.Count;

        var officialSellers = sample.Count(entry => entry.IsOfficialStore);
        var officialPrice = officialSellers == 0
            ? (decimal?)null
            : sample.Where(entry => entry.IsOfficialStore).Min(entry => entry.UnitPrice);

        if (officialShare >= options.BrandDominationShare)
        {
            flags |= PriceFlags.BrandDominated;
        }

        return new PriceReferenceResult
        {
            Status = PriceStatus.Calculated,
            SampleSize = clean.Count,
            SellersFound = sample.Count,
            OfficialStoreSellers = officialSellers,
            OfficialStoreShare = officialShare,
            OfficialStorePrice = officialPrice,
            Discarded = discarded,
            SampleHash = sampleHash,
            Flags = flags,
            MarketPrice = marketPrice,
            Source = useMode ? PriceSource.Mode : PriceSource.Median,
            Mode = modal.Bucket,
            Median = median,
            ModeStrength = modeStrength,
            ModalBucketSellers = modal.Sellers,
            CleanRangeLow = cleanLow,
            CleanRangeHigh = cleanHigh,
            SuggestedShowcasePrice = Math.Max(0m, marketPrice - options.ShowcaseDiscount),
            DispersionIndex = dispersion,
            Confidence = Rate(clean.Count, dispersion, options),
            WeeklyMarketPriceChange = RelativeChange(marketPrice, previous?.MarketPrice),
            WeeklyModeStrengthChange = RelativeChange(modeStrength, previous?.ModeStrength)
        };
    }

    /// <summary>
    /// Etapa 0 — só anúncio novo, ativo, com estoque e do mesmo produto; kit convertido
    /// para preço por unidade; e um anúncio por vendedor, o mais barato.
    /// </summary>
    private static List<SellerUnitPrice> NormalizeSample(
        string productAnchorId,
        IEnumerable<SellerListing> listings,
        List<DiscardedListing> discarded)
    {
        var eligible = new List<(SellerListing Listing, decimal UnitPrice)>();

        foreach (var listing in listings)
        {
            var reason = Reject(listing, productAnchorId);
            if (reason is not null)
            {
                discarded.Add(new DiscardedListing(listing.ListingId, listing.SellerId, reason.Value));
                continue;
            }

            // Divisão sem arredondar: arredondar aqui e de novo no balde seria arredondar
            // duas vezes em cima do mesmo número.
            eligible.Add((listing, listing.Price / listing.UnitsPerPack));
        }

        var sample = new List<SellerUnitPrice>();

        foreach (var perSeller in eligible.GroupBy(entry => entry.Listing.SellerId, StringComparer.Ordinal))
        {
            var ordered = perSeller
                .OrderBy(entry => entry.UnitPrice)
                .ThenBy(entry => entry.Listing.ListingId, StringComparer.Ordinal) // desempate estável
                .ToList();

            // O vendedor é loja oficial se QUALQUER anúncio dele for: a marca pode ter um
            // anúncio oficial e outros nem tanto, e continua sendo a marca.
            sample.Add(new SellerUnitPrice(
                perSeller.Key,
                ordered[0].UnitPrice,
                ordered.Exists(entry => entry.Listing.IsOfficialStore)));

            foreach (var loser in ordered.Skip(1))
            {
                discarded.Add(new DiscardedListing(
                    loser.Listing.ListingId,
                    loser.Listing.SellerId,
                    DiscardReason.DuplicateSeller));
            }
        }

        return sample;
    }

    private static DiscardReason? Reject(SellerListing listing, string productAnchorId) => listing switch
    {
        { IsNew: false } => DiscardReason.NotNew,
        { IsActive: false } => DiscardReason.Inactive,
        { HasStock: false } => DiscardReason.NoStock,
        { UnitsPerPack: < 1 } => DiscardReason.InvalidPack,
        { Price: <= 0 } => DiscardReason.InvalidPack,
        _ when !string.Equals(listing.ProductAnchorId, productAnchorId, StringComparison.Ordinal)
            => DiscardReason.DifferentProduct,
        _ => null
    };

    /// <summary>Mediana com o central mais baixo quando a quantidade é par.</summary>
    private static decimal LowerMedian(IReadOnlyList<decimal> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : sorted[(sorted.Count / 2) - 1];

    private static PriceConfidence Rate(int sampleSize, decimal? dispersion, PriceReferenceOptions options)
    {
        if (sampleSize < options.LowConfidenceSellerThreshold ||
            dispersion is null ||
            dispersion > options.LowConfidenceDispersionThreshold)
        {
            return PriceConfidence.Low;
        }

        return sampleSize >= options.HighConfidenceMinSellers && dispersion < options.HighConfidenceMaxDispersion
            ? PriceConfidence.High
            : PriceConfidence.Medium;
    }

    private static decimal? RelativeChange(decimal current, decimal? previous) =>
        previous is null or 0m ? null : (current - previous.Value) / previous.Value;

    private static PriceReferenceResult Blocked(
        PriceStatus status,
        int sellersFound,
        IReadOnlyList<DiscardedListing> discarded,
        string? sampleHash = null) => new()
        {
            Status = status,
            SampleSize = 0,
            SellersFound = sellersFound,
            Discarded = discarded,
            SampleHash = sampleHash ?? ComputeSampleHash([])
        };

    private static string ComputeSampleHash(IEnumerable<SellerUnitPrice> sample)
    {
        var payload = string.Join(
            '|',
            sample
                .OrderBy(entry => entry.SellerId, StringComparer.Ordinal)
                .Select(entry => $"{entry.SellerId}:{entry.UnitPrice.ToString(CultureInfo.InvariantCulture)}"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private readonly record struct SellerUnitPrice(string SellerId, decimal UnitPrice, bool IsOfficialStore);
}

internal static class BucketExtensions
{
    /// <summary>
    /// Arredonda para o balde MAIS PRÓXIMO, com o empate indo para cima.
    /// <para>
    /// Para cima sempre enviesaria o preço de mercado (R$ 23,40 viraria R$ 24) e inflaria o
    /// lucro exibido. Truncar separaria R$ 19,99 de R$ 20,00, que é exatamente o mesmo preço
    /// na cabeça de quem compra.
    /// </para>
    /// <para>
    /// Meia-para-par (banker's) seria mais neutro no valor, mas aqui o arredondamento decide
    /// EM QUE BALDE o vendedor cai, e preço terminado em ,50 é comum no varejo: o balde par
    /// recolheria os dois midpoints vizinhos (19,50 e 20,50 → 20) e o ímpar nenhum, então
    /// baldes pares ganhariam a moda pela paridade. Meia-para-cima mantém todo balde com a
    /// mesma largura, em [X − 0,50, X + 0,50).
    /// </para>
    /// </summary>
    internal static decimal ToBucket(this decimal price, PriceReferenceOptions options) =>
        price <= options.WholeBucketLimit
            ? Math.Round(price, 0, MidpointRounding.AwayFromZero)
            : Math.Round(price / options.CoarseBucketStep, 0, MidpointRounding.AwayFromZero) * options.CoarseBucketStep;
}
