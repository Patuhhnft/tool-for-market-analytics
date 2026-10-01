using System.Text.Json;
using System.Text.Json.Serialization;
using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Domain.Compliance;
using LeitorMercadoLivre.Domain.Margin;
using LeitorMercadoLivre.Domain.Opportunity;
using LeitorMercadoLivre.Domain.Pricing;

namespace LeitorMercadoLivre.Infrastructure.Pipeline;

/// <summary>Serialização única dos payloads jsonb: quem grava e quem lê usam as mesmas opções.</summary>
public static class AnalysisJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Read<T>(string? json) => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>Qual versão da configuração produziu o número — o que torna o número auditável (I7).</summary>
public sealed record ConfigVersionInfo(
    string ParametersFile,
    DateOnly ParametersValidFrom,
    string Regime,
    DateOnly RegimeValidFrom,
    string RegimeFile,
    string RegimeSource);

public sealed record DemandPayload(
    DemandResult Result,
    IReadOnlyList<string> ItemsInSeries,
    string? FallbackReason,
    // Como o crescimento deve ser exibido, já decidido com os limiares da configuração.
    // <c>null</c> em análise antiga, anterior a este campo.
    VisitGrowth? Growth = null);

public sealed record ExchangeInfo(DateOnly QuoteDate, decimal Ptax, decimal EffectiveRate);

public sealed record MarginPayload(
    long SupplierId,
    string SupplierName,
    decimal SupplierUnitPrice,
    string Currency,
    int ShipmentQuantity,
    ExchangeInfo? Exchange,
    LandedCost? LandedCost,
    MarginResult? Margin,
    // Maior preço no fornecedor, em moeda estrangeira, que mantém a margem-alvo em toda a faixa.
    decimal? MaxSupplierUnitPrice,
    string? FeeCategoryId,
    // Por que não há margem, quando não há. Falta de dado vira aviso (I6).
    string? Unavailable,
    ConfigVersionInfo Config,
    // Quando a margem foi avaliada: no ciclo, ou depois, ao cadastrar um fornecedor.
    DateTimeOffset AssessedAt);

public sealed record OpportunityPayload(OpportunityResult Result, ConfigVersionInfo Config);

/// <summary>
/// Riscos e custos que dependem do QUE o produto é, não do preço dele.
/// </summary>
/// <param name="Certifications">
/// Certificações que a categoria costuma exigir. É INDÍCIO para perguntar ao despachante,
/// nunca parecer: dentro de "Brinquedos" há item que precisa de selo e item que não precisa.
/// Sem certificação, o anúncio pode ser pausado e a carga retida — prejuízo total, não
/// margem menor, e nenhum outro número do sistema vê esse risco.
/// </param>
/// <param name="Kits">
/// Margem vendendo em kit. A tarifa fixa do ML é por venda: num item de R$ 20 ela come um
/// terço do preço avulso, e some num kit de cinco.
/// </param>
public sealed record ProductRiskPayload(
    IReadOnlyList<CertificationRequirement> Certifications,
    decimal PackagingPerUnit,
    decimal BreakageReserve,
    IReadOnlyList<KitOption> Kits);

/// <summary>Um canal e o veredito dele para ESTE produto.</summary>
/// <param name="Fits">
/// <c>true</c> dá tempo, <c>false</c> não dá, <c>null</c> não sabemos — série curta demais.
/// "Não sei" nunca vira "não dá": são decisões diferentes (I6).
/// </param>
public sealed record ChannelVerdict(string Name, int LeadTimeDays, bool Imported, bool? Fits);

/// <summary>
/// Quanto tempo de janela resta, e por quais canais ainda dá tempo de comprar.
/// <para>
/// É o eixo do tempo: o usuário não vende hoje, vende quando a mercadoria chega. Um produto
/// em plena aceleração hoje pode estar saturado quando a caixa desembarcar em 60 dias — e
/// esse é o erro mais caro que um importador comete.
/// </para>
/// </summary>
public sealed record RunwayPayload(
    Runway Runway,
    int MinimumSellingWindowDays,
    IReadOnlyList<ChannelVerdict> Channels)
{
    /// <summary>Algum canal importado ainda dá tempo? É o que separa os dois trilhos.</summary>
    public bool WorthImporting => Channels.Any(channel => channel.Imported && channel.Fits == true);
}

/// <summary>
/// O teto de compra do produto: quanto vale a pena pagar por uma unidade. Gravado à parte da
/// margem porque não depende de fornecedor — existe desde o primeiro ciclo, e é o número que
/// se leva para a negociação.
/// </summary>
public sealed record CeilingPayload(
    PurchaseCeiling Ceiling,
    string? FeeCategoryId,
    ConfigVersionInfo Config,
    DateTimeOffset AssessedAt);

/// <summary>
/// Tarifas de venda consultadas na coleta, para a categoria do produto. Gravadas com a análise
/// para o recálculo de margem não precisar chamar o Mercado Livre: só o Worker fala com a ML,
/// e a API não disputa o refresh token de uso único com ele.
/// </summary>
public sealed record FeeSnapshot(string ListingTypeId, IReadOnlyList<decimal> Limits, IReadOnlyList<SaleFeeBand> Bands);

public sealed record PricePayload(PriceReferenceResult Result, string? CategoryId, FeeSnapshot? Fees = null);
