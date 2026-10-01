import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../lib/api'
import { dateTime } from '../lib/format'
import type { ProductBoard } from '../lib/types'
import { ProductCardView } from './ProductCard'
import '../styles/board.css'

export function Board() {
  const [board, setBoard] = useState<ProductBoard | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [queued, setQueued] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setBoard(await api.board())
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : 'Não foi possível carregar os produtos.')
    } finally {
      setLoading(false)
    }
  }, [])

  // Carga inicial: estado só muda quando a resposta chega, e resposta atrasada de uma tela
  // já desmontada é descartada.
  useEffect(() => {
    let active = true
    api.board().then(
      (data) => { if (active) { setBoard(data); setLoading(false) } },
      (failure: unknown) => {
        if (!active) return
        setError(failure instanceof ApiError ? failure.message : 'Não foi possível carregar os produtos.')
        setLoading(false)
      })
    return () => { active = false }
  }, [])

  const runNow = async () => {
    try {
      const result = await api.runCycle()
      setQueued(result.message)
    } catch (failure) {
      setQueued(failure instanceof ApiError ? failure.message : 'Não foi possível enfileirar a coleta.')
    }
  }

  const failed = board?.lastAttempt?.status === 'failed' ? board.lastAttempt : null
  const low = board?.products.filter((card) => card.lowDemand) ?? []
  const main = board?.products.filter((card) => !card.lowDemand) ?? []

  return (
    <section className="board">
      <div className="board-bar">
        <div>
          <p className="section-kicker">Oportunidades</p>
          <h2>Produtos do último ciclo</h2>
          <p className="board-meta">
            {board?.cycle
              ? `Coleta concluída em ${dateTime(board.cycle.finishedAt)} · ${board.products.length} produtos`
              : loading ? 'Carregando…' : 'Nenhum ciclo concluído ainda'}
          </p>
        </div>
        <div className="board-actions">
          <button type="button" className="ghost-button" onClick={() => void load()} disabled={loading}>Atualizar</button>
          <button type="button" className="primary-button small" onClick={() => void runNow()}>Coletar agora</button>
        </div>
      </div>

      {queued && <p className="notice notice-info">{queued}</p>}
      {error && <p className="notice notice-alert" role="alert">{error}</p>}
      {failed && (
        <p className="notice notice-alert" role="alert">
          A última coleta, iniciada em {dateTime(failed.startedAt)}, falhou: {failed.error}
        </p>
      )}

      {board && board.products.length === 0 && !error && (
        <div className="empty-board">
          <h3>Ainda não há produtos analisados</h3>
          <p>Os cards aparecem depois do primeiro ciclo de coleta. Para ele acontecer:</p>
          <ol>
            <li>O Postgres precisa estar de pé: <code>docker compose up -d</code></li>
            <li>O Worker precisa estar rodando com a credencial do Mercado Livre no ambiente (<code>MELI_REFRESH_TOKEN</code>, <code>MELI_CLIENT_ID</code>, <code>MELI_CLIENT_SECRET</code>).</li>
            <li>O primeiro ciclo roda sozinho quando o Worker sobe; depois, a cada 6 horas.</li>
          </ol>
        </div>
      )}

      <div className="card-grid">
        {main.map((card) => <ProductCardView key={card.productId} card={card} onChanged={load} />)}
      </div>

      {low.length > 0 && (
        <>
          <div className="low-demand-divider">
            <h3>Demanda abaixo do piso</h3>
            <p>Crescimento em percentual sobre base pequena não é mercado (I11): estes vêm depois, independentemente do índice.</p>
          </div>
          <div className="card-grid">
            {low.map((card) => <ProductCardView key={card.productId} card={card} onChanged={load} />)}
          </div>
        </>
      )}
    </section>
  )
}
