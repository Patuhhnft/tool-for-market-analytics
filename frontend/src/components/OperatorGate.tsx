import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../lib/api'
import { getLastOperatorId, type ActiveOperator } from '../lib/operator'
import type { OperatorView } from '../lib/types'
import '../styles/operator.css'

type Props = {
  operator: ActiveOperator | null
  onChosen: (operator: ActiveOperator) => void
  children: React.ReactNode
}

/**
 * "Estou trabalhando com quem?" — a primeira coisa que aparece numa sessão nova.
 *
 * <b>Isto não é uma tela de login.</b> Não há senha, e escolher um nome não dá nem tira
 * permissão de ninguém: a empresa tem uma conta só, e isto serve para a auditoria saber a
 * quem atribuir cada mudança. Quem realmente barra é a chave de administrador.
 *
 * Por isso a tela não "protege" nada — ela só se recusa a deixar trabalhar sem dono, porque
 * o backend rejeita escrita não assinada e o usuário ficaria batendo em erro 400.
 */
export function OperatorGate({ operator, onChosen, children }: Props) {
  if (operator) return <>{children}</>

  return <OperatorPicker onChosen={onChosen} />
}

function OperatorPicker({ onChosen }: { onChosen: (operator: ActiveOperator) => void }) {
  const [operators, setOperators] = useState<OperatorView[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [naming, setNaming] = useState(false)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)

  const lastUsed = getLastOperatorId()

  useEffect(() => {
    let active = true
    api.operators().then(
      (list) => { if (active) setOperators(list) },
      (failure: unknown) => {
        if (!active) return
        setError(failure instanceof ApiError ? failure.message : 'Não foi possível carregar os operadores.')
        setOperators([])
      })
    return () => { active = false }
  }, [])

  /** Registra a sessão de trabalho e entra. */
  const choose = useCallback(async (chosen: OperatorView) => {
    setBusy(true)
    setError(null)
    try {
      await api.startWorkSession(chosen.id)
      onChosen({ id: chosen.id, name: chosen.name })
    } catch (failure) {
      // Sem a sessão registrada, a auditoria perderia o "quem estava lá" — melhor não entrar.
      setError(failure instanceof ApiError ? failure.message : 'Não foi possível registrar quem está trabalhando.')
      setBusy(false)
    }
  }, [onChosen])

  const create = async (event: React.FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      // Nome repetido não cria outro operador: o servidor devolve o que já existe.
      await choose(await api.createOperator(name))
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : 'Não foi possível cadastrar o nome.')
      setBusy(false)
    }
  }

  return (
    <main className="operator-gate">
      <div className="operator-card">
        <p className="eyebrow">Leitor do Mercado Livre</p>
        <h1>Estou trabalhando com quem?</h1>
        <p className="operator-note">
          Sem senha: é só para registrar quem mudou o quê. Você pode trocar a qualquer momento.
        </p>

        {error && <p className="notice notice-alert" role="alert">{error}</p>}

        {operators === null ? (
          <p className="operator-note">Carregando…</p>
        ) : (
          <>
            <div className="operator-list">
              {operators.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className={`operator-button ${item.id === lastUsed ? 'is-last' : ''}`}
                  disabled={busy}
                  onClick={() => void choose(item)}
                >
                  {item.name}
                  {item.id === lastUsed && <small>último a trabalhar</small>}
                </button>
              ))}
            </div>

            {operators.length === 0 && !naming && (
              <p className="operator-note">Ninguém cadastrado ainda. Comece pelo seu nome.</p>
            )}

            {naming ? (
              <form className="operator-new" onSubmit={(event) => void create(event)}>
                <input
                  type="text"
                  value={name}
                  maxLength={60}
                  autoFocus
                  placeholder="Seu nome"
                  aria-label="Seu nome"
                  onChange={(event) => setName(event.target.value)}
                />
                <button type="submit" className="primary-button small" disabled={busy || !name.trim()}>
                  Começar
                </button>
              </form>
            ) : (
              <button type="button" className="operator-other" onClick={() => setNaming(true)}>
                outro nome
              </button>
            )}
          </>
        )}
      </div>
    </main>
  )
}

/** O aviso no topo, com a saída para trocar de pessoa. */
export function OperatorBadge({ operator, onSwitch }: { operator: ActiveOperator; onSwitch: () => void }) {
  return (
    <p className="operator-badge">
      Bem-vindo de volta, <strong>{operator.name}</strong>
      {' · '}
      <button type="button" onClick={onSwitch}>Não é você? Trocar</button>
    </p>
  )
}
