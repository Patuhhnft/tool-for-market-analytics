import { useState } from 'react'
import { api, ApiError } from '../lib/api'
import { confidenceLabel, count, dateTime, day, foreign, money, percent, price, quadrantLabel, signedPercent } from '../lib/format'
import type { CeilingPayload, ProductCard, MarginPayload, ProductRiskPayload, RunwayPayload, Supplier, SupplierInput, VisitGrowth } from '../lib/types'
import { Sparkline } from './Sparkline'
import { SupplierForm } from './SupplierForm'
import '../styles/card.css'

type Props = {
  card: ProductCard
  onChanged: () => Promise<void>
}

export function ProductCardView({ card, onChanged }: Props) {
  const [message, setMessage] = useState<string | null>(null)
  const title = card.name ?? card.productId

  const addSupplier = async (input: SupplierInput) => {
    await api.addSupplier(card.productId, input)
    await onChanged()
  }

  const removeSupplier = async (supplier: Supplier) => {
    if (!window.confirm(`Desativar ${supplier.name}? A margem será recalculada sem ele.`)) return
    try {
      await api.removeSupplier(supplier.id)
      await onChanged()
    } catch (error) {
      setMessage(error instanceof ApiError ? error.message : 'Não foi possível desativar.')
    }
  }

  return (
    <article className={`product-card ${card.lowDemand ? 'is-low-demand' : ''}`}>
      <header className="card-header">
        <div className="card-title">
          <p className="section-kicker">{card.categoryId} · {card.productId}</p>
          <h3>{title}</h3>
        </div>
        <div className="card-score">
          {card.demand.growth?.isNewDemand && <span className="quadrant quadrant-Debut">estreia</span>}
          {card.quadrant && <span className={`quadrant quadrant-${card.quadrant}`}>{quadrantLabel[card.quadrant]}</span>}
          <strong title="Ordem de prioridade entre os produtos, não placar de lucro: quem promete dinheiro é o bloco de margem.">
            {card.opportunityIndex === null ? '—' : card.opportunityIndex.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </strong>
          <small>índice de prioridade{card.opportunity && !card.opportunity.includesMargin ? ' · sem margem' : ''}</small>
        </div>
      </header>

      <p className="why-now">{card.explanation}</p>

      <DemandBlock card={card} />

      <p className="competition">
        <strong>{count(card.sellers)}</strong> {card.sellers === 1 ? 'vendedor' : 'vendedores'}
        {' · '}
        {card.newSellersThisWeek > 0 ? <><strong>{card.newSellersThisWeek}</strong> {card.newSellersThisWeek === 1 ? 'novo' : 'novos'} na semana</> : 'nenhum novo na semana'}
      </p>

      <PriceBlock card={card} />

      {card.price.officialStoreSellers > 0 && (
        <p className={`notice ${card.price.flags.includes('BrandDominated') ? 'notice-alert' : 'notice-warn'}`}>
          {card.price.flags.includes('BrandDominated') ? (
            <>
              <strong>Vendido pela própria marca.</strong> A loja oficial ocupa{' '}
              {percent(card.price.officialStoreShare, 0)} dos anúncios — você briga por visibilidade
              antes de brigar por preço.
            </>
          ) : (
            <>A marca também vende este produto{card.price.officialStorePrice !== null && <>, a {money(card.price.officialStorePrice)}</>}.</>
          )}
        </p>
      )}

      {card.risks && <RisksBlock payload={card.risks} />}

      {card.runway && <RunwayBlock payload={card.runway} />}

      {card.ceiling && !card.margin?.margin && <CeilingBlock payload={card.ceiling} />}

      {card.price.status === 'Calculated' && <MarginBlock margin={card.margin} hasSuppliers={card.suppliers.length > 0} />}

      <section className="card-block">
        <h4>Fornecedores</h4>
        {card.suppliers.length === 0 && (
          <p className="muted-note">
            {card.ceiling?.ceiling.viable
              ? 'O teto acima já diz quanto vale a pena pagar. Cadastre o fornecedor para ver a margem real que ele entrega.'
              : 'Sem fornecedor não existe custo — a margem aparece assim que você cadastrar um.'}
          </p>
        )}
        <ul className="supplier-list">
          {card.suppliers.map((supplier) => (
            <li key={supplier.id}>
              <div>
                <strong>{supplier.name}</strong>
                <span>{foreign(supplier.unitPrice, supplier.currency)} / un · mínimo {count(supplier.minimumOrder)} · remessa de {count(supplier.shipmentQuantity)}</span>
              </div>
              <div className="supplier-actions">
                {supplier.whatsAppLink && <a className="contact-button whatsapp" href={supplier.whatsAppLink} target="_blank" rel="noreferrer">WhatsApp</a>}
                {supplier.url && <a className="contact-button" href={supplier.url} target="_blank" rel="noreferrer">Site</a>}
                <button type="button" className="ghost-button danger" onClick={() => void removeSupplier(supplier)}>Desativar</button>
              </div>
            </li>
          ))}
        </ul>
        <SupplierForm productName={card.name} onSubmit={addSupplier} />
      </section>

      {message && <p className="form-errors" role="alert">{message}</p>}

      <footer className="card-footer">
        <span>Calculado em {dateTime(card.calculatedAt)} · ciclo {card.cycleId}</span>
      </footer>
    </article>
  )
}

function DemandBlock({ card }: { card: ProductCard }) {
  const demand = card.demand.result

  return (
    <section className="card-block">
      <div className="block-heading">
        <h4>Visitas diárias</h4>
        <div className="badges">
          {demand.source === 'Ranking' && <span className="badge badge-warn">demanda estimada por posição</span>}
          {demand.source === 'Visits' && demand.lowDemand && (
            <span className="badge badge-warn">demanda baixa · {count(demand.lastWeekVisits)} visitas na semana</span>
          )}
        </div>
      </div>

      {demand.source === 'Visits' && (
        <>
          <p className="demand-headline">
            <strong>{count(demand.lastWeekVisits)}</strong> visitas na última semana
            {demand.absoluteDelta !== null && (
              <span className={demand.absoluteDelta >= 0 ? 'delta-up' : 'delta-down'}>
                {' '}({variacao(demand.absoluteDelta, card.demand.growth)})
              </span>
            )}
          </p>
          <Sparkline series={demand.series} label={`Visitas diárias de ${card.name ?? card.productId}`} />
          <p className="muted-note">Visita não é venda: a conversão varia por categoria e preço. Soma dos {card.demand.itemsInSeries.length} anúncios mais visitados.</p>
        </>
      )}

      {demand.source === 'Ranking' && (
        <p className="demand-headline">
          {demand.currentPosition}º no ranking de mais vendidos
          {demand.positionGain !== null && demand.positionGain !== 0 && ` (${demand.positionGain > 0 ? 'subiu' : 'caiu'} ${Math.abs(demand.positionGain)} em 7 dias)`}
        </p>
      )}

      {demand.source !== 'Visits' && card.demand.fallbackReason && <p className="muted-note">Sem série de visitas: {card.demand.fallbackReason}.</p>}
    </section>
  )
}

function PriceBlock({ card }: { card: ProductCard }) {
  const p = card.price

  if (p.status !== 'Calculated') {
    return (
      <section className="card-block">
        <h4>Preço e margem</h4>
        <p className="notice notice-warn">
          {p.status === 'InsufficientData'
            ? `Dados insuficientes: só ${p.sellersFound} ${p.sellersFound === 1 ? 'vendedor' : 'vendedores'} válidos. Preço e margem não são exibidos.`
            : 'Sem âncora de catálogo: não há como afirmar que os anúncios são do mesmo produto.'}
        </p>
      </section>
    )
  }

  const arrow = card.priceTrend === 'up' ? '▲' : card.priceTrend === 'down' ? '▼' : card.priceTrend === 'flat' ? '—' : ''
  const trendTitle = p.weeklyMarketPriceChange === null ? 'Sem semana anterior para comparar' : `${signedPercent(p.weeklyMarketPriceChange, 1)} na semana`

  return (
    <section className="card-block">
      <div className="block-heading">
        <h4>Preço de mercado</h4>
        <div className="badges">
          {card.priceUnstable && <span className="badge badge-warn">preço instável</span>}
          {p.flags.includes('SmallSample') && <span className="badge badge-warn">amostra pequena</span>}
        </div>
      </div>

      <dl className="figures">
        <div className="figure-row">
          <dt>Preço de mercado</dt>
          <dd>
            <strong className="big-number">{price(p.marketPrice)}</strong>
            {arrow && <span className={`trend trend-${card.priceTrend}`} title={trendTitle}>{arrow}</span>}
          </dd>
        </div>
        <div className="figure-row sub">
          <dt>fonte</dt>
          <dd>{p.source === 'Mode' ? `moda · ${percent(p.modeStrength, 0)} (${p.modalBucketSellers} ${p.modalBucketSellers === 1 ? 'vendedor' : 'vendedores'})` : 'mediana · mercado disperso'}</dd>
        </div>
        <div className="figure-row sub">
          <dt>confiança</dt>
          <dd>{confidenceLabel[p.confidence ?? 'Low']} · {p.sampleSize} de {p.sellersFound} vendedores após o corte</dd>
        </div>
        <div className="figure-row">
          <dt>Faixa praticada (p15–p85)</dt>
          <dd>{price(p.cleanRangeLow)} – {price(p.cleanRangeHigh)}</dd>
        </div>
        <div className="figure-row">
          <dt>Preço de vitrine sugerido</dt>
          <dd>{money(p.suggestedShowcasePrice)}</dd>
        </div>
      </dl>
    </section>
  )
}

/**
 * "Quanto posso pagar por isto?" — a pergunta que dá para responder antes de existir
 * fornecedor. Só depende do preço que o mercado pratica, das tarifas do Mercado Livre e da
 * margem-alvo, e por isso aparece já no primeiro ciclo.
 */
/**
 * Riscos que não aparecem em nenhum outro número: certificação exigida (que pode pausar o
 * anúncio e reter a carga — prejuízo TOTAL, não margem menor) e a saída do kit para produto
 * barato, onde a tarifa fixa do ML come a margem.
 */
function RisksBlock({ payload }: { payload: ProductRiskPayload }) {
  const { certifications, kits, breakageReserve } = payload
  const melhorKit = kits.length > 0 ? kits.reduce((a, b) => (b.gain > a.gain ? b : a)) : null

  if (certifications.length === 0 && !melhorKit && breakageReserve === 0) return null

  return (
    <section className="card-block">
      <h4>Antes de comprar</h4>

      {certifications.map((item) => (
        <p key={item.body} className="notice notice-warn">
          <strong>Pode exigir {item.body}.</strong> {item.note}
        </p>
      ))}

      {breakageReserve > 0 && (
        <p className="muted-note">
          A margem já desconta {percent(breakageReserve, 0)} de quebra/devolução e{' '}
          {money(payload.packagingPerUnit)} de embalagem por unidade.
        </p>
      )}

      {melhorKit && melhorKit.gain > 0 && (
        <dl className="figures compact">
          <div className="figure-row">
            <dt>Vendendo em kit de {melhorKit.units}</dt>
            <dd>
              {percent(melhorKit.margin)} <small>contra {percent(melhorKit.marginPerUnitSale)} avulso</small>
            </dd>
          </div>
        </dl>
      )}

      {melhorKit && melhorKit.gain > 0 && (
        <p className="muted-note">
          A tarifa fixa do Mercado Livre é por VENDA: num kit ela é cobrada uma vez só. Estimativa
          a {money(melhorKit.price)} — kit costuma ter desconto, então sirva-se dela para comparar
          tamanhos, não como promessa.
        </p>
      )}
    </section>
  )
}

const faseLabel: Record<string, string> = {
  Accelerating: 'acelerando',
  Linear: 'estável',
  Decelerating: 'perdendo velocidade',
  Falling: 'caindo',
  Unknown: 'sem série suficiente'
}

/**
 * "Dá tempo de importar?" — a pergunta que o índice de oportunidade não responde.
 *
 * O índice mede o mercado de HOJE. Mas a mercadoria chega em 25 ou 60 dias, e um produto em
 * queda hoje já estará morto quando a caixa desembarcar. Comprar isso é o erro mais caro do
 * importador, e é por isso que este bloco fica ACIMA do teto de compra.
 */
function RunwayBlock({ payload }: { payload: RunwayPayload }) {
  const { runway, channels, minimumSellingWindowDays } = payload
  const desconhecido = runway.lowerWeeks === null

  return (
    <section className={`card-block runway-block ${desconhecido ? '' : payload.worthImporting ? 'is-open' : 'is-closed'}`}>
      <div className="block-heading">
        <h4>Dá tempo de importar?</h4>
        <span className={`badge ${desconhecido ? 'badge-warn' : payload.worthImporting ? 'badge-good' : 'badge-alert'}`}>
          {faseLabel[runway.phase] ?? runway.phase}
        </span>
      </div>

      <p className="runway-headline">
        {desconhecido ? (
          <span className="muted-note">{runway.reason}</span>
        ) : (
          <>
            <strong>{runway.lowerWeeks}–{runway.upperWeeks}</strong> semanas de janela
            {' '}<span className="muted-note">(a decisão usa o piso: {runway.lowerWeeks})</span>
          </>
        )}
      </p>

      <ul className="channel-list">
        {channels.map((channel) => (
          <li key={channel.name} className={channel.fits === null ? 'is-unknown' : channel.fits ? 'is-fit' : 'is-unfit'}>
            <span>{channel.name}</span>
            <span className="channel-verdict">
              {channel.leadTimeDays} dias · {channel.fits === null ? 'não sabemos' : channel.fits ? 'dá tempo' : 'não dá tempo'}
            </span>
          </li>
        ))}
      </ul>

      <p className="muted-note">
        Exige o prazo mais {minimumSellingWindowDays} dias de venda depois da chegada — chegar no
        último dia da janela é estoque parado, não negócio.
      </p>
    </section>
  )
}

/**
 * A variação da semana entre parênteses. O percentual só entra quando ele EXISTE: com a
 * semana anterior zerada não há razão a calcular, e com base minúscula a razão é ruído (I11).
 * Nos dois casos o servidor manda `percent: null` e aqui fica só o número absoluto — que é o
 * que a I11 exige que sempre acompanhe qualquer taxa.
 */
function variacao(delta: number, growth: VisitGrowth | null): string {
  const absoluto = `${delta >= 0 ? '+' : '−'}${count(Math.abs(delta))}`

  if (growth?.percent != null) return `${absoluto} · ${signedPercent(growth.percent / 100)}`
  if (growth?.smallBase) return `${absoluto} · base pequena para percentual`
  return absoluto
}

function CeilingBlock({ payload }: { payload: CeilingPayload }) {
  const teto = payload.ceiling

  if (!teto.viable) {
    return (
      <section className="card-block">
        <h4>Quanto posso pagar</h4>
        <p className="notice notice-warn">
          Nenhum preço de compra fecha {percent(teto.targetMargin, 0)} de margem nesta faixa: as tarifas e o
          imposto sozinhos já comem o alvo. Não é "compre barato" — é "não dá".
        </p>
      </section>
    )
  }

  return (
    <section className="card-block ceiling-block">
      <h4>Quanto posso pagar</h4>

      <p className="ceiling-headline">
        <strong>{money(teto.inRange)}</strong>
        <span>por unidade, já posta na sua mão</span>
      </p>

      <p className="muted-note">
        Produto + frete + impostos, para manter {percent(teto.targetMargin, 0)} de margem em <em>qualquer</em> preço
        da faixa praticada. Aperta em {money(teto.bindingPrice)}.
      </p>

      <dl className="figures compact">
        <div className="figure-row">
          <dt>Vendendo no preço de mercado</dt>
          <dd>{money(teto.atMarket)}</dd>
        </div>
      </dl>

      <p className="muted-note">
        Não depende de fornecedor — é o número que você leva para negociar. Regime{' '}
        <strong>{payload.config.regime}</strong>, parâmetros de {day(payload.config.parametersValidFrom)}.
      </p>
    </section>
  )
}

function MarginBlock({ margin, hasSuppliers }: { margin: MarginPayload | null; hasSuppliers: boolean }) {
  if (!margin) {
    return hasSuppliers ? (
      <section className="card-block"><h4>Margem</h4><p className="muted-note">Recalculando…</p></section>
    ) : null
  }

  if (margin.unavailable || !margin.margin || !margin.landedCost) {
    return (
      <section className="card-block">
        <h4>Margem</h4>
        <p className="notice notice-warn">Margem indisponível: {margin.unavailable ?? 'dados incompletos'}.</p>
      </section>
    )
  }

  const m = margin.margin
  const worst = m.worstInRange

  return (
    <section className="card-block">
      <h4>Margem com {margin.supplierName}</h4>

      <dl className="figures">
        <div className="figure-row">
          <dt>Custo unitário (fornecedor)</dt>
          <dd>{money(m.unitCost)}</dd>
        </div>
        <div className="figure-row">
          <dt>Taxas ML + imposto</dt>
          <dd>{money(m.market.saleCost.total)}</dd>
        </div>
        <div className="figure-row highlight">
          <dt>Lucro no preço de mercado</dt>
          <dd className={m.market.profit < 0 ? 'value-negative' : ''}>{money(m.market.profit)}</dd>
        </div>
        <div className="figure-row highlight stacked">
          <dt>Margem (piso · mercado · teto)</dt>
          <dd>{percent(m.floor.margin)} · {percent(m.market.margin)} · {percent(m.ceiling.margin)}</dd>
        </div>
        <div className={`figure-row worst ${m.worstBelowTarget ? 'is-alert' : 'is-ok'}`}>
          <dt>Pior margem da faixa</dt>
          <dd>
            {percent(worst.margin)} em {money(worst.price)}
            <small>{m.worstBelowTarget ? `abaixo da margem-alvo de ${percent(m.targetMargin, 0)}` : `acima da margem-alvo de ${percent(m.targetMargin, 0)}`}</small>
          </dd>
        </div>
      </dl>

      {m.lossZones.length > 0 && (
        <p className="notice notice-alert">
          {m.lossZones.map((zone) => (
            <span key={zone.from}>Entre {money(zone.from)} e {zone.to > 1e12 ? 'qualquer preço acima' : money(zone.to)} cada venda dá prejuízo: o frete absorvido come o lucro. </span>
          ))}
        </p>
      )}

      <dl className="figures compact">
        <div className="figure-row"><dt>Preço mínimo de venda</dt><dd>{money(m.breakEvenPrice)}</dd></div>
        <div className="figure-row">
          <dt>Teto de compra seguro</dt>
          <dd>{money(m.maxUnitCostInRange)} por unidade{margin.maxSupplierUnitPrice !== null && <> · até <strong>{foreign(margin.maxSupplierUnitPrice, margin.currency)}</strong> no fornecedor</>}</dd>
        </div>
        <div className="figure-row"><dt>ROI no preço de mercado</dt><dd>{percent(m.roiAtMarket)}</dd></div>
      </dl>

      <details className="memo">
        <summary>Memória de cálculo</summary>
        <table>
          <tbody>
            {margin.landedCost.memo.map((line) => (
              <tr key={line.label}><th scope="row">{line.label}</th><td>{money(line.value)}</td><td>{line.formula}</td></tr>
            ))}
          </tbody>
        </table>
        <p>
          Regime <strong>{margin.config.regime}</strong> (vigente desde {day(margin.config.regimeValidFrom)}, {margin.config.regimeFile}) ·
          fonte: {margin.config.regimeSource}
        </p>
        {margin.exchange && (
          <p>
            PTAX de venda {margin.exchange.ptax.toLocaleString('pt-BR', { minimumFractionDigits: 4 })} de {day(margin.exchange.quoteDate)} ·
            câmbio efetivo {margin.exchange.effectiveRate.toLocaleString('pt-BR', { maximumFractionDigits: 4 })} · parâmetros de {day(margin.config.parametersValidFrom)} ({margin.config.parametersFile})
          </p>
        )}
        <p>Avaliada em {dateTime(margin.assessedAt)}.</p>
      </details>
    </section>
  )
}
