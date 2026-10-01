import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { OperatorGate } from './OperatorGate'
import type { OperatorView } from '../lib/types'

// A tela de seleção. O que se testa: quem escolhe fica registrado, nome repetido não duplica,
// e o último a trabalhar vem destacado. Nada aqui é autenticação — escolher não dá permissão.

const operadores: OperatorView[] = [
  { id: 1, name: 'Ana', createdAt: '2026-09-01T10:00:00Z', lastSeenAt: '2026-09-26T08:00:00Z' },
  { id: 2, name: 'Bruno', createdAt: '2026-09-02T10:00:00Z', lastSeenAt: null }
]

let chamadas: { url: string; method: string; body?: string }[] = []

function stubFetch(criado: OperatorView = { id: 3, name: 'Carla', createdAt: '', lastSeenAt: null }) {
  chamadas = []
  vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    chamadas.push({ url, method: init?.method ?? 'GET', body: init?.body as string | undefined })

    if (url.includes('/sessoes')) return Promise.resolve(new Response(null, { status: 204 }))
    if (init?.method === 'POST') {
      return Promise.resolve(new Response(JSON.stringify(criado), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    }
    return Promise.resolve(new Response(JSON.stringify(operadores), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  })
}

function memoria(): Storage {
  const dados = new Map<string, string>()
  return {
    getItem: (k: string) => dados.get(k) ?? null,
    setItem: (k: string, v: string) => void dados.set(k, v),
    removeItem: (k: string) => void dados.delete(k),
    clear: () => dados.clear(),
    key: () => null,
    get length() { return dados.size }
  } as Storage
}

beforeEach(() => {
  vi.stubGlobal('sessionStorage', memoria())
  vi.stubGlobal('localStorage', memoria())
  stubFetch()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const Painel = () => <p>painel</p>

describe('OperatorGate', () => {
  it('sem operador, o painel não aparece', async () => {
    render(<OperatorGate operator={null} onChosen={() => {}}><Painel /></OperatorGate>)

    expect(await screen.findByRole('heading', { name: 'Estou trabalhando com quem?' })).toBeTruthy()
    expect(screen.queryByText('painel')).toBeNull()
  })

  it('com operador, passa direto para o painel', () => {
    render(<OperatorGate operator={{ id: 1, name: 'Ana' }} onChosen={() => {}}><Painel /></OperatorGate>)

    expect(screen.getByText('painel')).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Estou trabalhando com quem?' })).toBeNull()
  })

  it('escolher alguém registra a sessão de trabalho antes de entrar', async () => {
    const escolhido = vi.fn()
    render(<OperatorGate operator={null} onChosen={escolhido}><Painel /></OperatorGate>)

    ;(await screen.findByRole('button', { name: /Ana/ })).click()

    await waitFor(() => expect(escolhido).toHaveBeenCalledWith({ id: 1, name: 'Ana' }))
    // Sem a sessão registrada, a auditoria perderia o "quem estava lá".
    expect(chamadas.some((c) => c.url.includes('/api/operadores/1/sessoes') && c.method === 'POST')).toBe(true)
  })

  it('destaca quem trabalhou por último', async () => {
    localStorage.setItem('leitor.operador.ultimo', '2')
    render(<OperatorGate operator={null} onChosen={() => {}}><Painel /></OperatorGate>)

    const bruno = await screen.findByRole('button', { name: /Bruno/ })
    const ana = await screen.findByRole('button', { name: /^Ana$/ })

    expect(bruno.className).toContain('is-last')
    expect(ana.className).not.toContain('is-last')
  })

  it('"outro nome" cadastra e já entra', async () => {
    const escolhido = vi.fn()
    render(<OperatorGate operator={null} onChosen={escolhido}><Painel /></OperatorGate>)

    ;(await screen.findByRole('button', { name: 'outro nome' })).click()

    const input = await screen.findByLabelText('Seu nome')
    // O React precisa enxergar a mudança do input controlado.
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!
    setter.call(input, 'Carla')
    input.dispatchEvent(new Event('input', { bubbles: true }))

    ;(await screen.findByRole('button', { name: 'Começar' })).click()

    await waitFor(() => expect(escolhido).toHaveBeenCalledWith({ id: 3, name: 'Carla' }))
    const cadastro = chamadas.find((c) => c.method === 'POST' && c.url.endsWith('/api/operadores'))
    expect(cadastro?.body).toContain('Carla')
  })

  it('falha ao carregar não trava a tela: dá para cadastrar assim mesmo', async () => {
    vi.stubGlobal('fetch', () => Promise.reject(new TypeError('rede caiu')))

    render(<OperatorGate operator={null} onChosen={() => {}}><Painel /></OperatorGate>)

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'outro nome' })).toBeTruthy()
  })
})
