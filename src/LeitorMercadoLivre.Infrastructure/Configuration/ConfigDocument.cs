using System.Text.Json.Serialization;

namespace LeitorMercadoLivre.Infrastructure.Configuration;

// Formato dos arquivos JSON da pasta de configuração. Os nomes ficam em português porque
// quem edita — pessoa ou agente — edita o JSON, não o C#. O schema correspondente está em
// config/configuracoes.schema.json, e o editor valida enquanto se digita.

public sealed class ConfigDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("tributos")]
    public List<TaxRuleEntry> TaxRules { get; set; } = [];

    [JsonPropertyName("parametros")]
    public List<ParametersEntry> Parameters { get; set; } = [];
}

/// <summary>Uma versão das alíquotas de um regime de importação, válida a partir de uma data.</summary>
public sealed class TaxRuleEntry
{
    [JsonRequired, JsonPropertyName("regime")]
    public string Regime { get; set; } = "";

    [JsonRequired, JsonPropertyName("vigenteDesde")]
    public DateOnly ValidFrom { get; set; }

    [JsonPropertyName("vigenteAte")]
    public DateOnly? ValidTo { get; set; }

    [JsonRequired, JsonPropertyName("faixasImpostoImportacao")]
    public List<BracketEntry> ImportDutyBrackets { get; set; } = [];

    [JsonPropertyName("ipi")]
    public decimal Ipi { get; set; }

    [JsonPropertyName("pis")]
    public decimal Pis { get; set; }

    [JsonPropertyName("cofins")]
    public decimal Cofins { get; set; }

    [JsonRequired, JsonPropertyName("icms")]
    public decimal Icms { get; set; }

    [JsonPropertyName("estado")]
    public string? State { get; set; }

    /// <summary>Obrigatória: de onde saiu cada número. Sem fonte, a versão é rejeitada.</summary>
    [JsonRequired, JsonPropertyName("fonte")]
    public string Source { get; set; } = "";

    [JsonPropertyName("observacao")]
    public string? Note { get; set; }
}

public sealed class BracketEntry
{
    /// <summary>Teto da faixa em dólares, inclusive. Ausente = sem teto (só na última).</summary>
    [JsonPropertyName("ateUsd")]
    public decimal? UpToForeign { get; set; }

    [JsonRequired, JsonPropertyName("aliquota")]
    public decimal Rate { get; set; }

    [JsonPropertyName("deducaoUsd")]
    public decimal DeductionForeign { get; set; }
}

/// <summary>Uma versão dos parâmetros de negócio, válida a partir de uma data.</summary>
public sealed class ParametersEntry
{
    [JsonRequired, JsonPropertyName("vigenteDesde")]
    public DateOnly ValidFrom { get; set; }

    /// <summary>Nome do regime em "tributos" cujas alíquotas valem para as compras.</summary>
    [JsonRequired, JsonPropertyName("regimeImportacao")]
    public string ImportRegime { get; set; } = "";

    /// <summary>Imposto do regime tributário sobre a venda (Simples etc.). Zero no MEI.</summary>
    [JsonRequired, JsonPropertyName("impostoSobreVenda")]
    public decimal SaleTaxRate { get; set; }

    [JsonRequired, JsonPropertyName("margemAlvo")]
    public decimal TargetMargin { get; set; }

    [JsonRequired, JsonPropertyName("spreadCambial")]
    public decimal ExchangeSpread { get; set; }

    [JsonRequired, JsonPropertyName("tipoAnuncio")]
    public string ListingTypeId { get; set; } = "";

    [JsonRequired, JsonPropertyName("limiarFreteGratis")]
    public decimal FreeShippingThreshold { get; set; }

    [JsonRequired, JsonPropertyName("faixasTarifaFixa")]
    public List<decimal> FeeBandLimits { get; set; } = [];

    [JsonRequired, JsonPropertyName("freteAbsorvidoPorUnidade")]
    public decimal AbsorbedFreightPerUnit { get; set; }

    [JsonRequired, JsonPropertyName("freteNacionalPorRemessa")]
    public decimal DomesticFreightTotal { get; set; }

    [JsonPropertyName("despesasAduaneirasPorRemessa")]
    public decimal CustomsFeesTotal { get; set; }

    [JsonRequired, JsonPropertyName("categorias")]
    public List<string> Categories { get; set; } = [];

    [JsonPropertyName("categoriasExcluidas")]
    public List<string> ExcludedCategories { get; set; } = [];

    [JsonPropertyName("finalistasPorCiclo")]
    public int FinalistsPerCycle { get; set; } = 20;

