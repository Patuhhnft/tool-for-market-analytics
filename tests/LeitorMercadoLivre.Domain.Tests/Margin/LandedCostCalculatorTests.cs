using LeitorMercadoLivre.Domain.Margin;

namespace LeitorMercadoLivre.Domain.Tests.Margin;

/// <summary>
/// As alíquotas daqui são ILUSTRATIVAS, escolhidas para dar contas limpas. O que se testa é
/// a mecânica — base, ordem de incidência, ICMS por dentro, dois câmbios — não a lei vigente,
/// que vem da tabela versionada (I8).
/// </summary>
[TestFixture]
public class LandedCostCalculatorTests
{
    private const decimal Spread = 0.06m;

    private static readonly ImportTaxRates Formal =
        ImportTaxRates.Formal(importDuty: 0.20m, ipi: 0.10m, pis: 0.021m, cofins: 0.0965m, icms: 0.18m);

    /// <summary>100 unidades a US$ 10, frete US$ 200, PTAX 5,00, frete nacional R$ 100.</summary>
    private static AcquisitionInput Lote(ImportTaxRates? taxes = null) =>
        new(SupplierUnitPrice: 10m, Quantity: 100, InternationalFreightTotal: 200m,
            FiscalExchangeRate: 5.00m, DomesticFreightTotal: 100m, Taxes: taxes ?? Formal);

    private static decimal R2(decimal value) => Math.Round(value, 2);

    [Test]
    public void ImportacaoFormal_CadeiaCompletaConferidaAMao()
    {
        // CIF = 10 × 100 + 200 = US$ 1.200
        // pago no exterior = 1.200 × 5,00 × 1,06 = 6.360,00
        // valor aduaneiro  = 1.200 × 5,00       = 6.000,00
        // II     = 6.000 × 0,20             = 1.200,00
        // IPI    = (6.000 + 1.200) × 0,10   =   720,00
        // PIS    = 6.000 × 0,021            =   126,00
        // Cofins = 6.000 × 0,0965           =   579,00
        // antes do ICMS = 8.625,00 → base = 8.625 ÷ 0,82 = 10.518,29 → ICMS = 1.893,29
        // total = 6.360 + 1.200 + 720 + 126 + 579 + 1.893,29 + 100 = 10.978,29 → ÷ 100
        var custo = LandedCostCalculator.Calculate(Lote(), Spread);

        Assert.Multiple(() =>
        {
            Assert.That(custo.PaidAbroad, Is.EqualTo(6360.00m));
            Assert.That(custo.CustomsValue, Is.EqualTo(6000.00m));
            Assert.That(custo.ImportDuty, Is.EqualTo(1200.00m));
            Assert.That(custo.Ipi, Is.EqualTo(720.00m));
            Assert.That(custo.Pis, Is.EqualTo(126.00m));
            Assert.That(custo.Cofins, Is.EqualTo(579.00m));
            Assert.That(R2(custo.IcmsBase), Is.EqualTo(10518.29m));
            Assert.That(R2(custo.Icms), Is.EqualTo(1893.29m));
            Assert.That(R2(custo.ShipmentTotal), Is.EqualTo(10978.29m));
            Assert.That(R2(custo.UnitCost), Is.EqualTo(109.78m));
        });
    }

    [Test]
    public void IcmsEPorDentro_NaoMultiplicacaoDireta()
    {
        // Só ICMS incidindo, sobre R$ 1.000: por dentro dá 219,51. O jeito proibido
        // (1.000 × 0,18) daria 180,00 — R$ 39,51 de imposto sumindo da conta.
        var soIcms = ImportTaxRates.Formal(0m, 0m, 0m, 0m, icms: 0.18m);
        var entrada = new AcquisitionInput(20m, 10, 0m, 5.00m, 0m, soIcms);

        var custo = LandedCostCalculator.Calculate(entrada, exchangeSpread: 0m);

        Assert.Multiple(() =>
        {
            Assert.That(custo.CustomsValue, Is.EqualTo(1000m));
            Assert.That(R2(custo.Icms), Is.EqualTo(219.51m));
            Assert.That(R2(custo.Icms), Is.Not.EqualTo(180.00m));
            Assert.That(Math.Round(custo.IcmsBase - custo.Icms, 8), Is.EqualTo(1000m), "o ICMS está dentro da própria base");
        });
    }

