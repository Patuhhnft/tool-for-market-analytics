namespace LeitorMercadoLivre.Infrastructure.Search;

/// <summary>
/// Uma página de resultados com o suficiente para o painel montar a paginação sem adivinhar:
/// o total é o de itens que PASSARAM no filtro, não o do banco inteiro.
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    /// <summary>Zero itens ainda é uma página: total 0 e uma página, nunca divisão por zero.</summary>
    public int TotalPages => Total == 0 ? 1 : (Total + PageSize - 1) / PageSize;

    public bool HasNext => Page < TotalPages;

    public bool HasPrevious => Page > 1;
}
