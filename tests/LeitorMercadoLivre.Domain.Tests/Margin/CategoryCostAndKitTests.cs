using LeitorMercadoLivre.Domain.Margin;

namespace LeitorMercadoLivre.Domain.Tests.Margin;

/// <summary>
/// Dois custos que a margem ignorava — embalagem e quebra — e a saída para produto barato:
/// vender em kit.
/// </summary>
[TestFixture]
public sealed class CategoryCostAndKitTests
{
    /// <summary>Comissão 16%, tarifa fixa R$ 6,75 abaixo de R$ 79, imposto 6%.</summary>
    private static SaleCostSchedule Tabela(decimal embalagem = 0m) => new(
        [new SaleFeeBand(79m, 0.16m, 6.75m), new SaleFeeBand(decimal.MaxValue, 0.16m, 0m)],
        freeShippingThreshold: 79m,
        absorbedFreightPerUnit: 20m,
        regimeTaxRate: 0.06m,
        packagingPerUnit: embalagem);

    // ----------------------------------------------------------------------------------
    // Reserva de quebra
    // ----------------------------------------------------------------------------------

    [Test]
    public void QuebraDividePeloQueSOBRA_NaoMultiplica()
    {
        // 5% quebram: de 100 compradas, 95 viram venda. Cada venda carrega o custo de
        // 100/95 unidades. Multiplicar por 1,05 subestimaria a perda — o erro é pequeno por
        // unidade e vira dinheiro no lote.
        var vidro = new CategoryCost(PackagingPerUnit: 0m, BreakageReserve: 0.05m);

        var real = vidro.ApplyBreakage(10m);

        Assert.Multiple(() =>
        {
            Assert.That(Math.Round(real, 4), Is.EqualTo(10.5263m), "10 ÷ 0,95");
            Assert.That(real, Is.GreaterThan(10m * 1.05m), "e é MAIOR que multiplicar por 1,05");
        });
    }

    [Test]
    public void SemQuebraOCustoNaoMuda()
    {
        Assert.That(CategoryCost.None.ApplyBreakage(10m), Is.EqualTo(10m));
    }

    [Test]
    public void QuebraDeCemPorCentoERecusada()
    {
        // Não sobra unidade para vender: o custo por venda seria infinito.
        var impossivel = new CategoryCost(0m, 1m);

        Assert.Throws<ArgumentOutOfRangeException>(() => impossivel.ApplyBreakage(10m));
    }

    [Test]
    public void CategoriaSemRegraNaoInventaCusto()
    {
        // Zero aqui significa "não estimamos", não "não existe" — melhor uma margem sem o
        // custo do que uma margem com um custo inventado (I6).
        var tabela = new CategoryCostTable(new Dictionary<string, CategoryCost>
        {
            ["MLB1574"] = new(1.50m, 0.08m)
        });

        Assert.Multiple(() =>
        {
            Assert.That(tabela.For("MLB1574").BreakageReserve, Is.EqualTo(0.08m));
            Assert.That(tabela.For("MLB1051"), Is.EqualTo(CategoryCost.None));
            Assert.That(tabela.For(null), Is.EqualTo(CategoryCost.None));
        });
    }

    // ----------------------------------------------------------------------------------
    // Embalagem
    // ----------------------------------------------------------------------------------

    [Test]
    public void EmbalagemEntraNoCustoDeVenda()
    {
        var custo = Tabela(embalagem: 2m).At(50m);

        Assert.Multiple(() =>
        {
            Assert.That(custo.Packaging, Is.EqualTo(2m));
            // 0,16×50 + 6,75 + 0 (abaixo do limiar) + 0,06×50 + 2 = 8 + 6,75 + 3 + 2
            Assert.That(custo.Total, Is.EqualTo(19.75m));
        });
    }

    [Test]
    public void EmbalagemPesaMuitoMaisEmProdutoBarato()
    {
        // O mesmo R$ 2 de caixa: 10% de um produto de R$ 20, 0,7% de um de R$ 300.
        var comEmbalagem = Tabela(embalagem: 2m);

        var barato = comEmbalagem.At(20m).Packaging / 20m;
        var caro = comEmbalagem.At(300m).Packaging / 300m;

        Assert.That(barato, Is.GreaterThan(caro * 10m));
    }

    // ----------------------------------------------------------------------------------
    // Kit — a saída para o ticket baixo
    // ----------------------------------------------------------------------------------

    [Test]
    public void KitDiluiATarifaFixaEMelhoraAMargem()
    {
        // Produto de R$ 20 com custo de R$ 6. Avulso, a tarifa fixa de R$ 6,75 sozinha come
        // um terço do preço. Em kit de 5 (R$ 100), ela é cobrada uma vez só.
        var opcoes = KitMarginCalculator.Evaluate(
            unitCost: 6m, unitMarketPrice: 20m, Tabela(), [2, 3, 5]);

        Assert.That(opcoes, Has.Count.EqualTo(3));

        var kit5 = opcoes.Single(o => o.Units == 5);

        Assert.Multiple(() =>
        {
            Assert.That(kit5.Price, Is.EqualTo(100m));
            Assert.That(kit5.Gain, Is.GreaterThan(0m), "o kit tem de ser melhor que o avulso");
            Assert.That(kit5.MarginPerUnitSale, Is.LessThan(0.20m), "avulso mal fecha");
        });
    }

    [Test]
    public void KitMaiorNemSempreEMelhor_OLimiarDeFreteCobra()
    {
        // Acima de R$ 79 o vendedor passa a absorver R$ 20 de frete. Um kit que cruza o
        // limiar pode ser PIOR que um menor — e é por isso que o custo de venda é recalculado
        // no preço do kit, nunca reaproveitado do preço unitário.
        var opcoes = KitMarginCalculator.Evaluate(
            unitCost: 6m, unitMarketPrice: 20m, Tabela(), [3, 4]);

        var kit3 = opcoes.Single(o => o.Units == 3);  // R$ 60, abaixo do limiar
        var kit4 = opcoes.Single(o => o.Units == 4);  // R$ 80, acima — absorve frete

        Assert.That(kit4.Margin, Is.LessThan(kit3.Margin),
            "cruzar o limiar de frete grátis piora a margem, apesar do kit maior");
    }

    [Test]
    public void KitDeUmaUnidadeNaoEKit()
    {
        var opcoes = KitMarginCalculator.Evaluate(6m, 20m, Tabela(), [1, 2]);

        Assert.That(opcoes.Select(o => o.Units), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void SemCustoOuSemPrecoNaoHaKit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(KitMarginCalculator.Evaluate(0m, 20m, Tabela(), [2]), Is.Empty);
            Assert.That(KitMarginCalculator.Evaluate(6m, 0m, Tabela(), [2]), Is.Empty);
        });
    }
}
