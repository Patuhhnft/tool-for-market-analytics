// Espelho dos contratos da API. Os nomes seguem o JSON que o ASP.NET devolve (camelCase),
// e os enums chegam como texto.

export type Quadrant = 'OpenWindow' | 'Race' | 'Saturating' | 'EndOfCycle'
export type PriceStatus = 'Calculated' | 'InsufficientData' | 'NoProductAnchor'
export type PriceSource = 'Mode' | 'Median'
export type PriceConfidence = 'Low' | 'Medium' | 'High'
export type DemandSource = 'Visits' | 'Ranking' | 'Insufficient'
export type Trend = 'up' | 'down' | 'flat'

export type CycleInfo = {
  id: number
  windowStart: string
  startedAt: string
  finishedAt: string | null
  status: 'running' | 'completed' | 'failed'
  error: string | null
}

/** Por que um anúncio saiu da amostra. O card não mostra, mas a auditoria depende disso (I7). */
export type DiscardedListing = {
  listingId: string
  sellerId: string
  reason: string
}

export type PriceReference = {
  status: PriceStatus
  sampleSize: number
  sellersFound: number
  /** Quantos vendedores da amostra são loja oficial. */
  officialStoreSellers: number
  /** Fração dos ANÚNCIOS que é de loja oficial — a prateleira que o comprador vê. */
  officialStoreShare: number
  officialStorePrice: number | null
  sampleHash: string
  discarded: DiscardedListing[]
  /** "None", "SmallSample", "DispersedMarket" ou combinação separada por vírgula. */
  flags: string
  marketPrice: number | null
  source: PriceSource | null
  mode: number | null
  median: number | null
  modeStrength: number | null
  modalBucketSellers: number
  cleanRangeLow: number | null
  cleanRangeHigh: number | null
  suggestedShowcasePrice: number | null
  dispersionIndex: number | null
  confidence: PriceConfidence | null
  weeklyMarketPriceChange: number | null
  weeklyModeStrengthChange: number | null
}

export type DailyCount = { date: string; visits: number }

export type DemandResult = {
  source: DemandSource
  score: number
  persistence: number
  lastWeekVisits: number | null
  previousWeekVisits: number | null
  absoluteDelta: number | null
  rate: number | null
  acceleration: number | null
  risingWeeks: number
  lowDemand: boolean
  falling: boolean
  currentPosition: number | null
  positionGain: number | null
  series: DailyCount[]
}

/**
 * Como o crescimento deve ser EXIBIDO, já decidido no servidor com os limiares da
 * configuração. Não use `result.rate` para mostrar percentual: aquele campo tem uma
 * proteção contra divisão por zero que devolve número mesmo sem haver razão a calcular.
 */
export type VisitGrowth = {
  /** Crescimento em pontos percentuais, ou null quando não é mensurável. */
  percent: number | null
  /** Semana anterior zerada: não há percentual, há estreia. */
  isNewDemand: boolean
  lastWeekVisits: number | null
  /** Houve semana anterior, mas pequena demais para o percentual significar algo. */
  smallBase: boolean
  /** Primeiro dia com visita, quando a demanda é nova. */
  startedOn: string | null
}

export type DemandPayload = {
  result: DemandResult
  itemsInSeries: string[]
  fallbackReason: string | null
  /** null em análise antiga, anterior a este campo. */
  growth: VisitGrowth | null
}

export type SaleCost = { price: number; commission: number; fixedFee: number; absorbedFreight: number; regimeTax: number; total: number }

export type PricePointMargin = { price: number; saleCost: SaleCost; profit: number; margin: number }

export type MarginResult = {
  unitCost: number
  floor: PricePointMargin
  market: PricePointMargin
  ceiling: PricePointMargin
  worstInRange: PricePointMargin
  roiAtMarket: number
  breakEvenPrice: number | null
  lossZones: { from: number; to: number }[]
  maxUnitCostAtMarket: number
  maxUnitCostInRange: number
  targetMargin: number
  worstBelowTarget: boolean
}

export type CalculationLine = { label: string; value: number; formula: string }

export type LandedCost = {
  regime: string
  customsValueForeign: number
  exceedsRegimeLimit: boolean
  effectiveExchangeRate: number
  unitCost: number
  totalTaxes: number
  shipmentTotal: number
  quantity: number
  memo: CalculationLine[]
}

export type ConfigVersionInfo = {
  parametersFile: string
  parametersValidFrom: string
  regime: string
  regimeValidFrom: string
  regimeFile: string
  regimeSource: string
}

export type MarginPayload = {
  supplierId: number
  supplierName: string
  supplierUnitPrice: number
  currency: string
  shipmentQuantity: number
  exchange: { quoteDate: string; ptax: number; effectiveRate: number } | null
  landedCost: LandedCost | null
  margin: MarginResult | null
  maxSupplierUnitPrice: number | null
  feeCategoryId: string | null
  unavailable: string | null
  config: ConfigVersionInfo
  assessedAt: string
}

