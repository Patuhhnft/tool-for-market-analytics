import { getActiveOperator } from './operator'
import type {
  AnalysisQueued,
  CatalogSearchResult,
  ConfigOverview,
  DiagnosticsReport,
  ExploreResult,
  FilterMeta,
  HistoryHit,
  Parameters,
  ProductBoard,
  ProductCard,
  OperatorView,
  Supplier,
  SupplierInput,
  TaxRule
} from './types'

// Endereço da API: VITE_API_URL no .env, ou a porta do perfil http do launchSettings.
const baseUrl = (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5078'

const adminKeyStorage = 'leitor.adminKey'

/** Chave do administrador lembrada só nesta aba. Armazenamento pode estar bloqueado: nunca quebra. */
export function getAdminKey(): string {
  try {
    return sessionStorage.getItem(adminKeyStorage) ?? ''
  } catch {
    return ''
  }
}

export function setAdminKey(key: string) {
  try {
    if (key) sessionStorage.setItem(adminKeyStorage, key)
    else sessionStorage.removeItem(adminKeyStorage)
  } catch {
    // Sem armazenamento, a chave vale só até recarregar a página.
  }
}

export class ApiError extends Error {
  readonly status: number
  readonly details: string[]

  constructor(status: number, message: string, details: string[] = []) {
    super(message)
    this.status = status
    this.details = details
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  const key = getAdminKey()
  if (key) headers.set('X-Admin-Key', key)

  // Quem está trabalhando. O backend exige em toda escrita, para nada ser gravado sem dono.
  // Vai também nas leituras — lá é ignorado, e assim não é preciso classificar cada chamada.
  // NÃO é credencial: não prova nada, só atribui autoria (veja lib/operator.ts).
  const operator = getActiveOperator()
  if (operator) headers.set('X-Operador-Id', String(operator.id))

  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, { ...init, headers })
  } catch (failure) {
    // Busca cancelada porque outra mais nova saiu na frente não é falha da API: quem chamou
    // precisa distinguir isso de "a API caiu", ou a tela pisca um erro a cada tecla digitada.
    if (failure instanceof DOMException && failure.name === 'AbortError') throw failure
    throw new ApiError(0, `A API não respondeu em ${baseUrl}. Ela está rodando?`)
  }

  if (response.status === 204) return undefined as T

  const text = await response.text()
  const body = text ? (JSON.parse(text) as unknown) : undefined

  if (!response.ok) {
    throw new ApiError(response.status, describe(response.status, body), details(body))
  }

  return body as T
}

function describe(status: number, body: unknown): string {
  if (body && typeof body === 'object') {
    const problem = body as { detail?: string; title?: string }
    if (problem.detail) return problem.detail
    if (problem.title) return problem.title
  }

  if (status === 422) return 'A configuração enviada foi recusada pela validação.'
  return `A API respondeu HTTP ${status}.`
}

function details(body: unknown): string[] {
  if (!body || typeof body !== 'object') return []
  const shaped = body as { errors?: string[] | Record<string, string[]> }
  if (Array.isArray(shaped.errors)) return shaped.errors
  if (shaped.errors) return Object.values(shaped.errors).flat()
  return []
}

export const api = {
  board: () => request<ProductBoard>('/api/products'),
  explore: (query: string, signal?: AbortSignal) =>
    request<ExploreResult>(`/api/oportunidades/explorar${query ? `?${query}` : ''}`, { signal }),
  filterMeta: (signal?: AbortSignal) => request<FilterMeta>('/api/oportunidades/explorar/filtros-meta', { signal }),
  recalculate: (productId: string) => request<ProductCard>(`/api/products/${encodeURIComponent(productId)}/recalculate`, { method: 'POST' }),
  addSupplier: (productId: string, input: SupplierInput) =>
    request<Supplier>(`/api/products/${encodeURIComponent(productId)}/suppliers`, { method: 'POST', body: JSON.stringify(input) }),
  removeSupplier: (supplierId: number) => request<void>(`/api/suppliers/${supplierId}`, { method: 'DELETE' }),
  runCycle: () => request<{ jobId: string; message: string }>('/api/cycles/run', { method: 'POST' }),
  config: () => request<ConfigOverview>('/api/admin/config'),
  addTaxRule: (rule: TaxRule) => request<ConfigOverview>('/api/admin/config/tax-rules', { method: 'POST', body: JSON.stringify(rule) }),
  addParameters: (parameters: Parameters) =>
    request<ConfigOverview>('/api/admin/config/parameters', { method: 'POST', body: JSON.stringify(parameters) }),
  uploadConfig: (nome: string, conteudo: string) =>
    request<ConfigOverview>('/api/admin/config/files', { method: 'POST', body: JSON.stringify({ nome, conteudo }) }),
  fileUrl: (name: string) => `${baseUrl}/api/admin/config/files/${encodeURIComponent(name)}`,
  diagnostics: () => request<DiagnosticsReport>('/api/diagnostics/mercadolivre'),

  // --- Barra de pesquisa.
  // O histórico não toca no Mercado Livre: pode rodar a cada tecla.
  searchHistory: (q: string, signal?: AbortSignal) =>
    request<HistoryHit[]>(`/api/busca/historico?q=${encodeURIComponent(q)}`, { signal }),
  // O catálogo CUSTA chamada da conta: só por ação explícita, nunca pela digitação.
  searchCatalog: (q: string, signal?: AbortSignal) =>
    request<CatalogSearchResult>(`/api/busca/catalogo?q=${encodeURIComponent(q)}`, { signal }),
  analyzeProduct: (productId: string) =>
    request<AnalysisQueued>(`/api/busca/analisar/${encodeURIComponent(productId)}`, { method: 'POST' }),

  // --- Operadores. Estes três rodam ANTES de haver operador escolhido, então não dependem
  // do cabeçalho que os demais mandam.
  operators: () => request<OperatorView[]>('/api/operadores'),
  createOperator: (nome: string) =>
    request<OperatorView>('/api/operadores', { method: 'POST', body: JSON.stringify({ nome }) }),
  startWorkSession: (operatorId: number) =>
    request<void>(`/api/operadores/${operatorId}/sessoes`, { method: 'POST' })
}
