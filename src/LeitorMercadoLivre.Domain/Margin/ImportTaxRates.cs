namespace LeitorMercadoLivre.Domain.Margin;

/// <summary>
/// Uma faixa do Imposto de Importação: vale para remessas com valor aduaneiro até
/// <paramref name="UpToForeign"/> (inclusive), em moeda estrangeira. <c>null</c> = sem teto.
/// </summary>
/// <param name="Rate">Alíquota sobre o valor aduaneiro, como fração.</param>
/// <param name="DeductionForeign">Dedução fixa por remessa, em moeda estrangeira.</param>
public sealed record ImportDutyBracket(decimal? UpToForeign, decimal Rate, decimal DeductionForeign = 0m);

/// <summary>
/// Alíquotas de um regime de importação.
/// <para>
/// O II é uma tabela de faixas, não uma alíquota única. A regra vigente da Remessa
/// Conforme (até US$ 50: 0% · até US$ 3.000: 60% − US$ 30) até caberia numa fórmula só,
/// mas por coincidência dos números; a de 2024 (20% até US$ 50) já não caberia. Como a
/// configuração pode ser atualizada por um agente, o modelo precisa expressar qualquer
/// desenho que a lei tiver — daí as faixas.
/// </para>
/// <para>
/// Os valores legais vêm da configuração versionada por vigência (I8). Este objeto só
/// carrega o que ela disser para a data do cálculo — nenhuma alíquota mora no código.
/// </para>
/// </summary>
public sealed record ImportTaxRates
{
    public required string Regime { get; init; }

    public required IReadOnlyList<ImportDutyBracket> ImportDutyBrackets { get; init; }

    public decimal Ipi { get; init; }

    public decimal Pis { get; init; }

    public decimal Cofins { get; init; }

    public required decimal Icms { get; init; }

    /// <summary>
    /// Valor aduaneiro máximo da remessa no regime: o teto da última faixa. Acima dele a
    /// importação é outra (a Remessa Conforme vale até US$ 3.000; depois, formal, por NCM).
    /// </summary>
    public decimal? MaxShipmentValueForeign => ImportDutyBrackets[^1].UpToForeign;

    /// <summary>Faixa que se aplica ao valor. Acima do teto, a última — e o cálculo sai marcado.</summary>
    public ImportDutyBracket BracketFor(decimal customsValueForeign) =>
        ImportDutyBrackets.FirstOrDefault(bracket => bracket.UpToForeign is null || customsValueForeign <= bracket.UpToForeign)
        ?? ImportDutyBrackets[^1];

    /// <summary>Atalho para regime de faixa única, com teto opcional.</summary>
    public static ImportTaxRates RemessaConforme(
        decimal importDuty,
        decimal icms,
        decimal deductionForeign = 0m,
        decimal? maxShipmentValueForeign = null) => new()
    {
        Regime = "Remessa Conforme",
        ImportDutyBrackets = [new ImportDutyBracket(maxShipmentValueForeign, importDuty, deductionForeign)],
        Icms = icms
    };

    public static ImportTaxRates Formal(decimal importDuty, decimal ipi, decimal pis, decimal cofins, decimal icms) => new()
    {
        Regime = "Importação formal",
        ImportDutyBrackets = [new ImportDutyBracket(null, importDuty)],
        Ipi = ipi,
        Pis = pis,
        Cofins = cofins,
        Icms = icms
    };

    public void Validate()
    {
        if (ImportDutyBrackets is null || ImportDutyBrackets.Count == 0)
        {
            throw new ArgumentException($"{Regime}: é preciso pelo menos uma faixa de II.", nameof(ImportDutyBrackets));
        }

        decimal? previous = null;
        for (var index = 0; index < ImportDutyBrackets.Count; index++)
        {
            var bracket = ImportDutyBrackets[index];
            RequireRate(bracket.Rate, $"faixa {index + 1} do II");

            if (bracket.DeductionForeign < 0)
            {
                throw new ArgumentException($"{Regime}: a dedução da faixa {index + 1} não pode ser negativa.", nameof(ImportDutyBrackets));
            }

            if (bracket.UpToForeign is null && index < ImportDutyBrackets.Count - 1)
            {
                throw new ArgumentException($"{Regime}: só a última faixa pode ficar sem teto.", nameof(ImportDutyBrackets));
            }

            if (bracket.UpToForeign is { } upTo && (upTo <= 0 || (previous is { } last && upTo <= last)))
            {
                throw new ArgumentException($"{Regime}: os tetos das faixas precisam ser positivos e crescentes.", nameof(ImportDutyBrackets));
            }

            previous = bracket.UpToForeign;
        }

        RequireRate(Ipi, nameof(Ipi));
        RequireRate(Pis, nameof(Pis));
        RequireRate(Cofins, nameof(Cofins));

        // ICMS = 100% faria a base "por dentro" dividir por zero.
        RequireRate(Icms, nameof(Icms));
    }

    private void RequireRate(decimal rate, string name)
    {
        if (rate < 0 || rate >= 1)
        {
            throw new ArgumentException($"{Regime}: {name} deve ser fração em [0, 1) — 0.18 significa 18%, não 18.");
        }
    }
}
