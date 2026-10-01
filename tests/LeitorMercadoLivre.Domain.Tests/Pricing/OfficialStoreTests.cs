using LeitorMercadoLivre.Domain.Pricing;

namespace LeitorMercadoLivre.Domain.Tests.Pricing;

/// <summary>
/// Loja oficial no cálculo do preço.
/// <para>
/// O caso real: "Kit 10 Pote De Vidro Marmita Rishon" foi indicado como oportunidade, mas
/// quem vende é a loja oficial da Rishon. Contra a marca dona do produto o revendedor não
/// compete em preço — ela compra de si mesma. O sistema recomendava uma briga perdida.
/// </para>
/// <para>
/// A correção NÃO exclui o produto nem mexe no preço de mercado: o preço continua sendo o que
/// o mercado pratica. O que muda é a leitura — o card passa a dizer quem está do outro lado.
/// </para>
/// </summary>
[TestFixture]
public sealed class OfficialStoreTests
{
    private const string Produto = "MLB123";

    private static readonly PriceReferenceOptions Opcoes = new();

    private static SellerListing Anuncio(string vendedor, decimal preco, bool oficial = false) =>
        new(vendedor, $"L{vendedor}", preco, Produto, IsOfficialStore: oficial);

    /// <summary>Oito revendedores em torno de R$ 100 — amostra grande o bastante para o corte.</summary>
    private static List<SellerListing> Revendedores() =>
    [
        .. Enumerable.Range(1, 8).Select(i => Anuncio($"R{i}", 98m + i))
    ];

    [Test]
    public void SemLojaOficialNadaMuda()
    {
        var resultado = PriceReferenceCalculator.Calculate(Produto, Revendedores(), Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.OfficialStoreSellers, Is.Zero);
            Assert.That(resultado.OfficialStorePrice, Is.Null);
            Assert.That(resultado.OfficialStoreShare, Is.Zero);
            Assert.That(resultado.HasFlag(PriceFlags.BrandDominated), Is.False);
        });
    }

    [Test]
    public void MarcaOcupandoAPrateleiraLevantaOSelo()
    {
        // O caso Rishon, com os números reais medidos em 28/09/2026: a loja oficial tinha
        // 10 dos 19 anúncios. Um vendedor só, metade da prateleira.
        var lista = Revendedores();
        for (var i = 1; i <= 8; i++)
        {
            lista.Add(new SellerListing("MARCA", $"L-marca{i}", 97m, Produto, IsOfficialStore: true));
        }

        var resultado = PriceReferenceCalculator.Calculate(Produto, lista, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.HasFlag(PriceFlags.BrandDominated), Is.True);
            Assert.That(resultado.OfficialStoreShare, Is.EqualTo(0.5m), "8 de 16 anúncios");
            Assert.That(resultado.OfficialStoreSellers, Is.EqualTo(1), "mas um vendedor só");
        });
    }

    [Test]
    public void MarcaComUmAnuncioSoNaoLevantaOSelo()
    {
        // A marca está presente, mas não ocupa a prateleira — e mesmo barata. Marcar aqui
        // faria o aviso acender em quase todo produto e deixar de significar alguma coisa.
        var comMarca = Revendedores();
        comMarca.Add(Anuncio("MARCA", 80m, oficial: true));

        var resultado = PriceReferenceCalculator.Calculate(Produto, comMarca, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.HasFlag(PriceFlags.BrandDominated), Is.False, "1 de 9 anúncios não é domínio");
            Assert.That(resultado.OfficialStoreSellers, Is.EqualTo(1), "mas o card continua sabendo que ela está lá");
            Assert.That(resultado.OfficialStorePrice, Is.EqualTo(80m), "e por quanto ela vende");
        });
    }

    [Test]
    public void OSeloMedePRESENCA_NaoPreco()
    {
        // A primeira versão disto media preço e acendeu em 28 de 36 produtos reais. Marca
        // cara ocupando a prateleira continua sendo domínio; marca barata com um anúncio, não.
        var marcaCaraDominando = Revendedores();
        for (var i = 1; i <= 8; i++)
        {
            marcaCaraDominando.Add(new SellerListing("MARCA", $"L{i}", 300m, Produto, IsOfficialStore: true));
        }

        var resultado = PriceReferenceCalculator.Calculate(Produto, marcaCaraDominando, Opcoes);

        Assert.That(resultado.HasFlag(PriceFlags.BrandDominated), Is.True,
            "o preço dela não importa para o selo: o que atrapalha é ocupar o que o comprador vê");
    }

    [Test]
    public void OProdutoNaoEExcluido_SoMarcado()
    {
        // "Rebaixar na classificação, sem excluir": o preço continua saindo, o card continua
        // existindo. Sumir com o produto tiraria do usuário a chance de decidir.
        var comMarca = Revendedores();
        for (var i = 1; i <= 8; i++)
        {
            comMarca.Add(new SellerListing("MARCA", $"L-m{i}", 97m, Produto, IsOfficialStore: true));
        }

        var resultado = PriceReferenceCalculator.Calculate(Produto, comMarca, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.HasFlag(PriceFlags.BrandDominated), Is.True);
            Assert.That(resultado.Status, Is.EqualTo(PriceStatus.Calculated));
            Assert.That(resultado.MarketPrice, Is.Not.Null);
            Assert.That(resultado.SellersFound, Is.EqualTo(9), "a marca conta como vendedor presente");
        });
    }

    [Test]
    public void VendedorComAnuncioOficialEOutroNaoContaComoOficial()
    {
        // A marca pode ter um anúncio oficial e outro comum. Continua sendo a marca.
        var lista = Revendedores();
        lista.Add(new SellerListing("MARCA", "L-comum", 70m, Produto, IsOfficialStore: false));
        lista.Add(new SellerListing("MARCA", "L-oficial", 90m, Produto, IsOfficialStore: true));

        var resultado = PriceReferenceCalculator.Calculate(Produto, lista, Opcoes);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.OfficialStoreSellers, Is.EqualTo(1));
            // O anúncio que entra na amostra é o mais barato do vendedor (70), como sempre.
            Assert.That(resultado.OfficialStorePrice, Is.EqualTo(70m));
        });
    }

    [Test]
    public void OPrecoDeMercadoNaoEAlteradoPelaMarca()
    {
        // A correção é de LEITURA, não de cálculo. Se ela mexesse no preço, todo o núcleo
        // estatístico (moda, corte, faixa) mudaria de significado sem aviso.
        var semMarca = PriceReferenceCalculator.Calculate(Produto, Revendedores(), Opcoes);

        var comMarcaCara = Revendedores();
        comMarcaCara.Add(Anuncio("MARCA", 200m, oficial: true));
        var comMarca = PriceReferenceCalculator.Calculate(Produto, comMarcaCara, Opcoes);

        var semMarcaMaisUm = Revendedores();
        semMarcaMaisUm.Add(Anuncio("OUTRO", 200m));
        var comRevendedor = PriceReferenceCalculator.Calculate(Produto, semMarcaMaisUm, Opcoes);

        Assert.That(comMarca.MarketPrice, Is.EqualTo(comRevendedor.MarketPrice),
            "um anúncio de R$ 200 pesa igual, seja da marca ou de um revendedor");
        Assert.That(semMarca.MarketPrice, Is.Not.Null);
    }
}
