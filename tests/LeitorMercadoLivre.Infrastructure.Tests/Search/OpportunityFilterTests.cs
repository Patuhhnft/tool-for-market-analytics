using LeitorMercadoLivre.Infrastructure.Search;

namespace LeitorMercadoLivre.Infrastructure.Tests.Search;

/// <summary>
/// A validação dos filtros, sem banco. O que está em jogo aqui é o contrato da query string:
/// valor inválido vira 400 explicado, e todos os problemas saem na mesma resposta.
/// </summary>
[TestFixture]
public sealed class OpportunityFilterTests
{
    [Test]
    public void QuerySemNadaViraFiltroVazioComPadroes()
    {
        var (filter, errors) = OpportunityFilter.Parse(new OpportunityFilterQuery());

        Assert.That(errors, Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(filter!.CategoryId, Is.Null);
            Assert.That(filter.Competition, Is.Empty);
            Assert.That(filter.Sort, Is.EqualTo(SortOption.OpportunityDesc));
            Assert.That(filter.Page, Is.EqualTo(1));
            Assert.That(filter.PageSize, Is.EqualTo(OpportunityFilter.DefaultPageSize));
        });
    }

    [Test]
    public void ValoresChegamNormalizados()
    {
        var (filter, errors) = OpportunityFilter.Parse(new OpportunityFilterQuery
        {
            Categoria = " mlb1051 ",
            Crescimento = "EXPLODING",
            Concorrencia = ["UpTo5", "upto5", "6to15"],
            Ordenar = " Price_Asc "
        });

        Assert.That(errors, Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(filter!.CategoryId, Is.EqualTo("MLB1051"), "a categoria é sempre maiúscula");
            Assert.That(filter.Growth, Is.EqualTo(GrowthOption.Exploding));
            Assert.That(filter.Competition, Is.EqualTo(new[] { CompetitionBand.UpTo5, CompetitionBand.From6To15 }), "sem repetição");
            Assert.That(filter.Sort, Is.EqualTo(SortOption.PriceAsc));
        });
    }

    [Test]
    public void TodosOsProblemasSaemDeUmaVez()
    {
        var (filter, errors) = OpportunityFilter.Parse(new OpportunityFilterQuery
        {
            Crescimento = "muito",
            Concorrencia = ["zzz"],
            Ordenar = "nada",
            PrecoMin = 500m,
            PrecoMax = 100m,
            OportunidadeMin = 5m,
            Page = 0,
            PageSize = 999
        });

        // Quem monta a URL na mão corrige tudo numa passada, em vez de um erro por requisição.
        Assert.That(filter, Is.Null);
        Assert.That(errors, Has.Count.EqualTo(7));
        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Some.Contains("crescimento="));
            Assert.That(errors, Has.Some.Contains("concorrencia="));
            Assert.That(errors, Has.Some.Contains("ordenar="));
            Assert.That(errors, Has.Some.Contains("precoMin"));
            Assert.That(errors, Has.Some.Contains("oportunidadeMin"));
            Assert.That(errors, Has.Some.Contains("page"));
            Assert.That(errors, Has.Some.Contains("pageSize"));
        });
    }

    [Test]
    public void LimitesDecimaisSaemEmFormatoInvariante()
    {
        // O separador decimal da mensagem não pode depender da cultura da máquina: a mesma
        // classe de bug que já derrubou a API com [Range] lido em pt-BR.
        var (_, errors) = OpportunityFilter.Parse(new OpportunityFilterQuery { PrecoMin = 10.5m, PrecoMax = 1.25m });

        Assert.That(errors.Single(), Does.Contain("10.5").And.Contain("1.25"));
    }

    [TestCase(0)]
    [TestCase(OpportunityFilter.MaxPageSize + 1)]
    public void TamanhoDePaginaForaDoLimiteERecusado(int pageSize)
    {
        var (filter, errors) = OpportunityFilter.Parse(new OpportunityFilterQuery { PageSize = pageSize });

        Assert.That(filter, Is.Null);
        Assert.That(errors, Has.Some.Contains("pageSize"));
    }

    [Test]
    public void FaixaDoIndiceVaiDeZeroAUm()
    {
        var (dentro, semErro) = OpportunityFilter.Parse(new OpportunityFilterQuery { OportunidadeMin = 0m, OportunidadeMax = 1m });
        var (fora, comErro) = OpportunityFilter.Parse(new OpportunityFilterQuery { OportunidadeMax = 1.0001m });

        Assert.Multiple(() =>
        {
            Assert.That(semErro, Is.Empty);
            Assert.That(dentro!.OpportunityMin, Is.EqualTo(0m));
            Assert.That(dentro.OpportunityMax, Is.EqualTo(1m));
            Assert.That(fora, Is.Null);
            Assert.That(comErro, Has.Some.Contains("oportunidadeMax"));
        });
    }

    [Test]
    public void ListaVaziaNaoEFiltro()
    {
        var (filter, errors) = OpportunityFilter.Parse(new OpportunityFilterQuery { Condicao = ["", "   "] });

        Assert.That(errors, Is.Empty);
        Assert.That(filter!.Conditions, Is.Empty, "espaço em branco não vira filtro nem erro");
    }
}
