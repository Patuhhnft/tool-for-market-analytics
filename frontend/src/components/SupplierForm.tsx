import { useState, type FormEvent } from 'react'
import { ApiError } from '../lib/api'
import type { SupplierInput } from '../lib/types'

type Props = {
  productName: string | null
  onSubmit: (input: SupplierInput) => Promise<void>
}

const empty = {
  name: '',
  unitPrice: '',
  currency: 'USD',
  minimumOrder: '1',
  shipmentQuantity: '10',
  internationalFreightTotal: '0',
  contact: '',
  whatsApp: '',
  url: ''
}

/**
 * Os lugares onde procurar o produto. São só links de busca: nenhum deles tem integração
 * automática, e o preço continua sendo digitado à mão.
 */
function buscas(query: string) {
  return [
    { nome: 'AliExpress', url: `https://pt.aliexpress.com/wholesale?SearchText=${query}`, dica: 'Varejo e atacado leve, em dólar' },
    { nome: 'Alibaba', url: `https://www.alibaba.com/trade/search?SearchText=${query}`, dica: 'Atacado internacional, em dólar' },
    {
      nome: '1688',
      url: `https://s.1688.com/selloffer/offer_search.htm?keywords=${query}`,
      // O 1688 é o atacado doméstico chinês: preços menores, site em chinês e preço em yuan.
      // Buscar por nome em português costuma não achar nada — traduza o termo antes.
      dica: 'Atacado doméstico chinês, em yuan — busque com o termo em chinês'
    }
  ]
}

/**
 * Cadastro manual de fornecedor. A busca é feita por você, nos links acima — não há fonte
 * automática ativa: a API de afiliados do AliExpress exige aprovação, o 1688 só abre API para
 * empresa registrada na China, e o §12 manda evitar scraping. Ao salvar, a margem do produto
 * é recalculada na hora.
 */
export function SupplierForm({ productName, onSubmit }: Props) {
  const [form, setForm] = useState(empty)
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [errors, setErrors] = useState<string[]>([])

  const set = (field: keyof typeof empty) => (value: string) => setForm((current) => ({ ...current, [field]: value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setErrors([])
    try {
      await onSubmit({
        name: form.name.trim(),
        unitPrice: Number(form.unitPrice.replace(',', '.')),
        currency: form.currency.trim().toUpperCase(),
        minimumOrder: Number(form.minimumOrder),
        shipmentQuantity: Number(form.shipmentQuantity),
        internationalFreightTotal: Number(form.internationalFreightTotal.replace(',', '.')),
        contact: form.contact.trim() || null,
        whatsApp: form.whatsApp.trim() || null,
        url: form.url.trim() || null
      })
      setForm(empty)
      setOpen(false)
    } catch (error) {
      setErrors(error instanceof ApiError ? [error.message, ...error.details] : ['Não foi possível salvar.'])
    } finally {
      setSaving(false)
    }
  }

  const query = productName ? encodeURIComponent(productName) : null

  return (
    <div className="supplier-form">
      {query && (
        <p className="supplier-search">
          Procurar fornecedor:{' '}
          {buscas(query).map((busca, indice) => (
            <span key={busca.nome}>
              {indice > 0 && ' · '}
              <a href={busca.url} target="_blank" rel="noreferrer" title={busca.dica}>{busca.nome}</a>
            </span>
          ))}
        </p>
      )}

      {!open ? (
        <button type="button" className="ghost-button" onClick={() => setOpen(true)}>+ Cadastrar fornecedor</button>
      ) : (
        <form onSubmit={(event) => void submit(event)}>
          <div className="form-grid">
            <Field label="Nome" value={form.name} onChange={set('name')} required />
            <Field label="Preço unitário" value={form.unitPrice} onChange={set('unitPrice')} inputMode="decimal" required />
            <Field label="Moeda" value={form.currency} onChange={set('currency')} hint="USD: a PTAX usada é a do dólar" />
            <Field label="Pedido mínimo" value={form.minimumOrder} onChange={set('minimumOrder')} inputMode="numeric" />
            <Field label="Unidades por remessa" value={form.shipmentQuantity} onChange={set('shipmentQuantity')} inputMode="numeric"
              hint="Tributos são por remessa" />
            <Field label="Frete da remessa" value={form.internationalFreightTotal} onChange={set('internationalFreightTotal')}
              inputMode="decimal" hint="Total, na moeda do fornecedor" />
            <Field label="WhatsApp" value={form.whatsApp} onChange={set('whatsApp')} hint="Com código do país: +86 138…" />
            <Field label="Site" value={form.url} onChange={set('url')} />
            <Field label="Contato" value={form.contact} onChange={set('contact')} />
          </div>

          {errors.length > 0 && (
            <ul className="form-errors" role="alert">{errors.map((error) => <li key={error}>{error}</li>)}</ul>
          )}

          <div className="form-actions">
            <button type="submit" className="primary-button small" disabled={saving}>{saving ? 'Salvando…' : 'Salvar e calcular margem'}</button>
            <button type="button" className="ghost-button" onClick={() => setOpen(false)}>Cancelar</button>
          </div>
        </form>
      )}
    </div>
  )
}

type FieldProps = {
  label: string
  value: string
  onChange: (value: string) => void
  required?: boolean
  hint?: string
  inputMode?: 'decimal' | 'numeric'
}

function Field({ label, value, onChange, required, hint, inputMode }: FieldProps) {
  return (
    <label className="field">
      <span>{label}</span>
      <input value={value} onChange={(event) => onChange(event.target.value)} required={required} inputMode={inputMode} />
      {hint && <small>{hint}</small>}
    </label>
  )
}
