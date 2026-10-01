import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ProductCardView } from './ProductCard'
import { cardBasePequena, cardComMargem, cardEstreia, cardSemFornecedor } from './cardFixture'

// O teto de compra existe para responder "quanto posso pagar?" antes de haver fornecedor.
// Se ele sumir do card justamente nesse caso, ele não serve para nada — é o que se testa aqui.

afterEach(cleanup)

const nada = () => Promise.resolve()

describe('teto de compra no card', () => {
  it('aparece sem fornecedor nenhum', () => {
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(cardSemFornecedor.suppliers).toHaveLength(0)
    expect(cardSemFornecedor.margin).toBeNull()

    expect(screen.getByRole('heading', { name: 'Quanto posso pagar' })).toBeTruthy()
    // R$ 8,925 → o card arredonda para exibir; o valor cheio fica na API (I3).
    expect(screen.getByText(/R\$\s*8,9/)).toBeTruthy()
  })

  it('diz onde a restrição aperta', () => {
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    // Sem isso o número parece arbitrário: o teto é R$ 8,92 por causa do piso em R$ 17.
    expect(screen.getByText(/Aperta em/)).toBeTruthy()
  })

  it('não se repete quando a margem completa já mostra o teto', () => {
    // Com fornecedor, a tabela da margem já traz "Teto de compra seguro". Dois blocos
    // dizendo a mesma coisa seria ruído.
    render(<ProductCardView card={cardComMargem} onChanged={nada} />)

    expect(screen.queryByRole('heading', { name: 'Quanto posso pagar' })).toBeNull()
    expect(screen.getByText('Teto de compra seguro')).toBeTruthy()
  })

  it('teto inviável vira aviso, não um valor negativo em reais', () => {
    const inviavel = {
      ...cardSemFornecedor,
      ceiling: {
        ...cardSemFornecedor.ceiling!,
        ceiling: { ...cardSemFornecedor.ceiling!.ceiling, inRange: -34.74, viable: false }
      }
    }

    render(<ProductCardView card={inviavel} onChanged={nada} />)

    expect(screen.getByText(/N[ãa]o é "compre barato"/)).toBeTruthy()
    expect(screen.queryByText(/-R\$|R\$\s*-/)).toBeNull()
  })

  it('a palavra "vendeu" não aparece em lugar nenhum do card', () => {
    // §3: o que se mede é visita e posição. Vale para o bloco novo como para todo o resto.
    const { container } = render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(container.textContent?.toLowerCase()).not.toContain('vendeu')
  })
})

describe('crescimento de visitas no card', () => {
  it('estreia mostra o volume e o selo, nunca um percentual', () => {
    render(<ProductCardView card={cardEstreia} onChanged={nada} />)

    // O `rate` cru da fixture vale 19.989 — se alguém voltar a usá-lo, "1.998.900" reaparece.
    expect(screen.getByText('estreia')).toBeTruthy()
    expect(screen.getByText(/\(\+19\.989\)/)).toBeTruthy()
    expect(document.body.textContent).not.toContain('1.998.900')
  })

  it('base pequena mostra o absoluto e diz por que não há percentual', () => {
    render(<ProductCardView card={cardBasePequena} onChanged={nada} />)

    expect(screen.getByText(/base pequena para percentual/)).toBeTruthy()
    expect(document.body.textContent).not.toContain('900%')
    expect(screen.queryByText('estreia')).toBeNull()
  })

  it('base normal continua mostrando o percentual, e as duas superfícies concordam', () => {
    // A correção não pode ter apagado o caso comum, que é a maioria dos cards. E a frase e o
    // bloco de visitas têm de dizer a MESMA coisa — a incoerência entre os dois foi o bug.
    const { container } = render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    // signedPercent arredonda para inteiro na tela; a precisão cheia fica na API (I3).
    expect(container.querySelector('.why-now')?.textContent).toContain('(−94%)')
    expect(container.querySelector('.delta-down')?.textContent).toContain('−94%')
    expect(screen.queryByText('estreia')).toBeNull()
  })

  it('análise antiga, sem o campo growth, não quebra nem inventa percentual', () => {
    const antiga = { ...cardSemFornecedor, demand: { ...cardSemFornecedor.demand, growth: null } }

    render(<ProductCardView card={antiga} onChanged={nada} />)

    expect(screen.getByText(/\(−1\.791\)/)).toBeTruthy()
    expect(document.body.textContent).not.toContain('%,')
  })
})

