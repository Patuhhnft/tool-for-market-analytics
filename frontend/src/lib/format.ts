// Formatação pt-BR. Arredondar é trabalho de quem exibe (I3): a API manda precisão cheia.

const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const integer = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 })

export function money(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : brl.format(value)
}

/** Preço "de vitrine": R$ 90 quando é inteiro, R$ 89,90 quando tem centavos. */
export function price(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return Number.isInteger(value) ? `R$ ${integer.format(value)}` : brl.format(value)
}

export function foreign(value: number | null | undefined, currency = 'USD'): string {
  if (value === null || value === undefined) return '—'
  return new Intl.NumberFormat('pt-BR', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value)
}

export function count(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : integer.format(value)
}

export function percent(fraction: number | null | undefined, digits = 1): string {
  if (fraction === null || fraction === undefined) return '—'
  return `${(fraction * 100).toLocaleString('pt-BR', { minimumFractionDigits: digits, maximumFractionDigits: digits })}%`
}

export function signedPercent(fraction: number | null | undefined, digits = 0): string {
  if (fraction === null || fraction === undefined) return '—'
  return `${fraction >= 0 ? '+' : '−'}${percent(Math.abs(fraction), digits)}`
}

export function dateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
}

export function day(iso: string): string {
  const [year, month, date] = iso.slice(0, 10).split('-')
  return `${date}/${month}/${year}`
}

export const quadrantLabel: Record<string, string> = {
  OpenWindow: 'Janela aberta',
  Race: 'Corrida',
  Saturating: 'Saturando',
  EndOfCycle: 'Fim de ciclo'
}

export const confidenceLabel: Record<string, string> = { High: 'alta', Medium: 'média', Low: 'baixa' }
