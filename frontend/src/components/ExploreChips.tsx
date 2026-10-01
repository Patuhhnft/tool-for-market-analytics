import { price } from '../lib/format'
import { emptyFilters, type ExploreFilters } from '../lib/exploreFilters'
import type { FilterMeta, FilterOption } from '../lib/types'

type Props = {
  filters: ExploreFilters
  meta: FilterMeta | null
  onChange: (filters: ExploreFilters) => void
}

type Chip = { key: string; label: string; clear: () => ExploreFilters }

/**
 * Os filtros ativos, um por selo, cada um com o seu X. É o resumo do que está valendo:
 * com a barra lateral fechada no celular, é a única coisa que responde "por que a lista
 * está tão curta?".
 */
export function ExploreChips({ filters, meta, onChange }: Props) {
  const chips = describe(filters, meta)
  if (chips.length === 0) return null

  return (
    <div className="chips" aria-label="Filtros ativos">
      {chips.map((chip) => (
        <button key={chip.key} type="button" className="chip" onClick={() => onChange(chip.clear())}>
          {chip.label}
          <span aria-hidden="true">×</span>
          <span className="sr-only">Remover filtro</span>
        </button>
      ))}
      <button type="button" className="chip chip-clear" onClick={() => onChange(emptyFilters())}>
        Limpar tudo
      </button>
    </div>
  )
}

function describe(filters: ExploreFilters, meta: FilterMeta | null): Chip[] {
  const chips: Chip[] = []

  if (filters.produto) {
    chips.push({
      key: 'produto',
      label: `Produto ${filters.produto}`,
      clear: () => ({ ...filters, produto: null, page: 1 })
    })
  }

  if (filters.categoria) {
    const found = meta?.categories.find((item) => item.id === filters.categoria)
    chips.push({
      key: 'categoria',
      label: found?.label ?? filters.categoria,
      clear: () => ({ ...filters, categoria: null, page: 1 })
    })
  }

  if (filters.precoMin !== null || filters.precoMax !== null) {
    chips.push({
      key: 'preco',
      label: range('Preço', filters.precoMin, filters.precoMax, price),
      clear: () => ({ ...filters, precoMin: null, precoMax: null, page: 1 })
    })
  }

  if (filters.oportunidadeMin !== null || filters.oportunidadeMax !== null) {
    chips.push({
      key: 'oportunidade',
      label: range('Índice', filters.oportunidadeMin, filters.oportunidadeMax, (value) => value.toLocaleString('pt-BR')),
      clear: () => ({ ...filters, oportunidadeMin: null, oportunidadeMax: null, page: 1 })
    })
  }

  if (filters.crescimento) {
    chips.push({
      key: 'crescimento',
      label: label(meta?.growth, filters.crescimento),
      clear: () => ({ ...filters, crescimento: null, page: 1 })
    })
  }

  for (const value of filters.concorrencia) {
    chips.push({
      key: `concorrencia-${value}`,
      label: label(meta?.competition, value),
      clear: () => ({ ...filters, concorrencia: filters.concorrencia.filter((item) => item !== value), page: 1 })
    })
  }

  for (const value of filters.novosVendedores) {
    chips.push({
      key: `novos-${value}`,
      label: label(meta?.newSellers, value),
      clear: () => ({ ...filters, novosVendedores: filters.novosVendedores.filter((item) => item !== value), page: 1 })
    })
  }

  for (const value of filters.condicao) {
    chips.push({
      key: `condicao-${value}`,
      label: label(meta?.conditions, value),
      clear: () => ({ ...filters, condicao: filters.condicao.filter((item) => item !== value), page: 1 })
    })
  }

  for (const value of filters.confianca) {
    chips.push({
      key: `confianca-${value}`,
      label: `Confiança ${label(meta?.confidence, value).toLowerCase()}`,
      clear: () => ({ ...filters, confianca: filters.confianca.filter((item) => item !== value), page: 1 })
    })
  }

  if (filters.freteGratis) {
    chips.push({ key: 'frete', label: 'Tem frete grátis', clear: () => ({ ...filters, freteGratis: false, page: 1 }) })
  }

  if (filters.temFornecedor !== null) {
    chips.push({
      key: 'fornecedor',
      label: filters.temFornecedor ? 'Com fornecedor' : 'Sem fornecedor',
      clear: () => ({ ...filters, temFornecedor: null, page: 1 })
    })
  }

  return chips
}

function label(options: FilterOption[] | undefined, value: string): string {
  return options?.find((option) => option.value === value)?.label ?? value
}

function range(prefix: string, min: number | null, max: number | null, format: (value: number) => string): string {
  if (min !== null && max !== null) return `${prefix} ${format(min)} – ${format(max)}`
  if (min !== null) return `${prefix} a partir de ${format(min)}`
  return `${prefix} até ${format(max!)}`
}
