import { useEffect, useState } from 'react'
import { api, ApiError } from '../lib/api'
import { price as priceLabel } from '../lib/format'
import type { CatalogCandidate, HistoryHit } from '../lib/types'
import '../styles/search.css'

type Props = {
  /** Chamado quando o usuário escolhe um produto já conhecido: a tela filtra por ele. */
  onPick: (productId: string) => void
}

/** Espera a digitação parar. Só vale para o histórico — o catálogo nunca é automático. */
const debounceMs = 300

/** Um link do Mercado Livre colado, ou um ID de anúncio. */
const listingPaste = /(?:mercadolivre\.com\.br|MLB-?\d{6,})/i

/**
 * A barra de pesquisa da área "Explorar".
 *
 * Duas fontes, com custos muito diferentes — e a tela deixa isso explícito:
 * - **Histórico**: SQL sobre o que já coletamos. Zero chamada, roda a cada tecla.
 * - **Catálogo**: o Mercado Livre. Custa chamada da conta, então é um BOTÃO, nunca automático.
 *
 * Colar link ou ID de anúncio não funciona e não há contorno — medido em 26/09/2026,
 * `/items/{id}` dá 403 e `/items/bulk` devolve 403 dentro de um envelope 200. Em vez de
 * devolver "nada encontrado", que seria mentira, a barra reconhece o gesto e explica.
 */
export function SearchBar({ onPick }: Props) {
  const [term, setTerm] = useState('')
  const [history, setHistory] = useState<HistoryHit[]>([])
  const [catalog, setCatalog] = useState<CatalogCandidate[] | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [searching, setSearching] = useState(false)
  const [queued, setQueued] = useState<string | null>(null)

  const pastedListing = listingPaste.test(term)

  const clean = term.trim()
  const longEnough = clean.length >= 2

  // Derivado, não guardado: enquanto o termo é curto demais, a lista simplesmente não
  // aparece — sem precisar de um efeito que limpa estado e dispara outro render.
  const visibleHistory = longEnough ? history : []

  // Histórico a cada tecla: não custa chamada ao Mercado Livre, só uma consulta nossa.
  useEffect(() => {
    if (!longEnough) return

    const controller = new AbortController()
    const timer = setTimeout(() => {
      api.searchHistory(clean, controller.signal).then(setHistory, (failure: unknown) => {
        if (failure instanceof DOMException && failure.name === 'AbortError') return
        setHistory([])
      })
    }, debounceMs)

    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [clean, longEnough])

  const searchCatalog = async () => {
    setSearching(true)
    setMessage(null)
    try {
      const result = await api.searchCatalog(clean)
      setCatalog(result.items)
      // "Indisponível" não é "nada encontrado" — a tela precisa dizer qual dos dois é.
      if (result.unavailable) setMessage(result.unavailable)
      else if (result.items.length === 0) setMessage('Nenhum produto ativo com esse nome no catálogo.')
    } catch (failure) {
      setMessage(failure instanceof ApiError ? failure.message : 'Não foi possível buscar no catálogo.')
    } finally {
      setSearching(false)
    }
  }

  const analyze = async (candidate: CatalogCandidate) => {
    setMessage(null)
    try {
      await api.analyzeProduct(candidate.id)
      setQueued(candidate.id)
    } catch (failure) {
      setMessage(failure instanceof ApiError ? failure.message : 'Não foi possível enfileirar a análise.')
    }
  }

  /** Trocar a busca invalida os candidatos do catálogo: eram resposta de outra pergunta. */
  const changeTerm = (value: string) => {
    setTerm(value)
    setCatalog(null)
    setMessage(null)
  }

  const clear = () => {
    setTerm('')
    setCatalog(null)
    setMessage(null)
    setQueued(null)
  }

  return (
    <div className="searchbar">
      <div className="searchbar-input">
        <input
          type="search"
          value={term}
          placeholder="Nome do produto, ou MLB do catálogo"
          aria-label="Procurar produto"
          onChange={(event) => changeTerm(event.target.value)}
        />
        {term && <button type="button" className="searchbar-clear" aria-label="Limpar busca" onClick={clear}>×</button>}
      </div>

      {pastedListing && (
        <p className="notice notice-warn">
          Link e código de <strong>anúncio</strong> não abrem: o Mercado Livre não libera anúncio de
          terceiro para esta conta. Cole o <strong>título</strong> do produto — a busca acha o item
          de catálogo e lista todos os concorrentes.
        </p>
      )}

      {queued && (
        <p className="notice notice-info">
          Análise de <strong>{queued}</strong> enfileirada. O Worker precisa estar rodando; o card
          aparece na lista em instantes.
        </p>
      )}

      {message && <p className="notice notice-alert" role="alert">{message}</p>}

      {visibleHistory.length > 0 && (
        <section className="searchbar-block">
          <h4>Já coletados</h4>
          <ul className="search-results">
            {visibleHistory.map((hit) => (
              <li key={hit.productId}>
                <button type="button" onClick={() => onPick(hit.productId)}>
                  <span className="search-name">{hit.name ?? hit.productId}</span>
                  <span className="search-meta">
                    {hit.categoryName ?? hit.categoryId}
                    {hit.marketPrice !== null && <> · {priceLabel(hit.marketPrice)}</>}
                    {hit.opportunityIndex !== null && <> · índice {hit.opportunityIndex.toLocaleString('pt-BR', { maximumFractionDigits: 2 })}</>}
                    {!hit.analyzed && <> · ainda sem análise</>}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      {longEnough && (
        <div className="searchbar-catalog">
          <button type="button" className="ghost-button" disabled={searching} onClick={() => void searchCatalog()}>
            {searching ? 'Procurando…' : 'Procurar no Mercado Livre'}
          </button>
          {/* O custo é da conta do ML, e quem paga tem direito de saber antes de clicar. */}
          <small>Custa uma chamada da conta. Analisar um produto novo custa ~23.</small>
        </div>
      )}

      {catalog && catalog.length > 0 && (
        <section className="searchbar-block">
          <h4>No catálogo do Mercado Livre</h4>
          <ul className="search-results">
            {catalog.map((candidate) => (
              <li key={candidate.id}>
                <div className="search-candidate">
                  <span className="search-name">{candidate.name ?? candidate.id}</span>
                  <span className="search-meta">{candidate.id}{candidate.alreadyKnown && <> · já coletado</>}</span>
                </div>
                {candidate.alreadyKnown ? (
                  <button type="button" className="ghost-button" onClick={() => onPick(candidate.id)}>Ver</button>
                ) : (
                  <button type="button" className="primary-button small" onClick={() => void analyze(candidate)}>Analisar</button>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
