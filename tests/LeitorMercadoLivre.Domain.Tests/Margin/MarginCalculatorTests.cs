using LeitorMercadoLivre.Domain.Margin;
using LeitorMercadoLivre.Domain.Pricing;

namespace LeitorMercadoLivre.Domain.Tests.Margin;

/// <summary>
/// Tabela usada em quase todos os casos (ilustrativa, contas limpas):
/// <list type="bullet">
/// <item>abaixo de R$ 79: comissão 16% + tarifa fixa R$ 6,75; comprador paga o frete</item>
/// <item>a partir de R$ 79: comissão 16%, sem tarifa fixa; vendedor absorve R$ 20 de frete</item>
/// <item>imposto do regime: 6% sobre a venda</item>
/// </list>
/// Então custo_venda(P) = 0,22·P + 6,75 abaixo de 79, e 0,22·P + 20 a partir de 79.
/// </summary>
[TestFixture]
public class MarginCalculatorTests
{
    private static readonly SaleCostSchedule Tabela = new(
        [new SaleFeeBand(79m, 0.16m, 6.75m), new SaleFeeBand(decimal.MaxValue, 0.16m, 0m)],
        freeShippingThreshold: 79m,
        absorbedFreightPerUnit: 20m,
        regimeTaxRate: 0.06m);

    private static readonly MarginOptions Opcoes = new() { TargetMargin = 0.30m };

    /// <summary>Faixa que atravessa o limiar: piso abaixo de 79, mercado e teto acima.</summary>
    private static readonly MarketPricePoints Faixa = new(Floor: 76m, Market: 85m, Ceiling: 100m);

    private static MarginResult Calcular(decimal custo, MarketPricePoints? faixa = null, SaleCostSchedule? tabela = null) =>
        MarginCalculator.Calculate(custo, faixa ?? Faixa, tabela ?? Tabela, Opcoes);

    private static decimal R2(decimal value) => Math.Round(value, 2);

    private static decimal R4(decimal value) => Math.Round(value, 4);

    // ----------------------------------------------------------------------------------
    // Três pontos, cada um com seu próprio custo de venda
    // ----------------------------------------------------------------------------------

    [Test]
    public void TresPontosConferidosAMao()
    {
        // piso    76: venda = 0,22·76 + 6,75 = 23,47 → lucro 22,53 → 29,64%
        // mercado 85: venda = 0,22·85 + 20   = 38,70 → lucro 16,30 → 19,18%
        // teto   100: venda = 0,22·100 + 20  = 42,00 → lucro 28,00 → 28,00%
        var resultado = Calcular(custo: 30m);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Floor.SaleCost.Total, Is.EqualTo(23.47m));
            Assert.That(resultado.Floor.Profit, Is.EqualTo(22.53m));
            Assert.That(R4(resultado.Floor.Margin), Is.EqualTo(0.2964m));

            Assert.That(resultado.Market.SaleCost.Total, Is.EqualTo(38.70m));
            Assert.That(resultado.Market.Profit, Is.EqualTo(16.30m));
            Assert.That(R4(resultado.Market.Margin), Is.EqualTo(0.1918m));

