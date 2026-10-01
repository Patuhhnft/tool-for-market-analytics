namespace LeitorMercadoLivre.Domain.Compliance;

/// <summary>
/// Um órgão que pode exigir certificação para o produto entrar no país e ser anunciado.
/// </summary>
public enum CertifyingBody
{
    /// <summary>Brinquedo, puericultura, eletrodoméstico — segurança do produto.</summary>
    Inmetro,

    /// <summary>Cosmético, contato com alimento, saúde.</summary>
    Anvisa,

    /// <summary>Qualquer coisa que transmita rádio: Bluetooth, Wi-Fi, controle remoto.</summary>
    Anatel
}

/// <summary>
/// Uma exigência provável para uma categoria.
/// </summary>
/// <param name="CategoryId">Categoria do Mercado Livre a que se aplica.</param>
/// <param name="Body">Quem exige.</param>
/// <param name="Note">O que exatamente pode ser exigido, em linguagem de quem vai comprar.</param>
public sealed record CertificationRequirement(string CategoryId, CertifyingBody Body, string Note);

/// <summary>
/// O que a categoria do produto sugere sobre certificação.
///
/// <para>
/// <b>Isto é um indício, não um parecer.</b> A exigência real depende do produto específico,
/// não da categoria: dentro de "Brinquedos" há item que precisa de selo do Inmetro e item que
/// não precisa. A tabela serve para o comprador PERGUNTAR ao fornecedor e ao despachante
/// antes de fechar — nunca para afirmar que o produto está regular ou irregular.
/// </para>
/// <para>
/// Por que isso importa em dinheiro: sem a certificação exigida, o anúncio pode ser pausado
/// pelo Mercado Livre e a mercadoria pode ficar retida na alfândega. O estoque vira prejuízo
/// inteiro, não margem menor. É um risco de perda total que nenhum outro número do sistema vê.
/// </para>
/// </summary>
public sealed class CertificationTable(IEnumerable<CertificationRequirement> requirements)
{
    private readonly List<CertificationRequirement> rules = [.. requirements ?? []];

    public IReadOnlyList<CertificationRequirement> Rules => rules;

    /// <summary>
    /// O que a categoria deste produto sugere. Lista vazia quer dizer "a tabela não tem regra
    /// para esta categoria" — o que NÃO é o mesmo que "não precisa de nada".
    /// </summary>
    public IReadOnlyList<CertificationRequirement> For(string? categoryId) =>
        string.IsNullOrWhiteSpace(categoryId)
            ? []
            : [.. rules.Where(rule => string.Equals(rule.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))];
}