describe('antes de comprar', () => {
  it('avisa da certificacao exigida pela categoria', () => {
    // Sem o selo, o anúncio pode ser pausado e a carga retida: é risco de perda TOTAL, e
    // nenhum outro número do card enxerga isso.
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(screen.getByText(/Pode exigir Inmetro/)).toBeTruthy()
  })

  it('mostra a margem em kit contra a avulsa', () => {
    // A tarifa fixa do ML é por venda: em produto barato ela come a margem avulsa, e o kit
    // a dilui. O card precisa mostrar os dois lado a lado.
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(screen.getByText(/Vendendo em kit de 5/)).toBeTruthy()
    expect(screen.getByText(/contra 3,0% avulso/)).toBeTruthy()
  })

  it('diz que a margem ja desconta quebra e embalagem', () => {
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(screen.getByText(/2% de quebra\/devolução/)).toBeTruthy()
  })

  it('sem riscos nem kit, o bloco nao aparece', () => {
    const limpo = {
      ...cardSemFornecedor,
      risks: { certifications: [], packagingPerUnit: 0, breakageReserve: 0, kits: [] }
    }

    render(<ProductCardView card={limpo} onChanged={nada} />)

    expect(screen.queryByRole('heading', { name: 'Antes de comprar' })).toBeNull()
  })
})

describe('loja oficial', () => {
  it('marca quando a propria marca ocupa a prateleira', () => {
    const daMarca = {
      ...cardSemFornecedor,
      price: { ...cardSemFornecedor.price, officialStoreSellers: 1, officialStoreShare: 0.53, flags: 'BrandDominated' }
    }

    render(<ProductCardView card={daMarca} onChanged={nada} />)

    expect(screen.getByText(/Vendido pela própria marca/)).toBeTruthy()
    expect(screen.getByText(/53% dos anúncios/)).toBeTruthy()
  })

  it('marca presente sem dominar vira aviso brando, nao alerta', () => {
    const { container } = render(<ProductCardView card={{
      ...cardSemFornecedor,
      price: { ...cardSemFornecedor.price, officialStoreSellers: 1, officialStoreShare: 0.1, officialStorePrice: 80, flags: 'None' }
    }} onChanged={nada} />)

    expect(screen.getByText(/A marca também vende este produto/)).toBeTruthy()
    expect(container.querySelector('.notice-alert')).toBeNull()
  })

  it('sem loja oficial nao diz nada', () => {
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(screen.queryByText(/própria marca/)).toBeNull()
  })
})

describe('da tempo de importar', () => {
  it('produto perdendo velocidade avisa que nao da tempo, por canal', () => {
    // O caso que custa dinheiro: o índice de oportunidade olha o mercado de HOJE, mas a
    // mercadoria chega em 25 dias. Comprar um viral que satura antes da caixa desembarcar
    // é o erro mais caro do importador.
    const { container } = render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(screen.getByRole('heading', { name: 'Dá tempo de importar?' })).toBeTruthy()
    expect(screen.getByText('perdendo velocidade')).toBeTruthy()
    expect(container.querySelectorAll('.channel-list li.is-unfit')).toHaveLength(2)
  })

  it('a decisao usa o PISO do runway, e a tela diz isso', () => {
    // Se usasse o teto, a compra passaria — e esse otimismo é exatamente o que se paga caro.
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(screen.getByText(/a decisão usa o piso: 1/)).toBeTruthy()
  })

  it('sem runway estimado, nao inventa veredito', () => {
    const semRunway = { ...cardSemFornecedor, runway: null }

    render(<ProductCardView card={semRunway} onChanged={nada} />)

    expect(screen.queryByRole('heading', { name: 'Dá tempo de importar?' })).toBeNull()
  })
})

describe('convite a cadastrar fornecedor', () => {
  it('muda de tom quando já existe um teto útil', () => {
    vi.stubGlobal('scrollTo', () => {})
    render(<ProductCardView card={cardSemFornecedor} onChanged={nada} />)

    expect(screen.getByText(/O teto acima j[áa] diz quanto vale a pena pagar/)).toBeTruthy()
    vi.unstubAllGlobals()
  })
})