/**
 * Quanto vale a pena pagar por uma unidade. Vem separado da margem de propósito: não depende
 * de fornecedor, então costuma existir justamente quando a margem ainda não existe.
 */
export type PurchaseCeiling = {
  /** Teto vendendo no preço de mercado. */
  atMarket: number
  /** Teto que aguenta QUALQUER preço da faixa praticada — o número conservador. */
  inRange: number
  /** Onde a restrição aperta. Nem sempre é o piso: o limiar de frete grátis costuma ser pior. */
  bindingPrice: number
  targetMargin: number
  prices: { floor: number; market: number; ceiling: number }
  /** Falso quando nenhum preço de compra fecha a margem-alvo: as tarifas já comem o alvo. */
  viable: boolean
}

export type CeilingPayload = {
  ceiling: PurchaseCeiling
  feeCategoryId: string | null
  config: ConfigVersionInfo
  assessedAt: string
}

// ------------------------------------------------------------------------------------
// Eixo do tempo: você não vende hoje, vende quando a mercadoria chega.
// ------------------------------------------------------------------------------------

export type DemandPhase = 'Unknown' | 'Accelerating' | 'Linear' | 'Decelerating' | 'Falling'

/** Quanto de janela resta. É FAIXA, nunca ponto — e quem decide compra usa o piso. */
export type Runway = {
  phase: DemandPhase
  lowerWeeks: number | null
  upperWeeks: number | null
  reason: string
}

/** `fits`: true dá tempo, false não dá, null não sabemos. Não sei ≠ não dá. */
export type ChannelVerdict = {
  name: string
  leadTimeDays: number
  imported: boolean
  fits: boolean | null
}

export type RunwayPayload = {
  runway: Runway
  minimumSellingWindowDays: number
  channels: ChannelVerdict[]
  worthImporting: boolean
}

export type CertifyingBody = 'Inmetro' | 'Anvisa' | 'Anatel'

/** Indício de certificação pela categoria — para perguntar ao despachante, não parecer. */
export type CertificationRequirement = {
  categoryId: string
  body: CertifyingBody
  note: string
}

/** Margem vendendo N unidades num anúncio só. */
export type KitOption = {
  units: number
  price: number
  margin: number
  marginPerUnitSale: number
  gain: number
}

export type ProductRiskPayload = {
  certifications: CertificationRequirement[]
  packagingPerUnit: number
  breakageReserve: number
  kits: KitOption[]
}

export type OpportunityResult = {
  index: number
  demandFactor: number
  supplyFactor: number
  pricePressure: number
  persistenceFactor: number
  marginFactor: number | null
  includesMargin: boolean
  quadrant: Quadrant
}

export type Supplier = {
  id: number
  name: string
  unitPrice: number
  currency: string
  minimumOrder: number
  shipmentQuantity: number
  internationalFreightTotal: number
  contact: string | null
  whatsApp: string | null
  whatsAppLink: string | null
  url: string | null
  source: string
  updatedAt: string
}

export type ProductCard = {
  productId: string
  name: string | null
  categoryId: string
  cycleId: number
  calculatedAt: string
  quadrant: Quadrant | null
  opportunityIndex: number | null
  lowDemand: boolean
  explanation: string
  sellers: number
  newSellersThisWeek: number
  price: PriceReference
  priceTrend: Trend | null
  priceUnstable: boolean
  feeCategoryId: string | null
  demand: DemandPayload
  margin: MarginPayload | null
  ceiling: CeilingPayload | null
  runway: RunwayPayload | null
  risks: ProductRiskPayload | null
  opportunity: OpportunityResult | null
  suppliers: Supplier[]
}

export type ProductBoard = { cycle: CycleInfo | null; lastAttempt: CycleInfo | null; products: ProductCard[] }

// ------------------------------------------------------------------------------------
// Explorar oportunidades — área separada do quadro. Os valores dos filtros são os mesmos
// que viajam na URL e ficam salvos no navegador, então são texto fixo, nunca o rótulo.
// ------------------------------------------------------------------------------------

export type GrowthOption = '10' | '50' | '100' | 'exploding'
export type CompetitionBand = 'upto5' | '6to15' | '16to30' | '31plus'
export type NewSellerBand = 'none' | 'upto2' | '3plus'
export type ConditionOption = 'new' | 'used'
export type ConfidenceOption = 'low' | 'medium' | 'high'
export type SortOption = 'opportunity_desc' | 'growth_desc' | 'competition_asc' | 'price_asc' | 'price_desc'

export type CategoryOption = { id: string; label: string; count: number }

