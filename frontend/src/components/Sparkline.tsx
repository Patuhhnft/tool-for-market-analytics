import { useState, type PointerEvent } from 'react'
import { count, day } from '../lib/format'
import type { DailyCount } from '../lib/types'
import '../styles/sparkline.css'

type Props = {
  series: DailyCount[]
  /** Nome da série para leitor de tela; a série é uma só, então não há legenda. */
  label: string
}

const width = 300
const height = 72
const padX = 6
const padTop = 8
const padBottom = 6

/**
 * Visitas diárias do produto — a série exata que entrou na conta da demanda (sem o dia
 * corrente, que vem pela metade). A divisa tracejada separa as duas semanas que o delta
 * compara.
 */
export function Sparkline({ series, label }: Props) {
  const [hovered, setHovered] = useState<number | null>(null)

  if (series.length < 2) {
    return <p className="sparkline-empty">Série curta demais para desenhar.</p>
  }

  const last = series.length - 1
  const max = Math.max(1, ...series.map((point) => point.visits))
  const x = (index: number) => padX + (index * (width - 2 * padX)) / last
  const y = (visits: number) => padTop + (1 - visits / max) * (height - padTop - padBottom)
  const baseline = height - padBottom

  const line = series.map((point, index) => `${index ? 'L' : 'M'}${x(index).toFixed(1)},${y(point.visits).toFixed(1)}`).join(' ')
  const area = `${line} L${x(last).toFixed(1)},${baseline} L${x(0).toFixed(1)},${baseline} Z`
  const weekDivider = series.length >= 14 ? (x(series.length - 7) + x(series.length - 8)) / 2 : null

  const total = series.reduce((sum, point) => sum + point.visits, 0)
  const summary = `${label}: ${count(total)} visitas em ${series.length} dias, de ${day(series[0].date)} a ${day(series[last].date)}.`

  const onPointer = (event: PointerEvent<SVGSVGElement>) => {
    const box = event.currentTarget.getBoundingClientRect()
    const position = ((event.clientX - box.left) / box.width) * width
    const index = Math.round(((position - padX) / (width - 2 * padX)) * last)
    setHovered(Math.min(last, Math.max(0, index)))
  }

  const focus = hovered ?? last
  const focusPoint = series[focus]

  return (
    <figure className="sparkline">
      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label={summary}
        onPointerMove={onPointer}
        onPointerLeave={() => setHovered(null)}
      >
        <line className="sparkline-baseline" x1={padX} x2={width - padX} y1={baseline} y2={baseline} />
        {weekDivider !== null && <line className="sparkline-divider" x1={weekDivider} x2={weekDivider} y1={padTop - 4} y2={baseline} />}
        <path className="sparkline-area" d={area} />
        <path className="sparkline-line" d={line} />
        {hovered !== null && <line className="sparkline-crosshair" x1={x(hovered)} x2={x(hovered)} y1={padTop - 4} y2={baseline} />}
        <circle className="sparkline-marker" cx={x(focus)} cy={y(focusPoint.visits)} r={4} />
        {/* Área de captura maior que a linha: o hover funciona em qualquer ponto do gráfico. */}
        <rect className="sparkline-hit" x={0} y={0} width={width} height={height} />
      </svg>

      {/* Fixo acima do gráfico: segue o hover no conteúdo, nunca na posição — não vaza do card. */}
      <figcaption className="sparkline-caption" aria-hidden="true">
        <strong>{count(focusPoint.visits)}</strong> {focusPoint.visits === 1 ? 'visita' : 'visitas'} · {day(focusPoint.date)}
      </figcaption>

      {/* A mesma série em tabela, para quem navega por leitor de tela.
          O sr-only vai no <div>, não na <table>: por especificação a largura usada de uma
          tabela nunca fica abaixo do conteúdo mínimo dela, então width: 1px é ignorado e a
          tabela estica o documento inteiro. O <div> aceita 1px e recorta; a tabela continua
          sendo tabela na árvore de acessibilidade. */}
      <div className="sr-only">
        <table>
          <caption>{label}</caption>
          <thead>
            <tr><th scope="col">Dia</th><th scope="col">Visitas</th></tr>
          </thead>
          <tbody>
            {series.map((point) => (
              <tr key={point.date}><td>{day(point.date)}</td><td>{point.visits}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
    </figure>
  )
}
