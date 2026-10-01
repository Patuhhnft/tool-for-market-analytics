namespace LeitorMercadoLivre.Infrastructure.Persistence;

// Entidades de persistência. Ficam na Infrastructure de propósito: o domínio calcula, o
// banco guarda. As saídas completas dos calculadores vão em colunas jsonb (I7) — auditáveis
// e sem migration a cada campo novo —, e só o que o painel filtra ou ordena vira coluna.

/// <summary>
/// Um ciclo de coleta. <see cref="WindowStart"/> é o início do bloco de 6 horas e tem
/// índice único: rodar o mesmo bloco duas vezes reaproveita o ciclo em vez de duplicar (I10).
/// </summary>
public sealed class CollectionCycle
{
    public long Id { get; set; }

    public DateTimeOffset WindowStart { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string Status { get; set; } = CycleStatus.Running;

    public string? Error { get; set; }

    /// <summary>Contagens do ciclo (produtos, anúncios, chamadas), em jsonb.</summary>
    public string StatsJson { get; set; } = "{}";
}

public static class CycleStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    /// <summary>
    /// Análise pedida pela barra de pesquisa, fora do ciclo agendado.
    /// <para>
    /// Status próprio de propósito: o quadro mostra "o último ciclo CONCLUÍDO", e uma busca
    /// avulsa marcada como concluída sequestraria a tela inteira — o painel passaria a exibir
    /// um quadro de um produto só. Assim ela fica no histórico sem se passar por coleta.
    /// </para>
    /// </summary>
    public const string OnDemand = "on-demand";
}

/// <summary>Produto de catálogo — a âncora que a invariante I5 exige.</summary>
public sealed class CatalogProduct
{
    public required string Id { get; set; }

    public string? Name { get; set; }

    public required string CategoryId { get; set; }

