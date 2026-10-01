import { parseFilters, toSearchParams, type ExploreFilters } from './exploreFilters'

// Filtros salvos no navegador. Tudo aqui é conveniência: se o armazenamento estiver
// bloqueado, cheio ou com lixo dentro, a tela funciona igual — só não lembra nada.
//
// O que se guarda é a query string, não um objeto: ela já é o formato que a URL e a API
// entendem, e ao ler tudo passa de novo pelo parseFilters, que descarta o que não conhece.
// Um preset salvo numa versão antiga abre com o que ainda fizer sentido.

export const storageKey = 'explorarOportunidades.filters.v1'

/** Vinte presets. É muito mais do que alguém usa, e evita que um bug encha o armazenamento. */
export const maxPresets = 20

export type SavedPreset = {
  id: string
  name: string
  /** Query string do filtro, no mesmo formato da URL. */
  query: string
  savedAt: string
}

export type SavedFiltersState = {
  /** Último filtro usado, para a tela reabrir onde parou. */
  lastUsed: string | null
  presets: SavedPreset[]
}

const empty: SavedFiltersState = { lastUsed: null, presets: [] }

export function readSavedFilters(): SavedFiltersState {
  let raw: string | null
  try {
    raw = localStorage.getItem(storageKey)
  } catch {
    return empty
  }

  if (!raw) return empty

  try {
    return validate(JSON.parse(raw))
  } catch {
    // JSON quebrado: começa do zero em vez de derrubar a tela.
    return empty
  }
}

export function writeSavedFilters(state: SavedFiltersState): SavedFiltersState {
  const trimmed: SavedFiltersState = { lastUsed: state.lastUsed, presets: state.presets.slice(0, maxPresets) }
  try {
    localStorage.setItem(storageKey, JSON.stringify(trimmed))
  } catch {
    // Cota estourada ou armazenamento bloqueado: o estado vale para esta sessão.
  }
  return trimmed
}

export function rememberLastUsed(filters: ExploreFilters): SavedFiltersState {
  const current = readSavedFilters()
  return writeSavedFilters({ ...current, lastUsed: toSearchParams({ ...filters, page: 1 }).toString() })
}

/** Salva com nome. Nome repetido substitui o anterior — dois "Eletrônicos" confundem mais que ajudam. */
export function savePreset(name: string, filters: ExploreFilters): SavedFiltersState {
  const current = readSavedFilters()
  const clean = name.trim().slice(0, 60)
  if (!clean) return current

  const preset: SavedPreset = {
    id: newId(),
    name: clean,
    query: toSearchParams({ ...filters, page: 1 }).toString(),
    savedAt: new Date().toISOString()
  }

  const kept = current.presets.filter((item) => item.name.toLowerCase() !== clean.toLowerCase())
  return writeSavedFilters({ ...current, presets: [preset, ...kept].slice(0, maxPresets) })
}

export function removePreset(id: string): SavedFiltersState {
  const current = readSavedFilters()
  return writeSavedFilters({ ...current, presets: current.presets.filter((item) => item.id !== id) })
}

/** Transforma a query guardada em filtro. O parse já rejeita o que não reconhece. */
export function filtersFromQuery(query: string): ExploreFilters {
  return parseFilters(new URLSearchParams(query))
}

/** O que veio do armazenamento é dado de fora: nada entra sem ter a forma conferida. */
function validate(parsed: unknown): SavedFiltersState {
  if (!parsed || typeof parsed !== 'object') return empty

  const shaped = parsed as Partial<SavedFiltersState>
  const presets: SavedPreset[] = []

  if (Array.isArray(shaped.presets)) {
    for (const item of shaped.presets) {
      if (!item || typeof item !== 'object') continue
      const preset = item as Partial<SavedPreset>
      if (typeof preset.name !== 'string' || typeof preset.query !== 'string') continue
      if (!preset.name.trim()) continue

      presets.push({
        id: typeof preset.id === 'string' && preset.id ? preset.id : newId(),
        name: preset.name.slice(0, 60),
        query: preset.query,
        savedAt: typeof preset.savedAt === 'string' ? preset.savedAt : new Date().toISOString()
      })

      if (presets.length === maxPresets) break
    }
  }

  return { lastUsed: typeof shaped.lastUsed === 'string' ? shaped.lastUsed : null, presets }
}

function newId(): string {
  try {
    return crypto.randomUUID()
  } catch {
    return `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
  }
}