            Assert.That(resultado.Ceiling.SaleCost.Total, Is.EqualTo(42.00m));
            Assert.That(resultado.Ceiling.Margin, Is.EqualTo(0.28m));
        });
    }

    [Test]
    public void PisoAbaixoDoLimiarUsaAEstruturaDeCustoDeBaixo()
    {
        // O teste que pega o reaproveitamento indevido. Se o piso herdasse o custo de venda
        // do mercado (38,70), o lucro no piso sairia 7,30 e a margem 9,6% — errada.
        var resultado = Calcular(custo: 30m);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Floor.SaleCost.FixedFee, Is.EqualTo(6.75m));
            Assert.That(resultado.Floor.SaleCost.AbsorbedFreight, Is.Zero);
            Assert.That(resultado.Market.SaleCost.FixedFee, Is.Zero);
            Assert.That(resultado.Market.SaleCost.AbsorbedFreight, Is.EqualTo(20m));
            Assert.That(resultado.Floor.Profit, Is.Not.EqualTo(7.30m));
        });
    }

    [Test]
    public void PiorMargemDaFaixaFicaNoLimiarNaoNoPiso()
    {
        // O piso tem margem MAIOR que o mercado (29,6% contra 19,2%), porque abaixo de 79 o
        // vendedor não paga frete. E a pior margem não está em nenhum dos três pontos: está
        // exatamente em R$ 79,00 — venda = 0,22·79 + 20 = 37,38 → lucro 11,62 → 14,71%.
        var resultado = Calcular(custo: 30m);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.WorstInRange.Price, Is.EqualTo(79m));
            Assert.That(R4(resultado.WorstInRange.Margin), Is.EqualTo(0.1471m));
            Assert.That(resultado.WorstInRange.Margin, Is.LessThan(resultado.Floor.Margin));
            Assert.That(resultado.WorstInRange.Margin, Is.LessThan(resultado.Market.Margin));
            Assert.That(resultado.WorstBelowTarget, Is.True);
        });
    }

    [Test]
    public void FaixaInteiraDeUmLadoDoLimiarTemPiorCasoNoPiso()
    {
        // Sem ponto de quebra dentro da faixa, a margem só cresce com o preço.
        var resultado = Calcular(custo: 30m, faixa: new MarketPricePoints(90m, 100m, 120m));

        Assert.That(resultado.WorstInRange.Price, Is.EqualTo(90m));
    }

    [Test]
    public void RoiNoPrecoDeMercado()
    {
        // 16,30 ÷ 30 = 54,33%
        Assert.That(R4(Calcular(custo: 30m).RoiAtMarket), Is.EqualTo(0.5433m));
    }

    [Test]
    public void MargemNegativaApareceComoNegativa()
    {
        // 85 − 100 − 38,70 = −53,70
        var resultado = Calcular(custo: 100m);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Market.Profit, Is.EqualTo(-53.70m));
            Assert.That(resultado.Market.Margin, Is.Negative);
            Assert.That(resultado.WorstBelowTarget, Is.True);
        });
    }

    // ----------------------------------------------------------------------------------
    // Ponto de equilíbrio e zonas de prejuízo
    // ----------------------------------------------------------------------------------

    [Test]
    public void PontoDeEquilibrio()
    {
        // abaixo de 79: P · 0,78 = 30 + 6,75 → P = 47,12
        var resultado = Calcular(custo: 30m);

        Assert.Multiple(() =>
        {
            Assert.That(R2(resultado.BreakEvenPrice!.Value), Is.EqualTo(47.12m));
            Assert.That(resultado.LossZones, Is.Empty);
        });
    }

    [Test]
    public void VenderUmPoucoMaisCaroDaPrejuizo()
    {
        // Custo 45. Abaixo de 79 o equilíbrio é 51,75 ÷ 0,78 = 66,35 — de 66,35 a 79 dá lucro.
        // Em 79 o frete entra e o equilíbrio salta para 65 ÷ 0,78 = 83,33.
        // Entre R$ 79,00 e R$ 83,33 cada venda dá PREJUÍZO, mesmo sendo mais cara que 75.
        var resultado = Calcular(custo: 45m);

        var lucroA75 = 75m - 45m - Tabela.At(75m).Total;
        var lucroA80 = 80m - 45m - Tabela.At(80m).Total;

        Assert.Multiple(() =>
        {
            Assert.That(R2(resultado.BreakEvenPrice!.Value), Is.EqualTo(66.35m));
            Assert.That(resultado.LossZones, Has.Count.EqualTo(1));
            Assert.That(resultado.LossZones[0].From, Is.EqualTo(79m));
            Assert.That(R2(resultado.LossZones[0].To), Is.EqualTo(83.33m));

            Assert.That(lucroA75, Is.Positive, "sanidade: R$ 75 dá lucro");
            Assert.That(lucroA80, Is.Negative, "sanidade: R$ 80 dá prejuízo");
        });
    }

    [Test]
    public void QuedaDeTarifaNoPontoDeQuebraFazOLucroNascerPositivo()
    {
        // Tabela invertida: tarifa fixa alta abaixo de 79, nada acima, sem frete absorvido.
        // Abaixo: equilíbrio seria (50 + 30) ÷ 0,84 = 95,24 — fora do trecho.
        // Acima: (50 + 0) ÷ 0,84 = 59,52 — antes do começo do trecho. O lucro salta de
        // negativo para positivo em 79, e 79 é o menor preço lucrativo.
        var tabela = new SaleCostSchedule(
            [new SaleFeeBand(79m, 0.16m, 30m), new SaleFeeBand(decimal.MaxValue, 0.16m, 0m)],
            freeShippingThreshold: 79m, absorbedFreightPerUnit: 0m, regimeTaxRate: 0m);

        var resultado = Calcular(custo: 50m, tabela: tabela);

        Assert.That(resultado.BreakEvenPrice, Is.EqualTo(79m));
    }

    [Test]
    public void SemPrecoLucrativoNaoInventaEquilibrio()
    {
        // Taxas somando 100%: nenhum preço paga a conta.
        var confiscatoria = new SaleCostSchedule(
            [new SaleFeeBand(decimal.MaxValue, 0.50m, 0m)],
            freeShippingThreshold: 79m, absorbedFreightPerUnit: 0m, regimeTaxRate: 0.50m);

        var resultado = Calcular(custo: 10m, tabela: confiscatoria);

        Assert.That(resultado.BreakEvenPrice, Is.Null);
    }

    // ----------------------------------------------------------------------------------
    // Teto de compra
    // ----------------------------------------------------------------------------------

    [Test]
    public void TetoDeCompraNoMercado_IdaEVolta()
    {
        // 85 × 0,70 − 38,70 = 20,80. Comprando a 20,80, a margem no mercado é exatamente 30%.
        var teto = Calcular(custo: 30m).MaxUnitCostAtMarket;
        var comprandoNoTeto = Calcular(custo: teto);

        Assert.Multiple(() =>
        {
            Assert.That(teto, Is.EqualTo(20.80m));
            Assert.That(comprandoNoTeto.Market.Margin, Is.EqualTo(0.30m));
        });
    }

    [Test]
    public void TetoDeCompraSeguroCobreAFaixaInteira_IdaEVolta()
    {
        // O teto "no mercado" (20,80) não protege quem acabar vendendo em R$ 79. O seguro é
        // o menor admissível da faixa: em 79, 79 × 0,70 − 37,38 = 17,92. Comprando a 17,92,
        // a PIOR margem da faixa inteira é exatamente 30%.
        var teto = Calcular(custo: 30m).MaxUnitCostInRange;
        var comprandoNoTeto = Calcular(custo: teto);

        Assert.Multiple(() =>
        {
            Assert.That(teto, Is.EqualTo(17.92m));
            Assert.That(teto, Is.LessThan(Calcular(custo: 30m).MaxUnitCostAtMarket));
            Assert.That(comprandoNoTeto.WorstInRange.Margin, Is.EqualTo(0.30m));
            Assert.That(comprandoNoTeto.WorstBelowTarget, Is.False);
        });
    }

    [Test]
    public void TetoSeguroComAlvoAltoOlhaOLimiteAntesDoPontoDeQuebra()
    {
        // Caso de borda da matemática, com alvo irreal de propósito. Tabela com tarifa fixa
        // de R$ 30 abaixo de 79 e nenhuma acima; comissão 16%, sem regime, sem frete.
        // Com alvo de 90%, a reta admissível P × (1 − 0,90 − 0,16) − fixo DECRESCE:
        //   trecho [76, 79): 76 → −34,56 ... chegando em 79 pela esquerda → −34,74
        //   trecho [79, 85]: 79 → −4,74 ... 85 → −5,10
        // O mínimo é −34,74, no limite ANTES de 79, com a reta do trecho de baixo. Avaliar só
        // os pontos (76, 79, 80, 85) com a tabela daria −34,56 — teto frouxo demais.
        var tarifaQueCai = new SaleCostSchedule(
            [new SaleFeeBand(79m, 0.16m, 30m), new SaleFeeBand(decimal.MaxValue, 0.16m, 0m)],
            freeShippingThreshold: 79m, absorbedFreightPerUnit: 0m, regimeTaxRate: 0m);
        var exigente = new MarginOptions { TargetMargin = 0.90m };

        var resultado = MarginCalculator.Calculate(
            30m, new MarketPricePoints(76m, 80m, 85m), tarifaQueCai, exigente);

        Assert.That(R2(resultado.MaxUnitCostInRange), Is.EqualTo(-34.74m));
    }

    [Test]
    public void TetoDeCompraNaoPrecisaDeFornecedor()
    {
        // A razão de o teto existir separado da margem: aqui não há custo unitário nenhum, e
        // mesmo assim o número sai — e sai igual ao que o caminho com fornecedor produz.
        var teto = MarginCalculator.Ceiling(Faixa, Tabela, Opcoes);
        var comFornecedor = Calcular(custo: 30m);

        Assert.Multiple(() =>
        {
            Assert.That(teto.AtMarket, Is.EqualTo(comFornecedor.MaxUnitCostAtMarket));
            Assert.That(teto.InRange, Is.EqualTo(comFornecedor.MaxUnitCostInRange));
            Assert.That(teto.AtMarket, Is.EqualTo(20.80m));
            Assert.That(teto.InRange, Is.EqualTo(17.92m));
            Assert.That(teto.TargetMargin, Is.EqualTo(0.30m));
            Assert.That(teto.Viable, Is.True);
        });
    }

    [Test]
    public void TetoDizOndeARestricaoAperta()
    {
        // NÃO é o piso. A faixa começa em 76, mas quem manda no teto é o limiar de frete
        // grátis: em R$ 79 o vendedor passa a absorver R$ 20, e esse é o pior ponto da faixa.
        // Sem dizer onde aperta, o número parece arbitrário para quem olha o card.
        var teto = MarginCalculator.Ceiling(Faixa, Tabela, Opcoes);

        Assert.That(teto.BindingPrice, Is.EqualTo(79m));
        Assert.That(teto.BindingPrice, Is.Not.EqualTo(Faixa.Floor));
    }

    [Test]
    public void TetoComprandoUmCentavoAcimaJaFuraOAlvo()
    {
        // O que faz o número valer alguma coisa: logo acima dele, a promessa quebra.
        var teto = MarginCalculator.Ceiling(Faixa, Tabela, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(Calcular(custo: teto.InRange).WorstBelowTarget, Is.False);
            Assert.That(Calcular(custo: teto.InRange + 0.01m).WorstBelowTarget, Is.True);
        });
    }

    [Test]
    public void TetoNegativoNaoEViavel()
    {
        // Tarifas e alvo somando mais do que o preço entrega: nenhum preço de compra fecha.
        // "Não dá" é uma resposta; um teto negativo exibido como dinheiro não é.
        var tarifaQueCai = new SaleCostSchedule(
            [new SaleFeeBand(79m, 0.16m, 30m), new SaleFeeBand(decimal.MaxValue, 0.16m, 0m)],
            freeShippingThreshold: 79m, absorbedFreightPerUnit: 0m, regimeTaxRate: 0m);

        var teto = MarginCalculator.Ceiling(
            new MarketPricePoints(76m, 80m, 85m), tarifaQueCai, new MarginOptions { TargetMargin = 0.90m });

        Assert.Multiple(() =>
        {
            Assert.That(R2(teto.InRange), Is.EqualTo(-34.74m));
            Assert.That(teto.Viable, Is.False);
        });
    }

    // ----------------------------------------------------------------------------------
    // Ligação com o preço de mercado e validação
    // ----------------------------------------------------------------------------------

    [Test]
    public void SemPrecoDeMercadoNaoHaMargem()
    {
        var semDados = PriceReferenceCalculator.Calculate("MLB1", [], new PriceReferenceOptions());

        Assert.That(MarketPricePoints.From(semDados), Is.Null);
    }

    [Test]
    public void PontosSaemDoPrecoDeMercado()
    {
        var anuncios = Enumerable.Range(0, 10)
            .Select(i => new SellerListing($"S{i}", $"L{i}", 80m + (i * 5m), "MLB1"))
            .ToList();

        var referencia = PriceReferenceCalculator.Calculate("MLB1", anuncios, new PriceReferenceOptions());
        var pontos = MarketPricePoints.From(referencia);

        Assert.Multiple(() =>
        {
            Assert.That(pontos, Is.Not.Null);
            Assert.That(pontos!.Floor, Is.EqualTo(referencia.CleanRangeLow));
            Assert.That(pontos.Market, Is.EqualTo(referencia.MarketPrice));
            Assert.That(pontos.Ceiling, Is.EqualTo(referencia.CleanRangeHigh));
        });
    }

    [Test]
    public void PrecosForaDeOrdemSaoRecusados()
    {
        Assert.Throws<ArgumentException>(() => Calcular(30m, new MarketPricePoints(100m, 85m, 76m)));
    }

    [Test]
    public void TabelaSemFaixaAbertaEhRecusada()
    {
        Assert.Throws<ArgumentException>(() => new SaleCostSchedule(
            [new SaleFeeBand(79m, 0.16m, 6.75m)], 79m, 20m, 0.06m));
    }
}
