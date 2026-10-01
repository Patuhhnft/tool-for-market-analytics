namespace LeitorMercadoLivre.Domain.Suppliers;

/// <summary>
/// Link <c>wa.me</c> de contato com fornecedor.
/// <para>
/// O brief fala em <c>wa.me/55...</c>, mas fornecedor de importação quase sempre é chinês
/// (+86). Forçar o 55 geraria um link para um número brasileiro que não existe. Regra:
/// número com código de país explícito (+ ou 00) é respeitado; número nacional de 10 ou 11
/// dígitos recebe o 55.
/// </para>
/// </summary>
public static class WhatsAppLink
{
    public static string? From(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var trimmed = phone.Trim();
        var explicitCountry = trimmed.StartsWith('+') || trimmed.StartsWith("00", StringComparison.Ordinal);
        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        if (trimmed.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];

        if (!explicitCountry)
        {
            // Sem código de país, só aceitamos o formato nacional brasileiro com DDD.
            if (digits.Length is not (10 or 11)) return null;
            digits = "55" + digits;
        }

        // E.164: no máximo 15 dígitos, e um número real tem mais que 7.
        return digits.Length is >= 8 and <= 15 ? $"https://wa.me/{digits}" : null;
    }
}
