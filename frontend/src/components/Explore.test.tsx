import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { Explore } from './Explore'
import type { FilterMeta } from '../lib/types'

// A tela inteira, com o fetch simulado. O que se testa aqui é a promessa central da área:
// a URL manda na tela, e mudar um filtro muda a URL e a busca — nessa ordem.

const meta: FilterMeta = {
  categories: [{ id: 'MLB1051', label: 'Celulares e Telefones', count: 3 }],
  priceMin: 10,
  priceMax: 900,
  totalProducts: 3,
  growth: [{ value: '10', label: '+10% ou mais' }],
  competition: [
    { value: 'upto5', label: 'Até 5 vendedores' },
    { value: '6to15', label: '6 a 15 vendedores' }
  ],
  newSellers: [{ value: 'none', label: 'Nenhum entrante' }],
  conditions: [{ value: 'new', label: 'Tem anúncio novo' }],
  confidence: [{ value: 'high', label: 'Alta' }],
  sort: [
    { value: 'opportunity_desc', label: 'Maior índice de oportunidade' },
    { value: 'price_asc', label: 'Menor preço' }
  ]
}

/** As URLs que a tela pediu, na ordem. É o que prova que o filtro chegou à API. */
let requested: string[] = []

function stubFetch() {
  requested = []
  vi.stubGlobal('fetch', (input: RequestInfo | URL) => {
    const url = String(input)
    requested.push(url)

    const body = url.includes('filtros-meta')
      ? meta
      : { items: [], total: 0, page: 1, pageSize: 20, totalPages: 1, hasNext: false, hasPrevious: false, appliedFilter: {}, lastCycle: null }

    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  })
}

/** Mostra a URL atual, para o teste ler o que a navegação fez. */
function CurrentUrl() {
  const location = useLocation()
  return <output data-testid="url">{location.pathname + location.search}</output>
}

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/explorar" element={<><Explore /><CurrentUrl /></>} />
      </Routes>
    </MemoryRouter>
  )
}

beforeEach(() => {
  vi.stubGlobal('localStorage', {
    getItem: () => null,
    setItem: () => {},
    removeItem: () => {}
  } as unknown as Storage)
  stubFetch()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('Explore', () => {
  it('abre com o filtro que veio na URL já marcado', async () => {
    const { findByRole } = renderAt('/explorar?concorrencia=upto5')

    const marcado = await findByRole('button', { name: 'Até 5 vendedores' })
    const solto = await findByRole('button', { name: '6 a 15 vendedores' })

    expect(marcado.getAttribute('aria-pressed')).toBe('true')
    expect(solto.getAttribute('aria-pressed')).toBe('false')
  })

  it('o filtro da URL chega à API', async () => {
    renderAt('/explorar?concorrencia=upto5&ordenar=price_asc')

    await waitFor(() => {
      const busca = requested.find((url) => url.includes('/explorar?'))
      expect(busca).toBeDefined()
      expect(busca).toContain('concorrencia=upto5')
      expect(busca).toContain('ordenar=price_asc')
      expect(busca).toContain('pageSize=20')
    })
  })

  it('marcar uma opção muda a URL e refaz a busca', async () => {
    const { findByRole } = renderAt('/explorar')

    const botao = await findByRole('button', { name: '6 a 15 vendedores' })
    botao.click()

    await waitFor(() => {
      expect(screen.getByTestId('url').textContent).toBe('/explorar?concorrencia=6to15')
    })

    await waitFor(() => {
      expect(requested.some((url) => url.includes('concorrencia=6to15'))).toBe(true)
    })
  })

  it('o selo do filtro ativo aparece e some ao ser removido', async () => {
    const { findByRole } = renderAt('/explorar?concorrencia=upto5')

    // Dois botões com o mesmo nome: o da barra lateral e o selo. O selo é o do fim.
    const selos = await screen.findAllByRole('button', { name: /Até 5 vendedores/ })
    const selo = selos[selos.length - 1]
    selo.click()

    await waitFor(() => {
      expect(screen.getByTestId('url').textContent).toBe('/explorar')
    })

    const contador = await findByRole('button', { name: 'Filtros' })
    expect(contador.textContent).toBe('Filtros')
  })

  it('sem resultado explica que os filtros se somam', async () => {
    renderAt('/explorar?concorrencia=upto5')

    expect(await screen.findByText('Nenhum produto com esses filtros')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Limpar os filtros' })).toBeTruthy()
  })
})
