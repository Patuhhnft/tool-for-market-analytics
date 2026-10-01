import { describe, expect, it } from 'vitest'
import {
  countActive,
  emptyFilters,
  parseFilters,
  sameFilters,
  toQuery,
  toSearchParams,
  toggle
} from './exploreFilters'

// A URL é a fonte da verdade da tela de explorar. Se a ida e a volta não baterem, um link
// compartilhado abre mostrando outra coisa — e o F5 muda a lista sozinho.

describe('parseFilters', () => {
  it('sem parâmetros devolve o filtro vazio', () => {
    expect(parseFilters(new URLSearchParams())).toEqual(emptyFilters())
  })

  it('descarta valor que não existe em vez de quebrar', () => {
    const filters = parseFilters(new URLSearchParams('crescimento=muito&concorrencia=zzz&ordenar=nada&page=abc'))

    expect(filters.crescimento).toBeNull()
    expect(filters.concorrencia).toEqual([])
    expect(filters.ordenar).toBe('opportunity_desc')
    expect(filters.page).toBe(1)
  })

  it('normaliza maiúsculas e remove repetição', () => {
    const filters = parseFilters(new URLSearchParams('categoria=mlb1051&concorrencia=UpTo5&concorrencia=upto5&crescimento=EXPLODING'))

    expect(filters.categoria).toBe('MLB1051')
    expect(filters.concorrencia).toEqual(['upto5'])
    expect(filters.crescimento).toBe('exploding')
  })

  it('faixa invertida perde o teto, em vez de virar um 400', () => {
    const filters = parseFilters(new URLSearchParams('precoMin=500&precoMax=100'))

    expect(filters.precoMin).toBe(500)
    expect(filters.precoMax).toBeNull()
  })

  it('índice fora de 0 a 1 é ignorado', () => {
    const filters = parseFilters(new URLSearchParams('oportunidadeMin=-1&oportunidadeMax=5'))

    expect(filters.oportunidadeMin).toBeNull()
    expect(filters.oportunidadeMax).toBeNull()
  })
})

describe('ida e volta pela URL', () => {
  it('o que sai volta igual', () => {
    const filters = parseFilters(
      new URLSearchParams(
        'categoria=MLB1276&precoMin=50&precoMax=900&oportunidadeMin=0.4&crescimento=50' +
          '&concorrencia=upto5&concorrencia=6to15&novosVendedores=none&condicao=new&confianca=high' +
          '&freteGratis=true&temFornecedor=false&ordenar=price_asc&page=3'
      )
    )

    expect(parseFilters(toSearchParams(filters))).toEqual(filters)
  })

  it('o padrão não polui a URL', () => {
    expect(toSearchParams(emptyFilters()).toString()).toBe('')
    expect(toSearchParams({ ...emptyFilters(), ordenar: 'opportunity_desc', page: 1 }).toString()).toBe('')
  })

  it('a query da API sempre leva página e tamanho', () => {
    const query = new URLSearchParams(toQuery(emptyFilters()))

    expect(query.get('page')).toBe('1')
    expect(query.get('pageSize')).toBe('20')
  })
})

describe('countActive', () => {
  it('ordenação e página não contam como filtro', () => {
    expect(countActive({ ...emptyFilters(), ordenar: 'price_asc', page: 4 })).toBe(0)
  })

  it('faixa conta uma vez, com um lado ou com os dois', () => {
    expect(countActive({ ...emptyFilters(), precoMin: 10 })).toBe(1)
    expect(countActive({ ...emptyFilters(), precoMin: 10, precoMax: 20 })).toBe(1)
  })

  it('cada opção marcada conta', () => {
    expect(countActive({ ...emptyFilters(), concorrencia: ['upto5', '6to15'], freteGratis: true })).toBe(3)
  })
})

describe('sameFilters', () => {
  it('a página não distingue dois filtros', () => {
    const base = { ...emptyFilters(), categoria: 'MLB1051' }

    expect(sameFilters(base, { ...base, page: 7 })).toBe(true)
    expect(sameFilters(base, { ...base, categoria: 'MLB1276' })).toBe(false)
  })
})

describe('toggle', () => {
  it('liga e desliga', () => {
    expect(toggle(['a'], 'b')).toEqual(['a', 'b'])
    expect(toggle(['a', 'b'], 'a')).toEqual(['b'])
  })
})
