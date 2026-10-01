// Quem está trabalhando, guardado no navegador.
//
// ISTO NÃO É AUTENTICAÇÃO. A empresa tem uma conta só; a pessoa se identifica escolhendo o
// próprio nome, sem senha. Serve para a auditoria responder "quem mudou isto?", nunca para
// decidir quem pode o quê — quem guarda a porta é a chave de administrador.
//
// Dois armazenamentos, de propósito:
//   - o operador ATIVO vai em sessionStorage, então fechar o navegador exige escolher de novo
//     (numa máquina compartilhada, ninguém herda o nome de quem usou antes);
//   - o ÚLTIMO usado vai em localStorage, só para a tela destacar o botão certo. É uma
//     conveniência de tela, não identidade: destacar não é entrar.

const activeKey = 'leitor.operador'
const lastKey = 'leitor.operador.ultimo'

export type ActiveOperator = { id: number; name: string }

export function getActiveOperator(): ActiveOperator | null {
  try {
    const raw = sessionStorage.getItem(activeKey)
    if (!raw) return null

    const parsed = JSON.parse(raw) as Partial<ActiveOperator>
    // Veio de fora: só entra com a forma conferida.
    if (typeof parsed?.id !== 'number' || typeof parsed?.name !== 'string') return null

    return { id: parsed.id, name: parsed.name }
  } catch {
    return null
  }
}

export function setActiveOperator(operator: ActiveOperator): void {
  try {
    sessionStorage.setItem(activeKey, JSON.stringify(operator))
  } catch {
    // Sem armazenamento, o nome vale só até recarregar a página.
  }

  try {
    localStorage.setItem(lastKey, String(operator.id))
  } catch {
    // Sem isto, só não vem ninguém destacado na próxima abertura.
  }
}

/** Troca de operador: esquece o ativo, mas lembra quem era para destacar na lista. */
export function clearActiveOperator(): void {
  try {
    sessionStorage.removeItem(activeKey)
  } catch {
    // Nada a fazer: a tela volta para a seleção de qualquer forma.
  }
}

export function getLastOperatorId(): number | null {
  try {
    const raw = localStorage.getItem(lastKey)
    if (!raw) return null

    const id = Number(raw)
    return Number.isInteger(id) ? id : null
  } catch {
    return null
  }
}