    /// <summary>Nome da categoria, só para exibir. O filtro é sempre pelo id.</summary>
    public string? CategoryName { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>Posição no ranking BEST_SELLER numa coleta. Base do proxy de demanda por posição.</summary>
public sealed class HighlightSnapshot
{
    public long Id { get; set; }

    public long CycleId { get; set; }

    public required string CategoryId { get; set; }

    public required string ProductId { get; set; }

    public int Position { get; set; }

    public DateTimeOffset CollectedAt { get; set; }
}

/// <summary>Um anúncio do produto numa coleta — a matéria-prima do preço de mercado.</summary>
public sealed class ListingSnapshot
{
    public long Id { get; set; }

    public long CycleId { get; set; }

    public required string ProductId { get; set; }

    public required string ItemId { get; set; }

    public required string SellerId { get; set; }

    public decimal Price { get; set; }

    public required string ListingTypeId { get; set; }

    /// <summary>Categoria folha do anúncio — é ela que define a comissão, não a do ranking.</summary>
    public string? CategoryId { get; set; }

    public required string Condition { get; set; }

    public bool FreeShipping { get; set; }

    public string? LogisticType { get; set; }

    public long? OfficialStoreId { get; set; }

    public int MinPurchaseUnit { get; set; } = 1;

    /// <summary>Visitas acumuladas do anúncio na coleta. A diferença entre ciclos é a pré-seleção barata.</summary>
    public int? TotalVisits { get; set; }

    public DateTimeOffset CollectedAt { get; set; }
}

/// <summary>Primeira vez que um vendedor apareceu num produto. É o que mede entrantes na semana.</summary>
public sealed class SeenSeller
{
    public required string ProductId { get; set; }

    public required string SellerId { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
}

/// <summary>
/// Visitas de um anúncio num dia. Chave (anúncio, dia) com upsert: a série é reescrita a cada
/// coleta, e o dia que a API omitiu simplesmente não tem linha — quem lê preenche com zero.
/// </summary>
public sealed class DailyVisits
{
    public required string ItemId { get; set; }

    public DateOnly Date { get; set; }

    public int Visits { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Faixa CONTÍNUA de dias cuja série de visitas do anúncio foi de fato consultada.
/// <para>
/// A API omite os dias sem visita, então dentro desta faixa um dia sem linha é zero. FORA
/// dela é desconhecido — e tratar como zero faria a semana antiga parecer vazia e fabricaria
/// crescimento. Se uma consulta nova não emenda com a faixa (a máquina ficou desligada), a
/// faixa recomeça: buraco nunca vira zero.
/// </para>
/// </summary>
public sealed class VisitCoverage
{
    public required string ItemId { get; set; }

    public DateOnly CoveredFrom { get; set; }

    public DateOnly CoveredTo { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Tudo o que foi calculado para um produto num ciclo. Colunas para o que o painel ordena e
/// filtra; o resto, completo, em jsonb.
/// </summary>
public sealed class ProductAnalysis
{
    public long Id { get; set; }

    public long CycleId { get; set; }

    public required string ProductId { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }

    public string PriceStatus { get; set; } = "";

    public decimal? MarketPrice { get; set; }

    public string? PriceConfidence { get; set; }

    public int SellersFound { get; set; }

    /// <summary>
    /// Vendedores presentes neste ciclo e ausentes no ciclo anterior do mesmo produto.
    /// <c>null</c> quando não há ciclo anterior — que é diferente de zero (§2.6).
    /// </summary>
    public int? NewSellers { get; set; }

    public decimal DemandScore { get; set; }

    public string DemandSource { get; set; } = "";

    public bool LowDemand { get; set; }

    public decimal? WorstMargin { get; set; }

    public decimal? OpportunityIndex { get; set; }

    public string? Quadrant { get; set; }

    public string Explanation { get; set; } = "";

    // --- Campos abaixo existem para serem filtrados e ordenados em SQL (§2.11). O valor
    // completo continua no jsonb; aqui fica só o que a busca precisa alcançar.

    /// <summary>
    /// Crescimento de visitas da semana, em pontos percentuais (12,5 = +12,5%).
    /// <c>null</c> quando não dá para medir: sem semana anterior, base zerada, ou base abaixo
    /// do piso — nunca infinito (§2.4).
    /// </summary>
    public decimal? VisitGrowthPercent { get; set; }

    /// <summary>Semana anterior sem visita alguma: não há percentual, e sim demanda nova.</summary>
    public bool IsNewDemand { get; set; }

    /// <summary>Visitas da última semana completa, base do "explodindo" da demanda nova.</summary>
    public int? LastWeekVisits { get; set; }

    /// <summary>
    /// Tem ao menos um anúncio com frete grátis. É "ao menos um", não "todos": o card é o
    /// produto, e quem procura oportunidade quer saber se existe essa condição no mercado.
    /// <c>null</c> em análise antiga, anterior a este campo.
    /// </summary>
    public bool? HasFreeShipping { get; set; }

    /// <summary>Tem ao menos um anúncio novo.</summary>
    public bool? HasNewCondition { get; set; }

    /// <summary>Tem ao menos um anúncio usado.</summary>
    public bool? HasUsedCondition { get; set; }

    public string PriceJson { get; set; } = "{}";

    public string DemandJson { get; set; } = "{}";

    public string? MarginJson { get; set; }

    /// <summary>
    /// O teto de compra do produto. Separado de <see cref="MarginJson"/> de propósito: este
    /// não depende de fornecedor e por isso costuma existir quando a margem ainda não existe.
    /// <c>null</c> sem preço de mercado confiável ou sem a tabela de tarifas da categoria.
    /// </summary>
    public string? CeilingJson { get; set; }

    /// <summary>
    /// Piso do runway em semanas, para o painel filtrar "dá tempo de importar". <c>null</c>
    /// quando a série é curta demais para estimar — que é diferente de zero.
    /// </summary>
    public decimal? RunwayWeeksLower { get; set; }

    /// <summary>Fase da curva: acelerando, linear, desacelerando, caindo ou desconhecida.</summary>
    public string? DemandPhase { get; set; }

    /// <summary>
    /// Algum canal importado ainda dá tempo. <c>null</c> sem estimativa de runway — "não
    /// sabemos" nunca pode virar "não dá" (I6).
    /// </summary>
    public bool? WorthImporting { get; set; }

    public string? RunwayJson { get; set; }

    /// <summary>
    /// Certificação exigida pela categoria e margem em kit. <c>null</c> em análise anterior
    /// a este campo.
    /// </summary>
    public string? RiskJson { get; set; }

    public string? OpportunityJson { get; set; }
}

/// <summary>Fornecedor de um produto. Fonte "manual" (cadastrado por mim) ou de uma API.</summary>
public sealed class Supplier
{
    public long Id { get; set; }

    public required string ProductId { get; set; }

    public required string Name { get; set; }

    /// <summary>Preço unitário em <see cref="Currency"/>.</summary>
    public decimal UnitPrice { get; set; }

    public string Currency { get; set; } = "USD";

    public int MinimumOrder { get; set; } = 1;

    /// <summary>Unidades que pretendo trazer por remessa. Os tributos são por remessa.</summary>
    public int ShipmentQuantity { get; set; } = 1;

    /// <summary>Frete internacional da remessa inteira, em <see cref="Currency"/>.</summary>
    public decimal InternationalFreightTotal { get; set; }

    public string? Contact { get; set; }

    public string? WhatsApp { get; set; }

    public string? Url { get; set; }

    public string Source { get; set; } = SupplierSource.Manual;

    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Quem cadastrou e quem mexeu por último. <c>null</c> em linha anterior ao cadastro de
    /// operadores — "não sabemos quem foi" é diferente de atribuir a alguém.
    /// </summary>
    public long? CreatedByOperatorId { get; set; }

    public long? UpdatedByOperatorId { get; set; }
}

public static class SupplierSource
{
    public const string Manual = "manual";
    public const string AliExpress = "aliexpress";
}

/// <summary>
/// Quem está trabalhando. É identificação por BOA-FÉ, não autenticação: a empresa tem uma
/// conta só, e a pessoa se identifica escolhendo o próprio nome numa lista, sem senha.
/// <para>
/// Serve para a auditoria responder "quem mudou este custo?", não para impedir ninguém de
/// nada. Quem guarda a porta é o <c>AdminOnly</c>, com a chave única da empresa — este
/// cadastro só atribui autoria ao que já passou por lá. Não o use como controle de acesso.
/// </para>
/// </summary>
public sealed class Operator
{
    public long Id { get; set; }

    /// <summary>Nome como se exibe, com acento e maiúscula: "José da Silva".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Nome sem acento e em minúsculo, só para comparar. Índice único é sobre ESTA coluna:
    /// é o que impede "José" e "Jose" de virarem dois operadores e partirem a auditoria em duas.
    /// </summary>
    public required string NameKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Desativado sai da lista de escolha, mas continua explicando o histórico.</summary>
    public bool Active { get; set; } = true;
}

/// <summary>
/// Uma escolha na tela "Estou trabalhando com quem?". Uma linha por vez que alguém se
/// identifica — é o que permite reconstruir quem estava no sistema em cada momento.
/// </summary>
public sealed class WorkSession
{
    public long Id { get; set; }

    public long OperatorId { get; set; }

    public DateTimeOffset StartedAt { get; set; }
}

/// <summary>
/// Credencial OAuth do Mercado Livre. O refresh token é de uso único: cada renovação devolve
/// um novo, que precisa ser gravado antes de qualquer outra chamada, ou o próximo ciclo morre.
/// </summary>
public sealed class OAuthCredential
{
    public int Id { get; set; }

    public required string Provider { get; set; }

    public required string AccessToken { get; set; }

    public required string RefreshToken { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Cotação PTAX de venda, em cache diário.</summary>
public sealed class ExchangeRateQuote
{
    public required string Currency { get; set; }

    /// <summary>Dia da cotação efetivamente usada — pode ser anterior ao pedido (fim de semana).</summary>
    public DateOnly QuoteDate { get; set; }

    /// <summary>Dia para o qual a cotação foi pedida.</summary>
    public DateOnly RequestedDate { get; set; }

    public decimal Sell { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
