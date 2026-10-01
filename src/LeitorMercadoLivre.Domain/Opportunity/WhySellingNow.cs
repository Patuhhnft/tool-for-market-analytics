using System.Globalization;
using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Domain.Pricing;

namespace LeitorMercadoLivre.Domain.Opportunity;

/// <summary>
/// "Por que vende agora", fase 1: justificativa montada por regras, sem LLM.
/// <para>
/// Duas regras de redação que não são estilo, são invariantes: todo percentual vem com o
/// número absoluto que o gerou (I11), e a palavra "vendeu" não aparece — o que se mede é
/// visita e posição, não venda (§3).
/// </para>
/// </summary>
public static class WhySellingNow
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="growth">
    /// Como o crescimento deve ser APRESENTADO. Vem de fora, já decidido, em vez de sair de
    /// <c>demand.Rate</c>: aquele campo tem uma proteção contra divisão por zero que devolve
    /// um número mesmo quando não existe razão nenhuma a calcular, e esse número já chegou a
    /// aparecer no card como "+1.998.900%".
    /// </param>
    public static string Explain(
        DemandResult demand, PriceReferenceResult price, int sellers, int? newSellersThisWeek, VisitGrowth growth)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(growth);

        List<string> parts = [DescribeDemand(demand, growth), DescribeCompetition(sellers, newSellersThisWeek)];

        var pricePart = DescribePrice(price);
        if (pricePart is not null) parts.Add(pricePart);

        return string.Join(" · ", parts);
    }

    private static string DescribeDemand(DemandResult demand, VisitGrowth growth) => demand.Source switch
    {
        // Estreia. Não existe percentual a mostrar, e a data em que a curva saiu do zero
        // informa mais do que qualquer razão: diz que ANTES não existia.
        DemandSource.Visits when growth.IsNewDemand && growth.StartedOn is { } inicio =>
            $"estreou em {inicio:dd/MM} e fez {Number(growth.LastWeekVisits ?? 0)} visitas em 7 dias",
        DemandSource.Visits when growth.IsNewDemand =>
            $"demanda nova: {Number(growth.LastWeekVisits ?? 0)} visitas na semana, sem semana anterior para comparar",

        // Base pequena: o percentual existe e é ruído. Sai o absoluto, e o card diz por quê (I11).
        DemandSource.Visits when growth.SmallBase && demand.AbsoluteDelta is { } pequeno and > 0 =>
            $"recebeu {Number(pequeno)} visitas a mais que na semana anterior (base pequena demais para percentual)",
        DemandSource.Visits when growth.SmallBase && demand.AbsoluteDelta is { } pequeno and < 0 =>
            $"recebeu {Number(-pequeno)} visitas a menos que na semana anterior (base pequena demais para percentual)",

        DemandSource.Visits when demand.AbsoluteDelta is { } delta and > 0 =>
            $"recebeu {Number(delta)} visitas a mais que na semana anterior ({SignedPercent(demand.Rate)})",
        DemandSource.Visits when demand.AbsoluteDelta is { } delta and < 0 =>
            $"recebeu {Number(-delta)} visitas a menos que na semana anterior ({SignedPercent(demand.Rate)})",
        DemandSource.Visits =>
            $"visitas estáveis: {Number(demand.LastWeekVisits ?? 0)} na última semana",
        DemandSource.Ranking when demand.PositionGain is { } gain and > 0 =>
            $"subiu {gain} {Plural(gain, "posição", "posições")} no ranking de mais vendidos (agora {demand.CurrentPosition}º)",
        DemandSource.Ranking when demand.PositionGain is { } gain and < 0 =>
            $"caiu {-gain} {Plural(-gain, "posição", "posições")} no ranking de mais vendidos (agora {demand.CurrentPosition}º)",
        DemandSource.Ranking =>
            $"parado na {demand.CurrentPosition}ª posição do ranking de mais vendidos",
        _ => "histórico ainda curto para medir crescimento"
    };

    /// <summary>
    /// Sem ciclo anterior não se sabe quem entrou. Dizer "nenhum novo" seria afirmar o que não
    /// foi medido — o card avisa que a comparação ainda não existe (I6).
    /// </summary>
    private static string DescribeCompetition(int sellers, int? newSellers)
    {
        var quantos = $"{sellers} {Plural(sellers, "vendedor", "vendedores")}";

        return newSellers switch
        {
            null => $"{quantos}, entrantes ainda sem comparação",
            0 => $"{quantos}, nenhum entrante desde a coleta anterior",
            { } entrantes => $"{quantos}, {entrantes} {Plural(entrantes, "entrante", "entrantes")} desde a coleta anterior"
        };
    }

    private static string? DescribePrice(PriceReferenceResult price) => price switch
    {
        { Status: not PriceStatus.Calculated } => null,
        { Source: PriceSource.Mode, ModeStrength: { } strength, MarketPrice: { } market } =>
            $"{Percent(strength)} dos vendedores ({price.ModalBucketSellers}) praticam {Money(market)}",
        { MarketPrice: { } market } => $"preço disperso, mediana em {Money(market)}",
        _ => null
    };

    private static string Number(int value) => value.ToString("N0", PtBr);

    private static string Money(decimal value) =>
        value == decimal.Truncate(value) ? $"R$ {value.ToString("N0", PtBr)}" : $"R$ {value.ToString("N2", PtBr)}";

    private static string Percent(decimal fraction) => $"{Math.Round(fraction * 100m, 0).ToString("N0", PtBr)}%";

    private static string SignedPercent(decimal? fraction) => fraction is { } value
        ? (value >= 0 ? "+" : "−") + Percent(Math.Abs(value))
        : "sem base de comparação";

    private static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;
}