    /// <summary>
    /// Produtos do ranking cujos anúncios são coletados antes do corte. A série diária de
    /// visitas custa uma chamada por anúncio, então ela só é buscada para os finalistas.
    /// </summary>
    [JsonPropertyName("candidatosPorCiclo")]
    public int CandidatesPerCycle { get; set; } = 40;

    /// <summary>
    /// Anúncios de maior visita por produto que compõem a série diária. Fixo entre ciclos,
    /// para a série de um ciclo ser comparável com a do anterior.
    /// </summary>
    [JsonPropertyName("anunciosPorProdutoNaSerie")]
    public int ListingsPerProductForSeries { get; set; } = 5;

    /// <summary>
    /// Quantos anúncios do produto têm as visitas medidas para escolher os da série. Cada um
    /// custa uma chamada (o /visits/items aceita só um id por vez), então este número é o
    /// orçamento de requisições por finalista.
    /// </summary>
    [JsonPropertyName("anunciosMedidosPorProduto")]
    public int VisitSampleSize { get; set; } = 12;

    /// <summary>Variação semanal abaixo da qual o preço é mostrado como estável (—), em fração.</summary>
    [JsonPropertyName("toleranciaSetaTendencia")]
    public decimal TrendTolerance { get; set; } = 0.01m;

    /// <summary>
    /// Canais de fornecimento e o prazo até a mercadoria chegar. É o eixo do tempo: o usuário
    /// não vende hoje, vende na CHEGADA — e um produto pode saturar nesse meio-tempo.
    /// </summary>
    [JsonPropertyName("canaisDeFornecimento")]
    public List<SupplyChannelEntry> SupplyChannels { get; set; } = [];

    /// <summary>
    /// Dias de venda que precisam sobrar DEPOIS da chegada para valer a pena importar. Chegar
    /// no último dia da janela não é negócio, é estoque parado.
    /// </summary>
    [JsonPropertyName("janelaMinimaVendaDias")]
    public int? MinimumSellingWindowDays { get; set; }

    [JsonPropertyName("runway")]
    public RunwayEntry? Runway { get; set; }

    /// <summary>
    /// Que certificação cada categoria costuma exigir. É INDÍCIO para perguntar ao
    /// despachante, nunca parecer: dentro de "Brinquedos" há item que precisa de selo do
    /// Inmetro e item que não precisa.
    /// </summary>
    /// <summary>
    /// Tamanhos de kit a avaliar. A tarifa fixa do ML é por VENDA, então vender 5 unidades
    /// num anúncio só dilui o que avulso inviabiliza.
    /// </summary>
    [JsonPropertyName("tamanhosDeKit")]
    public List<int> KitSizes { get; set; } = [];

    [JsonPropertyName("certificacoes")]
    public List<CertificationEntry> Certifications { get; set; } = [];

    /// <summary>
    /// Custos por unidade que a margem esquecia: embalagem e reserva de quebra. Em produto
    /// barato a embalagem pesa; em vidro, a quebra.
    /// </summary>
    [JsonPropertyName("custosPorCategoria")]
    public List<CategoryCostEntry> CategoryCosts { get; set; } = [];

    [JsonPropertyName("demanda")]
    public DemandEntry? Demand { get; set; }

    [JsonPropertyName("modeloPreco")]
    public PriceModelEntry? PriceModel { get; set; }

    [JsonPropertyName("oportunidade")]
    public OpportunityEntry? Opportunity { get; set; }

    [JsonPropertyName("observacao")]
    public string? Note { get; set; }
}

/// <summary>Um jeito de a mercadoria chegar, com o prazo que ele leva.</summary>
public sealed class SupplyChannelEntry
{
    [JsonRequired, JsonPropertyName("nome")]
    public string Name { get; set; } = "";

    /// <summary>Prazo típico até a porta, em dias. O portao de importação usa este número.</summary>
    [JsonRequired, JsonPropertyName("prazoDias")]
    public int LeadTimeDays { get; set; }

    /// <summary>Se este canal passa pela tributação de importação. Revenda nacional não passa.</summary>
    [JsonPropertyName("importado")]
    public bool Imported { get; set; } = true;

    [JsonPropertyName("observacao")]
    public string? Note { get; set; }
}

/// <summary>Uma exigência provável de certificação para uma categoria.</summary>
public sealed class CertificationEntry
{
    [JsonRequired, JsonPropertyName("categoria")]
    public string CategoryId { get; set; } = "";

    /// <summary>"Inmetro", "Anvisa" ou "Anatel".</summary>
    [JsonRequired, JsonPropertyName("orgao")]
    public string Body { get; set; } = "";

    [JsonRequired, JsonPropertyName("observacao")]
    public string Note { get; set; } = "";
}

