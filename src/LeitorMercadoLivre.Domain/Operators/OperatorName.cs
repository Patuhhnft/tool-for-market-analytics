using System.Globalization;
using System.Text;

namespace LeitorMercadoLivre.Domain.Operators;

/// <summary>
/// O nome de quem está trabalhando. Puro, sem I/O.
/// <para>
/// Duas formas do mesmo nome: a de <b>exibir</b> (<see cref="Display"/>, arrumada mas fiel ao
/// que a pessoa digitou) e a de <b>comparar</b> (<see cref="Key"/>, sem acento e em minúsculo).
/// "José", "JOSE" e " jose " são a mesma pessoa; o sistema guarda "José" e reconhece os três.
/// </para>
/// </summary>
public sealed record OperatorName(string Display, string Key)
{
    /// <summary>Nome vazio não identifica ninguém; longo demais é engano de digitação ou colagem.</summary>
    public const int MaxLength = 60;

    /// <summary>
    /// Arruma e devolve as duas formas, ou <c>null</c> com o motivo quando não dá para aceitar.
    /// </summary>
    public static (OperatorName? Name, string? Error) TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, "Digite um nome.");

        // Espaços repetidos no meio viram um só: "Ana  Maria" e "Ana Maria" são a mesma pessoa.
        var trimmed = string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (trimmed.Length == 0) return (null, "Digite um nome.");
        if (trimmed.Length > MaxLength) return (null, $"O nome passa de {MaxLength} caracteres.");

        // Só o que compõe nome de gente. Sem isto, um nome com "<" ou ";" atravessaria o
        // sistema até virar texto de tela ou linha de log.
        if (trimmed.Any(character => !IsNameCharacter(character)))
        {
            return (null, "Use apenas letras, espaço, hífen e apóstrofo.");
        }

        return (new OperatorName(Capitalize(trimmed), Normalize(trimmed)), null);
    }

    private static bool IsNameCharacter(char character) =>
        char.IsLetter(character) || character is ' ' or '-' or '\'' or '.';

    /// <summary>
    /// Primeira letra de cada palavra em maiúscula. Partículas ("de", "da", "dos") ficam em
    /// minúsculo, como se escreve em português — exceto quando abrem o nome.
    /// </summary>
    private static string Capitalize(string value)
    {
        string[] particles = ["de", "da", "do", "das", "dos", "e", "di", "du", "van", "von"];
        var words = value.Split(' ');

        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index].ToLower(PtBr);

            words[index] = index > 0 && particles.Contains(word)
                ? word
                : string.Create(word.Length, word, (span, source) =>
                {
                    source.AsSpan().CopyTo(span);
                    span[0] = char.ToUpper(span[0], PtBr);
                });
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// A forma de comparar: sem acento e em minúsculo. É ela que impede "José" e "Jose" de
    /// virarem dois operadores — e o índice único do banco é sobre ela, não sobre o nome exibido.
    /// </summary>
    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            // A decomposição separa "á" em "a" + acento; descartar a marca deixa só a letra.
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
}
