import type {
  CompetitionBand,
  ConditionOption,
  ConfidenceOption,
  GrowthOption,
  NewSellerBand,
  SortOption
} from './types'

// O filtro de "Explorar oportunidades". Uma única forma em três lugares: o estado da tela,
// a query string e o que o navegador guarda. Converter entre eles é tudo que este arquivo faz.
//
// A URL é a fonte da verdade: o que está na barra de endereço é o que a tela mostra. Isso é o
// que faz um filtro ser compartilhável, sobreviver ao F5 e responder ao botão "voltar".

export type ExploreFilters = {
  /** Um produto específico, vindo da barra de pesquisa. */
  produto: string | null
  categoria: string | null
  precoMin: number | null
  precoMax: number | null
  oportunidadeMin: number | null
  oportunidadeMax: number | null
  crescimento: GrowthOption | null
  concorrencia: CompetitionBand[]
  novosVendedores: NewSellerBand[]
  condicao: ConditionOption[]
  freteGratis: boolean
  confianca: ConfidenceOption[]
  temFornecedor: boolean | null
  ordenar: SortOption
  page: number
}

export const pageSize = 20

export const defaultSort: SortOption = 'opportunity_desc'

const growthValues: GrowthOption[] = ['10', '50', '100', 'exploding']
const competitionValues: CompetitionBand[] = ['upto5', '6to15', '16to30', '31plus']
const newSellerValues: NewSellerBand[] = ['none', 'upto2', '3plus']
const conditionValues: ConditionOption[] = ['new', 'used']
const confidenceValues: ConfidenceOption[] = ['low', 'medium', 'high']
const sortValues: SortOption[] = ['opportunity_desc', 'growth_desc', 'competition_asc', 'price_asc', 'price_desc']

export function emptyFilters(): ExploreFilters {
  return {
    produto: null,
    categoria: null,
    precoMin: null,
    precoMax: null,
    oportunidadeMin: null,
    oportunidadeMax: null,
    crescimento: null,
    concorrencia: [],
    novosVendedores: [],
    condicao: [],
    freteGratis: false,
    confianca: [],
    temFornecedor: null,
    ordenar: defaultSort,
    page: 1
  }
}

/**
 * Lê o filtro de onde ele estiver — URL ou armazenamento. Valor desconhecido é descartado em
 * silêncio: uma URL editada à mão, um preset velho ou um link de outra versão abrem a tela
 * com o que dá para aproveitar, em vez de quebrarem.
 */
export function parseFilters(params: URLSearchParams): ExploreFilters {
  const filters = emptyFilters()

  const produto = params.get('produto')?.trim().toUpperCase()
  if (produto) filters.produto = produto

  const categoria = params.get('categoria')?.trim().toUpperCase()
  if (categoria) filters.categoria = categoria

  filters.precoMin = positive(params.get('precoMin'))
  filters.precoMax = positive(params.get('precoMax'))
  filters.oportunidadeMin = fraction(params.get('oportunidadeMin'))
  filters.oportunidadeMax = fraction(params.get('oportunidadeMax'))

  filters.crescimento = pick(params.get('crescimento'), growthValues)
  filters.concorrencia = picks(params.getAll('concorrencia'), competitionValues)
  filters.novosVendedores = picks(params.getAll('novosVendedores'), newSellerValues)
  filters.condicao = picks(params.getAll('condicao'), conditionValues)
  filters.confianca = picks(params.getAll('confianca'), confidenceValues)

  filters.freteGratis = params.get('freteGratis') === 'true'
  filters.temFornecedor = bool(params.get('temFornecedor'))
  filters.ordenar = pick(params.get('ordenar'), sortValues) ?? defaultSort

  const page = Number(params.get('page'))
  filters.page = Number.isInteger(page) && page >= 1 ? page : 1

  // Uma faixa invertida não é filtro, é engano de digitação: a tela zera o teto e o usuário
  // vê o campo vazio, em vez de receber um 400 do servidor.
  if (filters.precoMin !== null && filters.precoMax !== null && filters.precoMin > filters.precoMax) filters.precoMax = null
  if (filters.oportunidadeMin !== null && filters.oportunidadeMax !== null && filters.oportunidadeMin > filters.oportunidadeMax) {
    filters.oportunidadeMax = null
  }

  return filters
}