/// <summary>
/// Embalagem e quebra por categoria — dois custos que a margem ignorava.
/// </summary>
public sealed class CategoryCostEntry
{
    [JsonRequired, JsonPropertyName("categoria")]
    public string CategoryId { get; set; } = "";

    /// <summary>Custo de embalar uma unidade, em R$.</summary>
    [JsonPropertyName("embalagemPorUnidade")]
    public decimal PackagingPerUnit { get; set; }

    /// <summary>
    /// Fração das unidades que quebra, extravia ou volta. Vidro quebra mais que plástico,
    /// e isso nunca apareceu em nenhuma conta do sistema.
    /// </summary>
    [JsonPropertyName("reservaQuebra")]
    public decimal BreakageReserve { get; set; }

    [JsonPropertyName("observacao")]
    public string? Note { get; set; }
}

/// <summary>Faixas de runway por fase da curva, em semanas. Calibráveis com o tempo.</summary>
public sealed class RunwayEntry
{
    [JsonPropertyName("diasMinimosDeSerie")]
    public int? MinimumDays { get; set; }

    [JsonPropertyName("acelerandoDe")]
    public decimal? AcceleratingLower { get; set; }

    [JsonPropertyName("acelerandoAte")]
    public decimal? AcceleratingUpper { get; set; }

    [JsonPropertyName("linearDe")]
    public decimal? LinearLower { get; set; }

    [JsonPropertyName("linearAte")]
    public decimal? LinearUpper { get; set; }

    [JsonPropertyName("desacelerandoDe")]
    public decimal? DeceleratingLower { get; set; }

    [JsonPropertyName("desacelerandoAte")]
    public decimal? DeceleratingUpper { get; set; }

    [JsonPropertyName("caindoDe")]
    public decimal? FallingLower { get; set; }

    [JsonPropertyName("caindoAte")]
    public decimal? FallingUpper { get; set; }

    [JsonPropertyName("toleranciaEstavel")]
    public decimal? FlatTolerance { get; set; }
}

public sealed class DemandEntry
{
    [JsonPropertyName("pisoSemanal")]
    public int? LowDemandFloor { get; set; }

    [JsonPropertyName("meiaEscalaVolume")]
    public decimal? VolumeHalfScale { get; set; }

    [JsonPropertyName("meiaEscalaTaxa")]
    public decimal? RateHalfScale { get; set; }

    [JsonPropertyName("janelaPersistenciaSemanas")]
    public int? PersistenceWindowWeeks { get; set; }

    [JsonPropertyName("meiaEscalaPosicao")]
    public decimal? RankingHalfScale { get; set; }

    /// <summary>Visitas mínimas na semana anterior para o percentual de crescimento valer.</summary>
    [JsonPropertyName("baseMinimaParaTaxa")]
    public int? MinBaseVisitsForRate { get; set; }

    /// <summary>Crescimento, em %, a partir do qual o produto conta como "explodindo".</summary>
    [JsonPropertyName("crescimentoExplodindo")]
    public decimal? ExplodingGrowthPercent { get; set; }

    /// <summary>Visitas na semana para demanda nova (sem semana anterior) contar como explodindo.</summary>
    [JsonPropertyName("visitasMinimasDemandaNova")]
    public int? MinVisitsForNewDemand { get; set; }
}

public sealed class PriceModelEntry
{
    [JsonPropertyName("limiteBaldeInteiro")]
    public decimal? WholeBucketLimit { get; set; }

    [JsonPropertyName("degrauBaldeAcima")]
    public decimal? CoarseBucketStep { get; set; }

    [JsonPropertyName("corteExtremos")]
    public decimal? TrimRatio { get; set; }

    [JsonPropertyName("amostraMinimaCorte")]
    public int? MinSellersForTrim { get; set; }

    [JsonPropertyName("forcaMinimaModa")]
    public decimal? MinModeStrength { get; set; }

    [JsonPropertyName("minimoVendedoresModa")]
    public int? MinModalBucketSellers { get; set; }
}

public sealed class OpportunityEntry
{
    [JsonPropertyName("pesoQuedaPreco")]
    public decimal? PriceDropWeight { get; set; }

    [JsonPropertyName("pesoDispersao")]
    public decimal? DispersionWeight { get; set; }

    [JsonPropertyName("pesoQuedaForcaModa")]
    public decimal? ModeStrengthDropWeight { get; set; }

    [JsonPropertyName("limiarDemandaSubindo")]
    public decimal? RisingDemandThreshold { get; set; }

    [JsonPropertyName("limiarConcorrenciaAquecida")]
    public decimal? HotCompetitionThreshold { get; set; }
}
