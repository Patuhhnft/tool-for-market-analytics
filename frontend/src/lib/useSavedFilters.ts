import { useCallback, useState } from 'react'
import type { ExploreFilters } from './exploreFilters'
import {
  filtersFromQuery,
  readSavedFilters,
  rememberLastUsed,
  removePreset,
  savePreset,
  type SavedPreset
} from './savedFiltersStorage'

/**
 * Os filtros salvos, como estado de tela. O armazenamento é lido uma vez na montagem; depois
 * cada operação devolve o estado novo já gravado, então a lista na tela e o que está no
 * navegador nunca divergem.
 */
export function useSavedFilters() {
  const [state, setState] = useState(readSavedFilters)

  const save = useCallback((name: string, filters: ExploreFilters) => {
    setState(savePreset(name, filters))
  }, [])

  const remove = useCallback((id: string) => {
    setState(removePreset(id))
  }, [])

  /**
   * Grava o último filtro sem mexer no estado da tela: isso acontece a cada mudança de
   * filtro, e um re-render por tecla digitada seria desperdício puro.
   */
  const remember = useCallback((filters: ExploreFilters) => {
    rememberLastUsed(filters)
  }, [])

  return {
    presets: state.presets as SavedPreset[],
    lastUsed: state.lastUsed,
    lastUsedFilters: state.lastUsed ? filtersFromQuery(state.lastUsed) : null,
    save,
    remove,
    remember
  }
}