export type FilterOption = { value: string; label: string }

export type FilterMeta = {
  categories: CategoryOption[]
  priceMin: number | null
  priceMax: number | null
  totalProducts: number
  growth: FilterOption[]
  competition: FilterOption[]
  newSellers: FilterOption[]
  conditions: FilterOption[]
  confidence: FilterOption[]
  sort: FilterOption[]
}

/** O filtro como o servidor o entendeu. Volta na resposta para a tela confirmar o que aplicou. */
export type AppliedFilter = {
  categoryId: string | null
  priceMin: number | null
  priceMax: number | null
  opportunityMin: number | null
  opportunityMax: number | null
  growth: string | null
  competition: string[]
  newSellers: string[]
  conditions: string[]
  freeShipping: boolean | null
  confidence: string[]
  hasSupplier: boolean | null
  sort: SortOption
  page: number
  pageSize: number
}

export type ExploreResult = {
  items: ProductCard[]
  total: number
  page: number
  pageSize: number
  totalPages: number
  hasNext: boolean
  hasPrevious: boolean
  appliedFilter: AppliedFilter
  lastCycle: CycleInfo | null
}

// ------------------------------------------------------------------------------------
// Barra de pesquisa. Duas fontes com custos muito diferentes: o histórico é SQL nosso e
// responde a cada tecla; o catálogo é o Mercado Livre e custa chamada da conta.
// ------------------------------------------------------------------------------------

/** Um produto que já coletamos. */
export type HistoryHit = {
  productId: string
  name: string | null
  categoryId: string
  categoryName: string | null
  lastSeenAt: string
  analyzedAt: string | null
  marketPrice: number | null
  opportunityIndex: number | null
  analyzed: boolean
}

/** Um produto do catálogo do ML, talvez ainda não analisado por nós. */
export type CatalogCandidate = {
  id: string
  name: string | null
  domainId: string | null
  thumbnail: string | null
  alreadyKnown: boolean
}

/** `unavailable` preenchido = não deu para buscar agora. NÃO é "nada encontrado". */
export type CatalogSearchResult = { items: CatalogCandidate[]; unavailable: string | null }

export type AnalysisQueued = { productId: string; jobId: string; message: string }

/** Um operador cadastrado. `lastSeenAt` é a última vez que ele se identificou. */
export type OperatorView = {
  id: number
  name: string
  createdAt: string
  lastSeenAt: string | null
}

export type SupplierInput = {
  name: string
  unitPrice: number
  currency: string
  minimumOrder: number
  shipmentQuantity: number
  internationalFreightTotal: number
  contact: string | null
  whatsApp: string | null
  url: string | null
}

// ------------------------------------------------------------------------------------
// Configuração — os nomes aqui são os do JSON da pasta config/, em português.
// ------------------------------------------------------------------------------------

export type Bracket = { ateUsd?: number | null; aliquota: number; deducaoUsd?: number }

export type TaxRule = {
  regime: string
  vigenteDesde: string
  vigenteAte?: string | null
  faixasImpostoImportacao: Bracket[]
  ipi?: number
  pis?: number
  cofins?: number
  icms: number
  estado?: string | null
  fonte: string
  observacao?: string | null
}

export type Parameters = {
  vigenteDesde: string
  regimeImportacao: string
  impostoSobreVenda: number
  margemAlvo: number
  spreadCambial: number
  tipoAnuncio: string
  limiarFreteGratis: number
  faixasTarifaFixa: number[]
  freteAbsorvidoPorUnidade: number
  freteNacionalPorRemessa: number
  despesasAduaneirasPorRemessa?: number
  categorias: string[]
  categoriasExcluidas?: string[]
  finalistasPorCiclo?: number
  candidatosPorCiclo?: number
  anunciosPorProdutoNaSerie?: number
  toleranciaSetaTendencia?: number
  observacao?: string | null
  [extra: string]: unknown
}

export type Sourced<T> = { entry: T; file: string }

export type ConfigFileStatus = {
  fileName: string
  accepted: boolean
  errors: string[]
  taxRules: number
  parameters: number
  lastModified: string
}

export type ConfigOverview = {
  folder: string
  adminFile: string
  files: ConfigFileStatus[]
  errors: string[]
  taxRules: Sourced<TaxRule>[]
  parameters: Sourced<Parameters>[]
  effective: { date: string; parameters: Sourced<Parameters>; taxRule: Sourced<TaxRule> } | null
  problems: string[]
}

export type EndpointResult = {
  name: string
  method: string
  path: string
  executed: boolean
  statusCode: number | null
  elapsedMilliseconds: number | null
  accessible: boolean
  detail: string | null
}

export type DiagnosticsReport = { checkedAt: string; tokenConfigured: boolean; endpoints: EndpointResult[] }