/** Só o que difere do padrão entra na URL — link curto e legível. */
export function toSearchParams(filters: ExploreFilters): URLSearchParams {
  const params = new URLSearchParams()

  if (filters.produto) params.set('produto', filters.produto)
  if (filters.categoria) params.set('categoria', filters.categoria)
  if (filters.precoMin !== null) params.set('precoMin', String(filters.precoMin))
  if (filters.precoMax !== null) params.set('precoMax', String(filters.precoMax))
  if (filters.oportunidadeMin !== null) params.set('oportunidadeMin', String(filters.oportunidadeMin))
  if (filters.oportunidadeMax !== null) params.set('oportunidadeMax', String(filters.oportunidadeMax))
  if (filters.crescimento) params.set('crescimento', filters.crescimento)
  for (const value of filters.concorrencia) params.append('concorrencia', value)
  for (const value of filters.novosVendedores) params.append('novosVendedores', value)
  for (const value of filters.condicao) params.append('condicao', value)
  for (const value of filters.confianca) params.append('confianca', value)
  if (filters.freteGratis) params.set('freteGratis', 'true')
  if (filters.temFornecedor !== null) params.set('temFornecedor', String(filters.temFornecedor))
  if (filters.ordenar !== defaultSort) params.set('ordenar', filters.ordenar)
  if (filters.page > 1) params.set('page', String(filters.page))

  return params
}

/** A query que vai para a API. Igual à da URL, mais o tamanho da página. */
export function toQuery(filters: ExploreFilters): string {
  const params = toSearchParams(filters)
  params.set('page', String(filters.page))
  params.set('pageSize', String(pageSize))
  return params.toString()
}

/**
 * Quantos filtros estão ativos. Ordenação e página não contam: elas sempre têm um valor, e
 * um contador que nunca zera não informa nada.
 */
export function countActive(filters: ExploreFilters): number {
  return (
    (filters.produto ? 1 : 0) +
    (filters.categoria ? 1 : 0) +
    (filters.precoMin !== null || filters.precoMax !== null ? 1 : 0) +
    (filters.oportunidadeMin !== null || filters.oportunidadeMax !== null ? 1 : 0) +
    (filters.crescimento ? 1 : 0) +
    filters.concorrencia.length +
    filters.novosVendedores.length +
    filters.condicao.length +
    filters.confianca.length +
    (filters.freteGratis ? 1 : 0) +
    (filters.temFornecedor !== null ? 1 : 0)
  )
}

/** Duas listas de filtros são a mesma coisa? Usado para saber se um preset está em uso. */
export function sameFilters(left: ExploreFilters, right: ExploreFilters): boolean {
  const strip = (filters: ExploreFilters) => ({ ...filters, page: 1 })
  return toSearchParams(strip(left)).toString() === toSearchParams(strip(right)).toString()
}

/** Liga/desliga um valor de um filtro de múltipla escolha. */
export function toggle<T>(values: T[], value: T): T[] {
  return values.includes(value) ? values.filter((item) => item !== value) : [...values, value]
}

function pick<T extends string>(raw: string | null, allowed: T[]): T | null {
  const value = raw?.trim().toLowerCase()
  return value && (allowed as string[]).includes(value) ? (value as T) : null
}

function picks<T extends string>(raw: string[], allowed: T[]): T[] {
  const result: T[] = []
  for (const item of raw) {
    const value = pick(item, allowed)
    if (value && !result.includes(value)) result.push(value)
  }
  return result
}

function positive(raw: string | null): number | null {
  if (raw === null || raw.trim() === '') return null
  const value = Number(raw)
  return Number.isFinite(value) && value >= 0 ? value : null
}

function fraction(raw: string | null): number | null {
  const value = positive(raw)
  return value !== null && value <= 1 ? value : null
}

function bool(raw: string | null): boolean | null {
  if (raw === 'true') return true
  if (raw === 'false') return false
  return null
}
