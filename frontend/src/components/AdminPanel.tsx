import { useCallback, useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import { api, ApiError, getAdminKey, setAdminKey } from '../lib/api'
import { dateTime, day, percent } from '../lib/format'
import type { ConfigOverview, Parameters, TaxRule } from '../lib/types'
import '../styles/admin.css'

/**
 * Área do administrador. Lê e grava a pasta config/ — a mesma que um agente pode abastecer
 * soltando arquivos. Toda mudança é uma VERSÃO NOVA com data de vigência: o que valia antes
 * continua valendo para as datas antigas, e cálculos passados seguem auditáveis.
 */
export function AdminPanel() {
  const [overview, setOverview] = useState<ConfigOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [key, setKey] = useState(getAdminKey())

  const load = useCallback(async () => {
    try {
      setOverview(await api.config())
      setError(null)
    } catch (failure) {
      setError(failure instanceof ApiError ? failure.message : 'Não foi possível ler a configuração.')
    }
  }, [])

  useEffect(() => {
    let active = true
    api.config().then(
      (data) => { if (active) setOverview(data) },
      (failure: unknown) => { if (active) setError(failure instanceof ApiError ? failure.message : 'Não foi possível ler a configuração.') })
    return () => { active = false }
  }, [])

  return (
    <section className="admin">
      <div className="board-bar">
        <div>
          <p className="section-kicker">Administração</p>
          <h2>Configuração de negócio</h2>
          <p className="board-meta">{overview ? `Pasta: ${overview.folder}` : 'Carregando…'}</p>
        </div>
        <div className="board-actions">
          <button type="button" className="ghost-button" onClick={() => void load()}>Recarregar pasta</button>
        </div>
        <label className="admin-key">
          <span>Chave do administrador</span>
          <input
            type="password"
            value={key}
            placeholder="vazio = só nesta máquina"
            onChange={(event) => { setKey(event.target.value); setAdminKey(event.target.value) }}
          />
        </label>
      </div>

      {error && <p className="notice notice-alert" role="alert">{error}</p>}

      {overview && (
        <>
          <EffectiveSummary overview={overview} />
          <FilesTable overview={overview} />
          <TaxRuleForm overview={overview} onSaved={setOverview} />
          <ParametersForm overview={overview} onSaved={setOverview} />
          <UploadForm onSaved={setOverview} />
        </>
      )}
    </section>
  )
}

// ------------------------------------------------------------------------------------
// Leitura
// ------------------------------------------------------------------------------------

function EffectiveSummary({ overview }: { overview: ConfigOverview }) {
  const effective = overview.effective

  if (!effective) {
    return (
      <div className="notice notice-alert" role="alert">
        <strong>Nenhuma configuração vigente hoje — a coleta não roda assim.</strong>
        <ul>{overview.problems.map((problem) => <li key={problem}>{problem}</li>)}</ul>
      </div>
    )
  }

  const tax = effective.taxRule.entry
  const parameters = effective.parameters.entry

  return (
    <div className="admin-grid">
      <article className="admin-card">
        <p className="section-kicker">Tributos vigentes em {day(effective.date)}</p>
        <h3>{tax.regime}</h3>
        <ul className="brackets">
          {tax.faixasImpostoImportacao.map((bracket, index) => (
            <li key={index}>
              {bracket.ateUsd ? `até US$ ${bracket.ateUsd.toLocaleString('pt-BR')}` : 'sem teto'}: II {percent(bracket.aliquota, 0)}
              {bracket.deducaoUsd ? ` − US$ ${bracket.deducaoUsd}` : ''}
            </li>
          ))}
        </ul>
        <p>ICMS {percent(tax.icms, 0)} (por dentro){tax.ipi ? ` · IPI ${percent(tax.ipi)}` : ''}{tax.pis ? ` · PIS ${percent(tax.pis, 2)}` : ''}{tax.cofins ? ` · Cofins ${percent(tax.cofins, 2)}` : ''}</p>
        <p className="source">Desde {day(tax.vigenteDesde)} · {effective.taxRule.file}</p>
        <p className="source">Fonte: {tax.fonte}</p>
        {tax.observacao && <p className="note">{tax.observacao}</p>}
      </article>

      <article className="admin-card">
        <p className="section-kicker">Parâmetros vigentes</p>
        <h3>Desde {day(parameters.vigenteDesde)}</h3>
        <dl className="param-list">
          <div><dt>Imposto sobre a venda</dt><dd>{percent(parameters.impostoSobreVenda)}</dd></div>
          <div><dt>Margem-alvo</dt><dd>{percent(parameters.margemAlvo, 0)}</dd></div>
          <div><dt>Spread cambial</dt><dd>{percent(parameters.spreadCambial)}</dd></div>
          <div><dt>Tipo de anúncio</dt><dd>{parameters.tipoAnuncio === 'gold_pro' ? 'Premium' : 'Clássico'}</dd></div>
          <div><dt>Frete absorvido / un.</dt><dd>R$ {parameters.freteAbsorvidoPorUnidade}</dd></div>
          <div><dt>Frete nacional / remessa</dt><dd>R$ {parameters.freteNacionalPorRemessa}</dd></div>
          <div><dt>Categorias</dt><dd>{parameters.categorias.join(', ')}</dd></div>
        </dl>
        {parameters.observacao && <p className="note">{parameters.observacao}</p>}
      </article>
    </div>
  )
}

function FilesTable({ overview }: { overview: ConfigOverview }) {
  return (
    <div className="admin-card wide">
      <p className="section-kicker">Arquivos da pasta</p>
      <table className="files-table">
        <thead>
          <tr><th>Arquivo</th><th>Situação</th><th>Tributos</th><th>Parâmetros</th><th>Alterado</th><th /></tr>
        </thead>
        <tbody>
          {overview.files.map((file) => (
            <tr key={file.fileName} className={file.accepted ? '' : 'is-rejected'}>
              <td>{file.fileName}{file.fileName === overview.adminFile && <small> · editado por esta tela</small>}</td>
              <td>
                {file.accepted ? <span className="badge badge-good">aceito</span> : <span className="badge badge-alert">rejeitado inteiro</span>}
                {file.errors.length > 0 && <ul className="file-errors">{file.errors.map((e) => <li key={e}>{e}</li>)}</ul>}
              </td>
              <td>{file.taxRules}</td>
              <td>{file.parameters}</td>
              <td>{dateTime(file.lastModified)}</td>
              <td><a href={api.fileUrl(file.fileName)} target="_blank" rel="noreferrer">ver</a></td>
            </tr>
          ))}
        </tbody>
      </table>
      {overview.errors.length > 0 && (
        <ul className="file-errors">{overview.errors.map((e) => <li key={e}>{e}</li>)}</ul>
      )}
      <p className="note">
        Um arquivo com qualquer erro é ignorado por inteiro — nunca aplicado pela metade — e os demais continuam valendo.
        Um agente pode soltar arquivos novos na pasta; eles aparecem aqui na próxima leitura.
      </p>
    </div>
  )
}

// ------------------------------------------------------------------------------------
// Escrita
// ------------------------------------------------------------------------------------

const toPercent = (fraction: number | undefined) => (fraction === undefined ? '' : String(+(fraction * 100).toFixed(4)))
const toFraction = (text: string) => Number(text.replace(',', '.')) / 100
const toNumber = (text: string) => Number(text.replace(',', '.'))
const tomorrow = () => new Date(Date.now() + 86_400_000).toISOString().slice(0, 10)

type BracketRow = { ateUsd: string; aliquota: string; deducaoUsd: string }

function TaxRuleForm({ overview, onSaved }: { overview: ConfigOverview; onSaved: (overview: ConfigOverview) => void }) {
  const base = overview.effective?.taxRule.entry
  const [open, setOpen] = useState(false)
  const [regime, setRegime] = useState(base?.regime ?? '')
  const [from, setFrom] = useState(tomorrow())
  const [rows, setRows] = useState<BracketRow[]>(() =>
    (base?.faixasImpostoImportacao ?? [{ aliquota: 0.6 }]).map((b) => ({
      ateUsd: b.ateUsd ? String(b.ateUsd) : '',
      aliquota: toPercent(b.aliquota),
      deducaoUsd: b.deducaoUsd ? String(b.deducaoUsd) : ''
    })))
  const [rates, setRates] = useState({
    icms: toPercent(base?.icms), ipi: toPercent(base?.ipi ?? 0), pis: toPercent(base?.pis ?? 0), cofins: toPercent(base?.cofins ?? 0)
  })
  const [state, setState] = useState(base?.estado ?? '')
  const [source, setSource] = useState('')
  const [note, setNote] = useState('')
  const [errors, setErrors] = useState<string[]>([])

  const regimes = [...new Set(overview.taxRules.map((rule) => rule.entry.regime))]

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const rule: TaxRule = {
      regime: regime.trim(),
      vigenteDesde: from,
      faixasImpostoImportacao: rows.map((row) => ({
        ateUsd: row.ateUsd ? toNumber(row.ateUsd) : null,
        aliquota: toFraction(row.aliquota),
        deducaoUsd: row.deducaoUsd ? toNumber(row.deducaoUsd) : 0
      })),
      icms: toFraction(rates.icms),
      ipi: toFraction(rates.ipi || '0'),
      pis: toFraction(rates.pis || '0'),
      cofins: toFraction(rates.cofins || '0'),
      estado: state.trim().toUpperCase() || null,
      fonte: source.trim(),
      observacao: note.trim() || null
    }

    try {
      onSaved(await api.addTaxRule(rule))
      setErrors([])
      setOpen(false)
      setSource('')
      setNote('')
    } catch (failure) {
      setErrors(failure instanceof ApiError ? [failure.message, ...failure.details] : ['Não foi possível salvar.'])
    }
  }

  const setRow = (index: number, field: keyof BracketRow) => (event: ChangeEvent<HTMLInputElement>) =>
    setRows((current) => current.map((row, i) => (i === index ? { ...row, [field]: event.target.value } : row)))

  if (!open) {
    return <button type="button" className="primary-button small admin-open" onClick={() => setOpen(true)}>Nova versão de tributos</button>
  }

  return (
    <form className="admin-card wide" onSubmit={(event) => void submit(event)}>
      <p className="section-kicker">Nova versão de tributos</p>
      <p className="note">Preenchida com a versão vigente. Mude só o que mudou; a anterior continua valendo até a véspera da nova vigência.</p>

      <div className="form-grid">
        <label className="field">
          <span>Regime</span>
          <input list="regimes" value={regime} onChange={(event) => setRegime(event.target.value)} required />
          <datalist id="regimes">{regimes.map((name) => <option key={name} value={name} />)}</datalist>
        </label>
        <label className="field"><span>Vigente desde</span><input type="date" value={from} onChange={(event) => setFrom(event.target.value)} required /></label>
        <label className="field"><span>Estado (UF)</span><input value={state} maxLength={2} onChange={(event) => setState(event.target.value)} /></label>
      </div>

      <table className="bracket-table">
        <thead><tr><th>Até (US$)</th><th>Alíquota do II (%)</th><th>Dedução (US$)</th><th /></tr></thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              <td><input value={row.ateUsd} onChange={setRow(index, 'ateUsd')} placeholder="sem teto" inputMode="decimal" /></td>
              <td><input value={row.aliquota} onChange={setRow(index, 'aliquota')} inputMode="decimal" required /></td>
              <td><input value={row.deducaoUsd} onChange={setRow(index, 'deducaoUsd')} placeholder="0" inputMode="decimal" /></td>
              <td>{rows.length > 1 && <button type="button" className="ghost-button danger" onClick={() => setRows(rows.filter((_, i) => i !== index))}>remover</button>}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <button type="button" className="ghost-button" onClick={() => setRows([...rows, { ateUsd: '', aliquota: '', deducaoUsd: '' }])}>+ faixa</button>

      <div className="form-grid">
        <label className="field"><span>ICMS (%)</span><input value={rates.icms} onChange={(e) => setRates({ ...rates, icms: e.target.value })} inputMode="decimal" required /></label>
        <label className="field"><span>IPI (%)</span><input value={rates.ipi} onChange={(e) => setRates({ ...rates, ipi: e.target.value })} inputMode="decimal" /></label>
        <label className="field"><span>PIS (%)</span><input value={rates.pis} onChange={(e) => setRates({ ...rates, pis: e.target.value })} inputMode="decimal" /></label>
        <label className="field"><span>Cofins (%)</span><input value={rates.cofins} onChange={(e) => setRates({ ...rates, cofins: e.target.value })} inputMode="decimal" /></label>
      </div>

      <label className="field"><span>Fonte (obrigatória)</span><input value={source} onChange={(event) => setSource(event.target.value)} placeholder="norma, URL e data da consulta" required /></label>
      <label className="field"><span>Observação</span><input value={note} onChange={(event) => setNote(event.target.value)} /></label>

      {errors.length > 0 && <ul className="form-errors" role="alert">{errors.map((e) => <li key={e}>{e}</li>)}</ul>}

      <div className="form-actions">
        <button type="submit" className="primary-button small">Salvar nova versão</button>
        <button type="button" className="ghost-button" onClick={() => setOpen(false)}>Cancelar</button>
      </div>
    </form>
  )
}

