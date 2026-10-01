import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../lib/api'
import { count, dateTime } from '../lib/format'
import {
  countActive,
  emptyFilters,
  parseFilters,
  toQuery,
  toSearchParams,
  type ExploreFilters
} from '../lib/exploreFilters'
import { filtersFromQuery } from '../lib/savedFiltersStorage'
import { useSavedFilters } from '../lib/useSavedFilters'
import type { ExploreResult, FilterMeta, SortOption } from '../lib/types'
import { ExploreChips } from './ExploreChips'
import { ExploreSidebar } from './ExploreSidebar'
import { ProductCardView } from './ProductCard'
import { SearchBar } from './SearchBar'
import '../styles/explore.css'

/** Tempo de silêncio antes de buscar. Curto o bastante para não parecer travado. */
const debounceMs = 350

export function Explore() {
  const [searchParams, setSearchParams] = useSearchParams()
  const [meta, setMeta] = useState<FilterMeta | null>(null)
  const [result, setResult] = useState<ExploreResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [drawerOpen, setDrawerOpen] = useState(false)

  // Desestruturado de propósito: o hook devolve um objeto novo a cada render, e é a função
  // estável — não o objeto — que pode entrar nas dependências de um efeito.
  const { presets, lastUsed, save, remove, remember } = useSavedFilters()

  // A URL é a fonte da verdade. O estado da tela é derivado dela, nunca o contrário: por isso
  // recarregar, compartilhar o link e o botão "voltar" fazem a coisa certa sem código extra.
  const filters = useMemo(() => parseFilters(searchParams), [searchParams])
  const query = useMemo(() => toQuery(filters), [filters])
  const active = countActive(filters)

  const apply = useCallback(
    (next: ExploreFilters, replace = false) => {
      setSearchParams(toSearchParams(next), { replace })
    },
    [setSearchParams]
  )

  // Na primeira abertura sem nenhum filtro na URL, volta o último usado. Chegar por um link
  // compartilhado não dispara isso: quem mandou o link escolheu os filtros.
  const restored = useRef(false)
  useEffect(() => {
    if (restored.current) return
    restored.current = true
    if (searchParams.toString() !== '' || !lastUsed) return
    apply(filtersFromQuery(lastUsed), true)
  }, [apply, lastUsed, searchParams])

  useEffect(() => {
    const controller = new AbortController()
    api.filterMeta(controller.signal).then(setMeta, () => {
      // Sem os metadados a tela ainda busca; só fica sem as listas de opções.
    })
    return () => controller.abort()
  }, [])

  const load = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true)
      return api.explore(query, signal).then(
        (data) => {
          setResult(data)
          setError(null)
          setLoading(false)
        },
        (failure: unknown) => {
          // Cancelada porque outra busca saiu na frente: quem chegou depois manda na tela.
          if (failure instanceof DOMException && failure.name === 'AbortError') return
          setError(failure instanceof ApiError ? failure.message : 'Não foi possível buscar os produtos.')
          setLoading(false)
        }
      )
    },
    [query]
  )

  // Espera a digitação parar e cancela a busca anterior. Sem o cancelamento, uma resposta
  // lenta de um filtro velho poderia chegar depois e sobrescrever a lista certa.
  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(() => void load(controller.signal), debounceMs)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [load])

  useEffect(() => {
    remember(filters)
  }, [filters, remember])

  const reload = useCallback(async () => { await load() }, [load])

  const goToPage = (page: number) => {
    apply({ ...filters, page })
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  return (
    <section className="explore">
      <div className="board-bar">
        <div>
          <p className="section-kicker">Explorar oportunidades</p>
          <h2>Busca por filtros</h2>
          <p className="board-meta">
            {result
              ? `${count(result.total)} ${result.total === 1 ? 'produto encontrado' : 'produtos encontrados'}${
                  result.lastCycle ? ` · última coleta em ${dateTime(result.lastCycle.finishedAt)}` : ''
                }`
              : loading ? 'Buscando…' : '—'}
          </p>
        </div>
        <div className="board-actions">
          <button
            type="button"
            className="ghost-button drawer-toggle"
            aria-expanded={drawerOpen}
            onClick={() => setDrawerOpen((open) => !open)}
          >
            Filtros{active > 0 ? ` (${active})` : ''}
          </button>
          <label className="field sort-field">
            <span className="sr-only">Ordenar por</span>
            <select
              value={filters.ordenar}
              onChange={(event) => apply({ ...filters, ordenar: event.target.value as SortOption, page: 1 })}
            >
              {(meta?.sort ?? []).map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </select>
          </label>
        </div>
      </div>

      {/* A barra vem antes dos filtros: procurar um produto específico e filtrar o catálogo
          são perguntas diferentes, e a primeira é mais direta. Escolher um resultado
          simplesmente filtra a lista por ele — o card é o mesmo de sempre. */}
      <SearchBar onPick={(productId) => apply({ ...emptyFilters(), produto: productId })} />

      <ExploreChips filters={filters} meta={meta} onChange={(next) => apply(next)} />

      {error && <p className="notice notice-alert" role="alert">{error}</p>}

      <div className="explore-layout">
        <aside className={`explore-sidebar ${drawerOpen ? 'is-open' : ''}`} aria-label="Filtros">
          <ExploreSidebar
            filters={filters}
            meta={meta}
            presets={presets}
            onChange={(next) => apply(next)}
            onType={(next) => apply(next, true)}
            onSavePreset={(name) => save(name, filters)}
            onRemovePreset={remove}
          />
        </aside>

        <div className="explore-results" aria-busy={loading}>
          {result && result.items.length === 0 && !loading && (
            <div className="empty-board">
              <h3>Nenhum produto com esses filtros</h3>
              <p>
                {active > 0
                  ? 'Os filtros combinam entre si: cada um a mais só diminui a lista. Tire o mais restritivo e veja o que volta.'
                  : 'Ainda não há produtos analisados. Os cards aparecem depois do primeiro ciclo de coleta.'}
              </p>
              {active > 0 && (
                <button type="button" className="primary-button small" onClick={() => apply(emptyFilters())}>
                  Limpar os filtros
                </button>
              )}
            </div>
          )}

          <div className={`card-grid ${loading ? 'is-stale' : ''}`}>
            {result?.items.map((card) => (
              <ProductCardView key={card.productId} card={card} onChanged={reload} />
            ))}
          </div>

          {result && result.totalPages > 1 && (
            <nav className="pager" aria-label="Páginas">
              <button
                type="button"
                className="ghost-button"
                disabled={!result.hasPrevious}
                onClick={() => goToPage(result.page - 1)}
              >
                Anterior
              </button>
              <span>Página {result.page} de {result.totalPages}</span>
              <button
                type="button"
                className="ghost-button"
                disabled={!result.hasNext}
                onClick={() => goToPage(result.page + 1)}
              >
                Próxima
              </button>
            </nav>
          )}
        </div>
      </div>
    </section>
  )
}
