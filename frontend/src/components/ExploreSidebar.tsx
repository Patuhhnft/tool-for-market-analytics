import { useEffect, useState } from 'react'
import { emptyFilters, sameFilters, toggle, type ExploreFilters } from '../lib/exploreFilters'
import { filtersFromQuery, type SavedPreset } from '../lib/savedFiltersStorage'
import { count } from '../lib/format'
import type {
  CompetitionBand,
  ConditionOption,
  ConfidenceOption,
  FilterMeta,
  GrowthOption,
  NewSellerBand
} from '../lib/types'

type Props = {
  filters: ExploreFilters
  meta: FilterMeta | null
  presets: SavedPreset[]
  /** Muda um filtro discreto: entra no histórico, então "voltar" desfaz. */
  onChange: (filters: ExploreFilters) => void
  /** Muda um campo que se digita: substitui a entrada do histórico a cada tecla. */
  onType: (filters: ExploreFilters) => void
  onSavePreset: (name: string) => void
  onRemovePreset: (id: string) => void
}

export function ExploreSidebar({ filters, meta, presets, onChange, onType, onSavePreset, onRemovePreset }: Props) {
  const [presetName, setPresetName] = useState('')

  const set = (patch: Partial<ExploreFilters>) => onChange({ ...filters, ...patch, page: 1 })

  return (
    <div className="filter-panel">
      <section className="filter-group">
        <h3>Categoria</h3>
        <label className="field">
          <span className="sr-only">Categoria</span>
          <select value={filters.categoria ?? ''} onChange={(event) => set({ categoria: event.target.value || null })}>
            <option value="">Todas as categorias</option>
            {meta?.categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.label} ({count(category.count)})
              </option>
            ))}
          </select>
        </label>
      </section>

      <section className="filter-group">
        <h3>Preço de mercado</h3>
        <NumberRange
          min={filters.precoMin}
          max={filters.precoMax}
          hintMin={meta?.priceMin ?? null}
          hintMax={meta?.priceMax ?? null}
          step={1}
          onChange={(precoMin, precoMax) => onType({ ...filters, precoMin, precoMax, page: 1 })}
        />
      </section>

      <section className="filter-group">
        <h3>Índice de oportunidade</h3>
        <NumberRange
          min={filters.oportunidadeMin}
          max={filters.oportunidadeMax}
          hintMin={0}
          hintMax={1}
          step={0.05}
          onChange={(oportunidadeMin, oportunidadeMax) => onType({ ...filters, oportunidadeMin, oportunidadeMax, page: 1 })}
        />
        <p className="filter-hint">Prioridade de 0 a 1 — não é lucro.</p>
      </section>

      <section className="filter-group">
        <h3>Crescimento de visitas</h3>
        <div className="option-row">
          {meta?.growth.map((option) => (
            <button
              key={option.value}
              type="button"
              className={`option ${filters.crescimento === option.value ? 'is-on' : ''}`}
              aria-pressed={filters.crescimento === option.value}
              onClick={() =>
                set({ crescimento: filters.crescimento === option.value ? null : (option.value as GrowthOption) })
              }
            >
              {option.label}
            </button>
          ))}
        </div>
        <p className="filter-hint">Visita não é venda: o que cresce aqui é o interesse.</p>
      </section>

      <section className="filter-group">
        <h3>Concorrência</h3>
        <div className="option-row">
          {meta?.competition.map((option) => (
            <button
              key={option.value}
              type="button"
              className={`option ${filters.concorrencia.includes(option.value as CompetitionBand) ? 'is-on' : ''}`}
              aria-pressed={filters.concorrencia.includes(option.value as CompetitionBand)}
              onClick={() => set({ concorrencia: toggle(filters.concorrencia, option.value as CompetitionBand) })}
            >
              {option.label}
            </button>
          ))}
        </div>
      </section>

      <section className="filter-group">
        <h3>Entrantes desde a coleta anterior</h3>
        <div className="option-row">
          {meta?.newSellers.map((option) => (
            <button
              key={option.value}
              type="button"
              className={`option ${filters.novosVendedores.includes(option.value as NewSellerBand) ? 'is-on' : ''}`}
              aria-pressed={filters.novosVendedores.includes(option.value as NewSellerBand)}
              onClick={() => set({ novosVendedores: toggle(filters.novosVendedores, option.value as NewSellerBand) })}
            >
              {option.label}
            </button>
          ))}
        </div>
        <p className="filter-hint">Produto sem coleta anterior fica fora: não medido não é zero.</p>
      </section>

      <section className="filter-group">
        <h3>Anúncios</h3>
        <div className="option-row">
          {meta?.conditions.map((option) => (
            <button
              key={option.value}
              type="button"
              className={`option ${filters.condicao.includes(option.value as ConditionOption) ? 'is-on' : ''}`}
              aria-pressed={filters.condicao.includes(option.value as ConditionOption)}
              onClick={() => set({ condicao: toggle(filters.condicao, option.value as ConditionOption) })}
            >
              {option.label}
            </button>
          ))}
          <button
            type="button"
            className={`option ${filters.freteGratis ? 'is-on' : ''}`}
            aria-pressed={filters.freteGratis}
            onClick={() => set({ freteGratis: !filters.freteGratis })}
          >
            Tem frete grátis
          </button>
        </div>
      </section>

      <section className="filter-group">
        <h3>Confiança do preço</h3>
        <div className="option-row">
          {meta?.confidence.map((option) => (
            <button
              key={option.value}
              type="button"
              className={`option ${filters.confianca.includes(option.value as ConfidenceOption) ? 'is-on' : ''}`}
              aria-pressed={filters.confianca.includes(option.value as ConfidenceOption)}
              onClick={() => set({ confianca: toggle(filters.confianca, option.value as ConfidenceOption) })}
            >
              {option.label}
            </button>
          ))}
        </div>
      </section>

      <section className="filter-group">
        <h3>Fornecedor</h3>
        <div className="option-row">
          <button
            type="button"
            className={`option ${filters.temFornecedor === true ? 'is-on' : ''}`}
            aria-pressed={filters.temFornecedor === true}
            onClick={() => set({ temFornecedor: filters.temFornecedor === true ? null : true })}
          >
            Já cadastrado
          </button>
          <button
            type="button"
            className={`option ${filters.temFornecedor === false ? 'is-on' : ''}`}
            aria-pressed={filters.temFornecedor === false}
            onClick={() => set({ temFornecedor: filters.temFornecedor === false ? null : false })}
          >
            Ainda sem fornecedor
          </button>
        </div>
      </section>

      <section className="filter-group">
        <h3>Filtros salvos</h3>
        <form
          className="preset-form"
          onSubmit={(event) => {
            event.preventDefault()
            onSavePreset(presetName)
            setPresetName('')
          }}
        >
          <input
            type="text"
            value={presetName}
            maxLength={60}
            placeholder="Nome deste filtro"
            aria-label="Nome do filtro a salvar"
            onChange={(event) => setPresetName(event.target.value)}
          />
          <button type="submit" className="ghost-button" disabled={!presetName.trim()}>Salvar</button>
        </form>

        {presets.length === 0 ? (
          <p className="filter-hint">Nada salvo ainda. Os filtros ficam neste navegador.</p>
        ) : (
          <ul className="preset-list">
            {presets.map((preset) => {
              const stored = filtersFromQuery(preset.query)
              const active = sameFilters(stored, filters)
              return (
                <li key={preset.id}>
                  <button
                    type="button"
                    className={`preset ${active ? 'is-on' : ''}`}
                    aria-current={active ? 'true' : undefined}
                    onClick={() => onChange({ ...stored, page: 1 })}
                  >
                    {preset.name}
                  </button>
                  <button
                    type="button"
                    className="preset-remove"
                    aria-label={`Apagar o filtro ${preset.name}`}
                    onClick={() => onRemovePreset(preset.id)}
                  >
                    ×
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </section>

      <button type="button" className="ghost-button full" onClick={() => onChange(emptyFilters())}>
        Limpar todos os filtros
      </button>
    </div>
  )
}

type RangeProps = {
  min: number | null
  max: number | null
  hintMin: number | null
  hintMax: number | null
  step: number
  onChange: (min: number | null, max: number | null) => void
}

/**
 * Par de campos numéricos com estado local: o que se digita aparece na hora, e o filtro só
 * sai depois que a digitação para. Sem isso, "1500" viraria quatro buscas — e a primeira
 * delas seria por "1".
 */
function NumberRange({ min, max, hintMin, hintMax, step, onChange }: RangeProps) {
  const [low, setLow] = useState(text(min))
  const [high, setHigh] = useState(text(max))

  // O filtro pode mudar por fora (chip removido, preset aplicado, botão voltar): os campos
  // acompanham. Enquanto os valores forem os mesmos, nada é reescrito e o cursor não pula.
  useEffect(() => { setLow(text(min)) }, [min])
  useEffect(() => { setHigh(text(max)) }, [max])

  useEffect(() => {
    const timer = setTimeout(() => {
      const nextLow = number(low)
      const nextHigh = number(high)
      if (nextLow !== min || nextHigh !== max) onChange(nextLow, nextHigh)
    }, 400)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [low, high])

  return (
    <div className="range-row">
      <label className="field">
        <span>de</span>
        <input
          type="number"
          inputMode="decimal"
          min={0}
          step={step}
          value={low}
          placeholder={hintMin === null ? '' : String(round(hintMin))}
          onChange={(event) => setLow(event.target.value)}
        />
      </label>
      <label className="field">
        <span>até</span>
        <input
          type="number"
          inputMode="decimal"
          min={0}
          step={step}
          value={high}
          placeholder={hintMax === null ? '' : String(round(hintMax))}
          onChange={(event) => setHigh(event.target.value)}
        />
      </label>
    </div>
  )
}

function text(value: number | null): string {
  return value === null ? '' : String(value)
}

function number(value: string): number | null {
  if (value.trim() === '') return null
  const parsed = Number(value.replace(',', '.'))
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function round(value: number): number {
  return Math.round(value * 100) / 100
}
