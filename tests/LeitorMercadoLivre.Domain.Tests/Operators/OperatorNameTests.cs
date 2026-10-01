using LeitorMercadoLivre.Domain.Operators;

namespace LeitorMercadoLivre.Domain.Tests.Operators;

/// <summary>
/// A normalização do nome de operador. O que está em jogo: duas grafias da mesma pessoa não
/// podem virar dois operadores, senão a auditoria se parte em duas e nenhuma conta a história.
/// </summary>
[TestFixture]
public sealed class OperatorNameTests
{
    private static OperatorName Parse(string raw)
    {
        var (name, error) = OperatorName.TryParse(raw);
        Assert.That(name, Is.Not.Null, error);
        return name!;
    }

    [TestCase("  joão  ", "João")]
    [TestCase("MARIA CLARA", "Maria Clara")]
    [TestCase("ana  maria", "Ana Maria")]
    [TestCase("josé da silva", "José da Silva")]
    [TestCase("DOS SANTOS", "Dos Santos")]
    public void ArrumaOQueFoiDigitado(string raw, string exibido)
    {
        // Partícula no meio fica minúscula; abrindo o nome, não.
        Assert.That(Parse(raw).Display, Is.EqualTo(exibido));
    }

    [TestCase("José", "Jose")]
    [TestCase("JOSÉ", "  jose  ")]
    [TestCase("Ana Maria", "ana maria")]
    [TestCase("Conceição", "conceicao")]
    public void MesmaPessoaEscritaDeFormasDiferentesTemAMesmaChave(string um, string outro)
    {
        Assert.That(Parse(um).Key, Is.EqualTo(Parse(outro).Key));
    }

    [Test]
    public void PessoasDiferentesTemChavesDiferentes()
    {
        Assert.That(Parse("Ana").Key, Is.Not.EqualTo(Parse("Ana Maria").Key));
    }

    [Test]
    public void AChaveNaoTemAcentoNemMaiuscula()
    {
        var name = Parse("José Antônio D'Ávila");

        Assert.Multiple(() =>
        {
            Assert.That(name.Key, Is.EqualTo("jose antonio d'avila"));
            Assert.That(name.Display, Is.EqualTo("José Antônio D'ávila"), "a forma de exibir mantém o acento");
        });
    }

    [TestCase("", "Digite um nome.")]
    [TestCase("   ", "Digite um nome.")]
    [TestCase(null, "Digite um nome.")]
    public void NomeVazioNaoIdentificaNinguem(string? raw, string esperado)
    {
        var (name, error) = OperatorName.TryParse(raw);

        Assert.That(name, Is.Null);
        Assert.That(error, Is.EqualTo(esperado));
    }

    [Test]
    public void NomeLongoDemaisERecusado()
    {
        var (name, error) = OperatorName.TryParse(new string('a', OperatorName.MaxLength + 1));

        Assert.That(name, Is.Null);
        Assert.That(error, Does.Contain("60"));
    }

    [TestCase("Ana<script>")]
    [TestCase("Ana; DROP TABLE")]
    [TestCase("Ana\nMaria")]
    [TestCase("Ana|Maria")]
    public void CaractereQueNaoEDeNomeERecusado(string raw)
    {
        // Não é defesa de segurança — é higiene: nome de gente não tem isso dentro, e o
        // valor vai parar em tela e em log de auditoria.
        var (name, error) = OperatorName.TryParse(raw);

        Assert.That(name, Is.Null);
        Assert.That(error, Does.Contain("letras"));
    }

    [Test]
    public void NomeCompostoComHifenEApostrofoPassa()
    {
        Assert.That(Parse("Ana-Lúcia O'Brien").Display, Is.EqualTo("Ana-lúcia O'brien"));
    }
}
