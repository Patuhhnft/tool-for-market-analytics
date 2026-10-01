import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { emptyFilters } from './exploreFilters'
import {
  filtersFromQuery,
  maxPresets,
  readSavedFilters,
  rememberLastUsed,
  removePreset,
  savePreset,
  storageKey
} from './savedFiltersStorage'

// Guardar filtro é conveniência. O que estes testes protegem é a promessa de que, quando o
// armazenamento falha — bloqueado, cheio ou com lixo dentro —, a tela continua funcionando.

function fakeStorage() {
  const data = new Map<string, string>()
  return {
    getItem: (key: string) => data.get(key) ?? null,
    setItem: (key: string, value: string) => void data.set(key, value),
    removeItem: (key: string) => void data.delete(key),
    clear: () => data.clear(),
    key: (index: number) => [...data.keys()][index] ?? null,
    get length() {
      return data.size
    }
  } as Storage
}

beforeEach(() => {
  vi.stubGlobal('localStorage', fakeStorage())
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('sem nada guardado', () => {
  it('devolve o estado vazio', () => {
    expect(readSavedFilters()).toEqual({ lastUsed: null, presets: [] })
  })
})

describe('presets', () => {
  const filtro = { ...emptyFilters(), categoria: 'MLB1051', concorrencia: ['upto5' as const] }

  it('salva e reaplica o mesmo filtro', () => {
    savePreset('Celulares baratos', filtro)
    const [preset] = readSavedFilters().presets

    expect(preset.name).toBe('Celulares baratos')
    expect(filtersFromQuery(preset.query)).toEqual({ ...filtro, page: 1 })
  })

  it('nome repetido substitui em vez de duplicar', () => {
    savePreset('Mesmo nome', filtro)
    savePreset('mesmo nome', { ...emptyFilters(), categoria: 'MLB1276' })
    const { presets } = readSavedFilters()

    expect(presets).toHaveLength(1)
    expect(filtersFromQuery(presets[0].query).categoria).toBe('MLB1276')
  })

  it('nome em branco não vira preset', () => {
    savePreset('   ', filtro)

    expect(readSavedFilters().presets).toHaveLength(0)
  })

  it('para de crescer no limite', () => {
    for (let i = 0; i < maxPresets + 5; i++) savePreset(`Filtro ${i}`, filtro)

    expect(readSavedFilters().presets).toHaveLength(maxPresets)
  })

  it('apaga pelo id', () => {
    savePreset('Para apagar', filtro)
    const [preset] = readSavedFilters().presets

    expect(removePreset(preset.id).presets).toHaveLength(0)
  })
})

describe('último filtro usado', () => {
  it('é gravado sem a página', () => {
    rememberLastUsed({ ...emptyFilters(), categoria: 'MLB1051', page: 5 })

    expect(readSavedFilters().lastUsed).toBe('categoria=MLB1051')
  })
})

describe('armazenamento hostil', () => {
  it('JSON quebrado não derruba a leitura', () => {
    localStorage.setItem(storageKey, '{isso não é json')

    expect(readSavedFilters()).toEqual({ lastUsed: null, presets: [] })
  })

  it('preset sem os campos certos é descartado', () => {
    localStorage.setItem(
      storageKey,
      JSON.stringify({ lastUsed: 42, presets: [{ nome: 'errado' }, { name: 'certo', query: 'categoria=MLB1051' }] })
    )
    const state = readSavedFilters()

    expect(state.lastUsed).toBeNull()
    expect(state.presets).toHaveLength(1)
    expect(state.presets[0].name).toBe('certo')
  })

  it('leitura bloqueada devolve vazio em vez de lançar', () => {
    vi.stubGlobal('localStorage', {
      getItem: () => {
        throw new Error('SecurityError')
      }
    } as unknown as Storage)

    expect(() => readSavedFilters()).not.toThrow()
    expect(readSavedFilters()).toEqual({ lastUsed: null, presets: [] })
  })

  it('escrita bloqueada ainda devolve o estado da sessão', () => {
    vi.stubGlobal('localStorage', {
      getItem: () => null,
      setItem: () => {
        throw new Error('QuotaExceededError')
      }
    } as unknown as Storage)

    const state = savePreset('Cabe na sessão', emptyFilters())

    expect(state.presets).toHaveLength(1)
  })

  it('query de uma versão antiga abre com o que ainda vale', () => {
    // "concorrencia=enorme" não existe mais; a categoria continua valendo.
    expect(filtersFromQuery('categoria=MLB1051&concorrencia=enorme')).toEqual({
      ...emptyFilters(),
      categoria: 'MLB1051'
    })
  })
})