    [Test]
    public void SpreadEncareceOPagamentoMasNaoABaseDosTributos()
    {
        var semSpread = LandedCostCalculator.Calculate(Lote(), exchangeSpread: 0m);
        var comSpread = LandedCostCalculator.Calculate(Lote(), exchangeSpread: 0.06m);

        Assert.Multiple(() =>
        {
            Assert.That(comSpread.TotalTaxes, Is.EqualTo(semSpread.TotalTaxes), "tributo usa o câmbio fiscal");
            Assert.That(comSpread.PaidAbroad - semSpread.PaidAbroad, Is.EqualTo(360.00m), "1.200 × 5,00 × 0,06");
            Assert.That(comSpread.UnitCost - semSpread.UnitCost, Is.EqualTo(3.60m));
        });
    }

    [Test]
    public void RemessaConforme_DeducaoDoIIENenhumIpiPisCofins()
    {
        // CIF = 60 + 10 = US$ 70 → VA = 350,00
        // II = 350 × 0,60 − 20 × 5,00 = 210 − 100 = 110,00
        // antes do ICMS = 460,00 → ICMS (17%) = 460 × 0,17 ÷ 0,83 = 94,22
        // pago = 70 × 5,30 = 371,00 → total = 371 + 110 + 94,22 = 575,22
        var regime = ImportTaxRates.RemessaConforme(importDuty: 0.60m, icms: 0.17m, deductionForeign: 20m);
        var entrada = new AcquisitionInput(60m, 1, 10m, 5.00m, 0m, regime);

        var custo = LandedCostCalculator.Calculate(entrada, Spread);

        Assert.Multiple(() =>
        {
            Assert.That(custo.CustomsValue, Is.EqualTo(350.00m));
            Assert.That(custo.ImportDuty, Is.EqualTo(110.00m));
            Assert.That(custo.Ipi + custo.Pis + custo.Cofins, Is.Zero);
            Assert.That(R2(custo.Icms), Is.EqualTo(94.22m));
            Assert.That(R2(custo.UnitCost), Is.EqualTo(575.22m));
        });
    }

    [Test]
    public void DeducaoMaiorQueOImpostoZeraOIINaoONegativa()
    {
        // VA = 25,00 → 25 × 0,60 − 100 = −85 → II = 0, nunca crédito.
        var regime = ImportTaxRates.RemessaConforme(0.60m, 0.17m, deductionForeign: 20m);

        var custo = LandedCostCalculator.Calculate(new AcquisitionInput(5m, 1, 0m, 5.00m, 0m, regime), Spread);

        Assert.That(custo.ImportDuty, Is.Zero);
    }

    [Test]
    public void DeducaoEPorRemessaNaoPorUnidade()
    {
        // 10 unidades a US$ 6 + frete US$ 10 = mesmo CIF de US$ 70 do teste acima. A dedução
        // entra UMA vez: II = 110,00. Dividir por unidade antes aplicaria a dedução dez vezes.
        var regime = ImportTaxRates.RemessaConforme(0.60m, 0.17m, deductionForeign: 20m);

        var custo = LandedCostCalculator.Calculate(new AcquisitionInput(6m, 10, 10m, 5.00m, 0m, regime), Spread);

        Assert.Multiple(() =>
        {
            Assert.That(custo.ImportDuty, Is.EqualTo(110.00m));
            Assert.That(R2(custo.UnitCost), Is.EqualTo(57.52m), "575,22 ÷ 10");
        });
    }

    // ----------------------------------------------------------------------------------
    // Regra vigente da Remessa Conforme (Receita Federal, desde 12/05/2026):
    // pessoa física, site certificado — até US$ 50: II 0% · de US$ 50,01 a US$ 3.000: II 60%
    // com dedução de US$ 30. A fórmula máx(0; 60% × VA − US$ 30) reproduz as duas faixas,
    // porque 0,6 × 50 − 30 = 0: a dedução foi desenhada para emendar as faixas sem degrau.
    // ----------------------------------------------------------------------------------

    private static readonly ImportTaxRates RemessaConforme2026 = new()
    {
        Regime = "Remessa Conforme — pessoa física",
        ImportDutyBrackets = [new(50m, 0m), new(3000m, 0.60m, 30m)],
        Icms = 0.20m
    };

    /// <summary>A regra anterior (ago/2024 a mai/2026): 20% até US$ 50; 60% − US$ 20 acima.</summary>
    private static readonly ImportTaxRates RemessaConforme2024 = new()
    {
        Regime = "Remessa Conforme — pessoa física",
        ImportDutyBrackets = [new(50m, 0.20m), new(3000m, 0.60m, 20m)],
        Icms = 0.17m
    };