function ParametersForm({ overview, onSaved }: { overview: ConfigOverview; onSaved: (overview: ConfigOverview) => void }) {
  const base = overview.effective?.parameters.entry ?? overview.parameters[0]?.entry
  const [open, setOpen] = useState(false)
  const [values, setValues] = useState(() => ({
    vigenteDesde: tomorrow(),
    regimeImportacao: base?.regimeImportacao ?? '',
    impostoSobreVenda: toPercent(base?.impostoSobreVenda),
    margemAlvo: toPercent(base?.margemAlvo),
    spreadCambial: toPercent(base?.spreadCambial),
    tipoAnuncio: base?.tipoAnuncio ?? 'gold_special',
    freteAbsorvidoPorUnidade: String(base?.freteAbsorvidoPorUnidade ?? ''),
    freteNacionalPorRemessa: String(base?.freteNacionalPorRemessa ?? ''),
    despesasAduaneirasPorRemessa: String(base?.despesasAduaneirasPorRemessa ?? 0),
    categorias: (base?.categorias ?? []).join(', '),
    observacao: ''
  }))
  const [errors, setErrors] = useState<string[]>([])

  if (!base) return null

  const regimes = [...new Set(overview.taxRules.map((rule) => rule.entry.regime))]
  const set = (field: keyof typeof values) => (event: ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setValues((current) => ({ ...current, [field]: event.target.value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()

    // O que a tela não edita (faixas de tarifa, calibração de demanda, modelo de preço)
    // é herdado da versão vigente, sem mudança.
    const next: Parameters = {
      ...base,
      vigenteDesde: values.vigenteDesde,
      regimeImportacao: values.regimeImportacao,
      impostoSobreVenda: toFraction(values.impostoSobreVenda),
      margemAlvo: toFraction(values.margemAlvo),
      spreadCambial: toFraction(values.spreadCambial),
      tipoAnuncio: values.tipoAnuncio,
      freteAbsorvidoPorUnidade: toNumber(values.freteAbsorvidoPorUnidade),
      freteNacionalPorRemessa: toNumber(values.freteNacionalPorRemessa),
      despesasAduaneirasPorRemessa: toNumber(values.despesasAduaneirasPorRemessa || '0'),
      categorias: values.categorias.split(/[,\s]+/).map((c) => c.trim().toUpperCase()).filter(Boolean),
      observacao: values.observacao.trim() || null
    }

    try {
      onSaved(await api.addParameters(next))
      setErrors([])
      setOpen(false)
    } catch (failure) {
      setErrors(failure instanceof ApiError ? [failure.message, ...failure.details] : ['Não foi possível salvar.'])
    }
  }

  if (!open) {
    return <button type="button" className="primary-button small admin-open" onClick={() => setOpen(true)}>Nova versão de parâmetros</button>
  }

  return (
    <form className="admin-card wide" onSubmit={(event) => void submit(event)}>
      <p className="section-kicker">Nova versão de parâmetros</p>
      <div className="form-grid">
        <label className="field"><span>Vigente desde</span><input type="date" value={values.vigenteDesde} onChange={set('vigenteDesde')} required /></label>
        <label className="field">
          <span>Regime de importação</span>
          <select value={values.regimeImportacao} onChange={set('regimeImportacao')}>
            {regimes.map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <label className="field"><span>Imposto sobre a venda (%)</span><input value={values.impostoSobreVenda} onChange={set('impostoSobreVenda')} inputMode="decimal" required />
          <small>MEI = 0 · Simples Anexo I 1ª faixa = 4</small></label>
        <label className="field"><span>Margem-alvo (%)</span><input value={values.margemAlvo} onChange={set('margemAlvo')} inputMode="decimal" required /></label>
        <label className="field"><span>Spread cambial (%)</span><input value={values.spreadCambial} onChange={set('spreadCambial')} inputMode="decimal" required /></label>
        <label className="field">
          <span>Tipo de anúncio</span>
          <select value={values.tipoAnuncio} onChange={set('tipoAnuncio')}>
            <option value="gold_special">Clássico</option>
            <option value="gold_pro">Premium</option>
          </select>
        </label>
        <label className="field"><span>Frete absorvido por unidade (R$)</span><input value={values.freteAbsorvidoPorUnidade} onChange={set('freteAbsorvidoPorUnidade')} inputMode="decimal" required /></label>
        <label className="field"><span>Frete nacional por remessa (R$)</span><input value={values.freteNacionalPorRemessa} onChange={set('freteNacionalPorRemessa')} inputMode="decimal" required /></label>
        <label className="field"><span>Despesas aduaneiras por remessa (R$)</span><input value={values.despesasAduaneirasPorRemessa} onChange={set('despesasAduaneirasPorRemessa')} inputMode="decimal" /></label>
      </div>
      <label className="field"><span>Categorias monitoradas</span><input value={values.categorias} onChange={set('categorias')} required /></label>
      <label className="field"><span>Observação</span><input value={values.observacao} onChange={set('observacao')} /></label>

      {errors.length > 0 && <ul className="form-errors" role="alert">{errors.map((e) => <li key={e}>{e}</li>)}</ul>}

      <div className="form-actions">
        <button type="submit" className="primary-button small">Salvar nova versão</button>
        <button type="button" className="ghost-button" onClick={() => setOpen(false)}>Cancelar</button>
      </div>
    </form>
  )
}

function UploadForm({ onSaved }: { onSaved: (overview: ConfigOverview) => void }) {
  const [errors, setErrors] = useState<string[]>([])
  const [done, setDone] = useState<string | null>(null)

  const upload = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return

    setErrors([])
    setDone(null)
    try {
      onSaved(await api.uploadConfig(file.name.toLowerCase(), await file.text()))
      setDone(`${file.name} validado e gravado na pasta.`)
    } catch (failure) {
      setErrors(failure instanceof ApiError ? [failure.message, ...failure.details] : ['Não foi possível enviar.'])
    }
  }

  return (
    <div className="admin-card wide">
      <p className="section-kicker">Enviar arquivo JSON</p>
      <p className="note">O arquivo só chega à pasta se passar na validação completa. Formato em <code>config/configuracoes.schema.json</code>.</p>
      <input type="file" accept=".json,application/json" onChange={(event) => void upload(event)} />
      {done && <p className="notice notice-info">{done}</p>}
      {errors.length > 0 && <ul className="form-errors" role="alert">{errors.map((e) => <li key={e}>{e}</li>)}</ul>}
    </div>
  )
}
