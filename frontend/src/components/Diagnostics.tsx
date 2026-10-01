import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../lib/api'
import type { DiagnosticsReport, EndpointResult } from '../lib/types'

/**
 * Bloqueios que o diagnóstico de 24/09/2026 mediu e que o projeto já contornou. Continuam
 * sendo testados a cada execução — se a ML reabrir um deles, ele volta a aparecer verde —
 * mas não são pintados como falha inesperada.
 */
function knownRestriction(endpoint: EndpointResult): string | null {
  if (!endpoint.executed || endpoint.accessible) return null

  if (endpoint.path.startsWith('/trends')) {
    return 'Restrição conhecida: não existe mais trends público. Substituído pelo ranking de mais vendidos (/highlights).'
  }

  if (endpoint.path.startsWith('/items') && !endpoint.path.includes('/visits')) {
    return 'Restrição conhecida: o detalhe de anúncio de terceiro é bloqueado (sem sold_quantity). A demanda vem das visitas (/visits).'
  }

  return null
}

export function Diagnostics() {
  const [report, setReport] = useState<DiagnosticsReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const run = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setReport(await api.diagnostics())
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : 'Não foi possível consultar a API.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    let active = true
    api.diagnostics().then(
      (data) => { if (active) { setReport(data); setLoading(false) } },
      (failure: unknown) => {
        if (!active) return
        setError(failure instanceof ApiError ? failure.message : 'Não foi possível consultar a API.')
        setLoading(false)
      })
    return () => { active = false }
  }, [])

  const accessible = report?.endpoints.filter((endpoint) => endpoint.accessible).length ?? 0
  const known = report?.endpoints.filter((endpoint) => knownRestriction(endpoint) !== null).length ?? 0
  const unexpected = report?.endpoints.filter((endpoint) => endpoint.executed && !endpoint.accessible && !knownRestriction(endpoint)).length ?? 0

  return (
    <>
      <section className="hero-section">
        <div className="hero-copy">
          <p className="section-kicker">Diagnóstico</p>
          <h1>Leia o mercado antes de entrar nele.</h1>
          <p className="hero-description">
            O que o seu token alcança na API do Mercado Livre, endpoint por endpoint. Bloqueios já contornados pelo projeto
            aparecem como restrição conhecida; só o que for inesperado aparece como falha.
          </p>
          <button className="primary-button" type="button" onClick={() => void run()} disabled={loading}>
            <span aria-hidden="true">↻</span>{loading ? 'Executando…' : 'Executar diagnóstico'}
          </button>
        </div>
        <div className="signal-graphic" aria-hidden="true">
          <span className="signal-ring ring-one" /><span className="signal-ring ring-two" /><span className="signal-ring ring-three" />
          <span className="signal-core" /><span className="signal-label">MLB</span>
        </div>
      </section>

      <section className="summary-grid" aria-label="Resumo do diagnóstico">
        <article className="summary-card summary-card-primary">
          <span className="summary-label">Acessíveis</span>
          <strong>{accessible}<small> / {report?.endpoints.length ?? '—'}</small></strong>
          <span className="summary-note">endpoints respondendo agora</span>
        </article>
        <article className="summary-card">
          <span className="summary-label">Falhas inesperadas</span>
          <strong className={unexpected > 0 ? 'value-alert' : 'value-good'}>{report ? unexpected : '—'}</strong>
          <span className="summary-note">{known} restrições conhecidas, já contornadas</span>
        </article>
        <article className="summary-card">
          <span className="summary-label">Credencial</span>
          <strong className={report?.tokenConfigured ? 'value-good' : 'value-alert'}>{report?.tokenConfigured ? 'Ativa' : 'Ausente'}</strong>
          <span className="summary-note">OAuth DevCenter</span>
        </article>
      </section>

      {error && <div className="error-banner" role="alert"><strong>Diagnóstico indisponível.</strong><span>{error}</span></div>}

      <section className="content-section">
        <div className="section-heading">
          <div><p className="section-kicker">Mapa de acesso</p><h2>Endpoints monitorados</h2></div>
          {report && <time dateTime={report.checkedAt}>Atualizado às {new Date(report.checkedAt).toLocaleTimeString('pt-BR')}</time>}
        </div>
        <div className="endpoint-list">
          {report?.endpoints.map((endpoint) => {
            const restriction = knownRestriction(endpoint)
            const state = endpoint.accessible ? 'accessible' : restriction ? 'known' : endpoint.executed ? 'denied' : 'pending'

            return (
              <article className="endpoint-row" key={`${endpoint.method}-${endpoint.path}`}>
                <div className={`endpoint-icon is-${state}`} aria-hidden="true">
                  {state === 'accessible' ? '✓' : state === 'denied' ? '!' : state === 'known' ? '–' : '·'}
                </div>
                <div className="endpoint-info">
                  <div className="endpoint-title-line">
                    <h3>{endpoint.name}</h3>
                    <span className={`result-badge badge-${state}`}>
                      {state === 'accessible' ? 'Acessível' : state === 'known' ? `Restrição conhecida · HTTP ${endpoint.statusCode}` : state === 'denied' ? `HTTP ${endpoint.statusCode}` : 'Pendente'}
                    </span>
                  </div>
                  <code>{endpoint.method} {endpoint.path}</code>
                  {restriction && <p className="restriction-note">{restriction}</p>}
                  <p>{endpoint.detail || 'Resposta recebida sem detalhes adicionais.'}</p>
                </div>
                {endpoint.elapsedMilliseconds !== null && <span className="latency">{endpoint.elapsedMilliseconds} ms</span>}
              </article>
            )
          })}
          {!report && !error && <div className="empty-state">Carregando leitura dos endpoints…</div>}
        </div>
      </section>
    </>
  )
}