    [TestCase(40, 0)]      // abaixo de US$ 50: zero
    [TestCase(50, 0)]      // exatamente na emenda: 30 − 30 = 0
    [TestCase(100, 150)]   // 60 − 30 = US$ 30 → × 5,00 = R$ 150
    [TestCase(1000, 2850)] // 600 − 30 = US$ 570 → R$ 2.850
    public void RemessaConforme2026_FaixasEmendadasPelaDeducao(decimal valorUsd, decimal iiEsperado)
    {
        var custo = LandedCostCalculator.Calculate(
            new AcquisitionInput(valorUsd, 1, 0m, 5.00m, 0m, RemessaConforme2026), Spread);

        Assert.That(custo.ImportDuty, Is.EqualTo(iiEsperado));
    }

    [Test]
    public void FaixasExpressamRegraQueUmaFormulaUnicaNaoExpressaria()
    {
        // US$ 40 na regra de 2024: 20% × 40 = US$ 8 → R$ 40. A fórmula única
        // máx(0; 60% × 40 − 20) daria US$ 4 — metade do imposto real.
        var custo = LandedCostCalculator.Calculate(new AcquisitionInput(40m, 1, 0m, 5.00m, 0m, RemessaConforme2024), Spread);

        Assert.That(custo.ImportDuty, Is.EqualTo(40m));
    }

    [Test]
    public void AcimaDoTetoDoRegimeOCalculoSaiMarcado()
    {
        // 30 unidades a US$ 100 + US$ 50 de frete = US$ 3.050: passou do teto de US$ 3.000.
        // Acima dele a importação é formal, com alíquota por NCM — o número não vale.
        var dentro = LandedCostCalculator.Calculate(new AcquisitionInput(100m, 29, 50m, 5.00m, 0m, RemessaConforme2026), Spread);
        var fora = LandedCostCalculator.Calculate(new AcquisitionInput(100m, 30, 50m, 5.00m, 0m, RemessaConforme2026), Spread);

        Assert.Multiple(() =>
        {
            Assert.That(dentro.CustomsValueForeign, Is.EqualTo(2950m));
            Assert.That(dentro.ExceedsRegimeLimit, Is.False);
            Assert.That(fora.CustomsValueForeign, Is.EqualTo(3050m));
            Assert.That(fora.ExceedsRegimeLimit, Is.True);
        });
    }

    [Test]
    public void MemoriaDeCalculoFechaComOTotal()
    {
        var custo = LandedCostCalculator.Calculate(Lote(), Spread);
        var linha = (string rotulo) => custo.Memo.Single(item => item.Label == rotulo).Value;

        var somaDasParcelas = linha("Pago no exterior") + linha("II") + linha("IPI") + linha("PIS-Importação")
            + linha("Cofins-Importação") + linha("ICMS") + linha("Despesas aduaneiras") + linha("Frete nacional");

        Assert.Multiple(() =>
        {
            Assert.That(somaDasParcelas, Is.EqualTo(linha("Total da remessa")));
            Assert.That(custo.Regime, Is.EqualTo("Importação formal"));
        });
    }

    [Test]
    public void PrecoMaximoDoFornecedor_IdaEVolta()
    {
        // Comprando exatamente no preço devolvido, o custo desembarcado cabe no alvo — e
        // fica colado nele, não com folga inventada.
        const decimal alvo = 100m;

        var teto = LandedCostCalculator.MaxSupplierUnitPrice(Lote(), alvo, Spread);
        var custoNoTeto = LandedCostCalculator.Calculate(Lote() with { SupplierUnitPrice = teto!.Value }, Spread);

        Assert.Multiple(() =>
        {
            Assert.That(teto, Is.LessThan(10m), "a US$ 10 o custo é 109,78, acima do alvo");
            Assert.That(custoNoTeto.UnitCost, Is.LessThanOrEqualTo(alvo));
            Assert.That(custoNoTeto.UnitCost, Is.GreaterThan(alvo - 0.01m));
        });
    }

    [Test]
    public void PrecoMaximoDoFornecedor_NuloQuandoNemDeGracaFecha()
    {
        // Só frete e tributo sobre o frete já dão ~R$ 19 por unidade.
        Assert.That(LandedCostCalculator.MaxSupplierUnitPrice(Lote(), targetUnitCost: 1m, Spread), Is.Null);
    }

    [Test]
    public void RemessaSemUnidadeEhRecusada()
    {
        Assert.Throws<ArgumentException>(() => LandedCostCalculator.Calculate(Lote() with { Quantity = 0 }, Spread));
    }

    [Test]
    public void AliquotaDeIcmsDeCemPorCentoEhRecusada()
    {
        // Por dentro com 100% dividiria por zero.
        var absurda = ImportTaxRates.Formal(0m, 0m, 0m, 0m, icms: 1m);

        Assert.Throws<ArgumentException>(() => LandedCostCalculator.Calculate(Lote(absurda), Spread));
    }
}
