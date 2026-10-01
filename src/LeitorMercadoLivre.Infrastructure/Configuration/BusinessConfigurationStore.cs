using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LeitorMercadoLivre.Domain.Demand;
using LeitorMercadoLivre.Domain.Compliance;
using LeitorMercadoLivre.Domain.Margin;
using LeitorMercadoLivre.Domain.Opportunity;
using LeitorMercadoLivre.Domain.Pricing;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.Configuration;

public sealed class ConfigurationFolderOptions
{
    public const string SectionName = "Configuracao";

    /// <summary>Pasta dos JSON. Relativa ao diretório de trabalho, ou absoluta.</summary>
    public string Folder { get; set; } = "config";

    /// <summary>Arquivo que a área do administrador edita. Os demais só são lidos.</summary>
    public string AdminFileName { get; set; } = "configuracoes.json";
}

public sealed record Sourced<T>(T Entry, string File);

public sealed record ConfigFileStatus(
    string FileName,
    bool Accepted,
    IReadOnlyList<string> Errors,
    int TaxRules,
    int Parameters,
    DateTimeOffset LastModified);

public sealed record LoadedConfiguration(
    string Folder,
    IReadOnlyList<ConfigFileStatus> Files,
    IReadOnlyList<Sourced<TaxRuleEntry>> TaxRules,
    IReadOnlyList<Sourced<ParametersEntry>> Parameters,
    IReadOnlyList<string> Errors);

/// <summary>Tudo que vale numa data, já convertido para os tipos do domínio.</summary>
public sealed record EffectiveConfiguration(
    DateOnly Date,
    Sourced<ParametersEntry> Parameters,
    Sourced<TaxRuleEntry> TaxRule,
    ImportTaxRates ImportTaxRates,
    PriceReferenceOptions PriceReference,
    DemandOptions Demand,
    OpportunityOptions Opportunity,
    MarginOptions Margin,
    RunwayOptions Runway,
    IReadOnlyList<SupplyChannel> Channels,
    int MinimumSellingWindowDays,
    CategoryCostTable CategoryCosts,
    CertificationTable Certifications,
    IReadOnlyList<int> KitSizes);

/// <summary>Um canal de fornecimento já no formato que o cálculo usa.</summary>
public sealed record SupplyChannel(string Name, int LeadTimeDays, bool Imported, string? Note);

public sealed record ResolveResult(EffectiveConfiguration? Configuration, IReadOnlyList<string> Problems);

public sealed record WriteResult(bool Saved, IReadOnlyList<string> Errors);

/// <summary>
/// A pasta de configuração é a fonte única dos parâmetros de negócio: tributos, venda,
/// categorias, calibração. A área do administrador edita os mesmos arquivos, e um agente
/// pode soltar arquivos novos nela.
/// <para>
/// Como quem escreve pode ser um agente automático, a leitura é defensiva: propriedade
/// desconhecida é erro (pega erro de digitação), campo obrigatório ausente é erro, e um
/// arquivo com qualquer problema é rejeitado INTEIRO — nunca aplicado pela metade. O
/// arquivo ruim aparece na área do administrador; os bons continuam valendo.
/// </para>
/// <para>
/// Nada é sobrescrito: mudança é versão nova com data de vigência (I8). E cada análise grava
/// a versão que usou (I7), então um número antigo continua auditável.
/// </para>
/// </summary>
public sealed class BusinessConfigurationStore(IOptions<ConfigurationFolderOptions> options)
{
    private static readonly JsonSerializerOptions Strict = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Regex SafeFileName = new("^[a-z0-9][a-z0-9._-]{0,80}\\.json$", RegexOptions.Compiled);

    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public string Folder => Path.GetFullPath(options.Value.Folder);

    private string AdminFile => Path.Combine(Folder, options.Value.AdminFileName);

    public LoadedConfiguration Load()
    {
        var files = new List<ConfigFileStatus>();
        var taxRules = new List<Sourced<TaxRuleEntry>>();
        var parameters = new List<Sourced<ParametersEntry>>();
        var errors = new List<string>();

        if (!Directory.Exists(Folder))
        {
            return new LoadedConfiguration(Folder, files, taxRules, parameters, [$"A pasta de configuração não existe: {Folder}"]);
        }

        // O arquivo do administrador vem primeiro: num conflito, ele prevalece.
        var paths = Directory.GetFiles(Folder, "*.json")
            .Where(path => !path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path) == options.Value.AdminFileName ? 0 : 1)
            .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var (document, fileErrors) = Parse(File.ReadAllText(path));

            if (document is null || fileErrors.Count > 0)
            {
                files.Add(new ConfigFileStatus(name, false, fileErrors, 0, 0, File.GetLastWriteTimeUtc(path)));
                continue;
            }

            // Duplicata entre arquivos: fica a primeira (arquivo do administrador, depois ordem alfabética).
            foreach (var rule in document.TaxRules)
            {
                var clash = taxRules.FirstOrDefault(existing => SameVersion(existing.Entry, rule));
                if (clash is not null)
                {
                    errors.Add($"{name}: tributo '{rule.Regime}' vigente desde {rule.ValidFrom:yyyy-MM-dd} já existe em {clash.File} — ignorado.");
                    continue;
                }

                taxRules.Add(new Sourced<TaxRuleEntry>(rule, name));
            }

            foreach (var entry in document.Parameters)
            {
                var clash = parameters.FirstOrDefault(existing => existing.Entry.ValidFrom == entry.ValidFrom);
                if (clash is not null)
                {
                    errors.Add($"{name}: parâmetros vigentes desde {entry.ValidFrom:yyyy-MM-dd} já existem em {clash.File} — ignorados.");
                    continue;
                }

                parameters.Add(new Sourced<ParametersEntry>(entry, name));
            }

            files.Add(new ConfigFileStatus(name, true, [], document.TaxRules.Count, document.Parameters.Count, File.GetLastWriteTimeUtc(path)));
        }

        return new LoadedConfiguration(Folder, files, taxRules, parameters, errors);
    }

    /// <summary>A versão dos parâmetros e dos tributos vigente numa data.</summary>
    public ResolveResult ResolveAt(DateOnly date, LoadedConfiguration? loaded = null)
    {
        loaded ??= Load();
        var problems = new List<string>();

        var parameters = loaded.Parameters
            .Where(item => item.Entry.ValidFrom <= date)
            .MaxBy(item => item.Entry.ValidFrom);

        if (parameters is null)
        {
            problems.Add($"Nenhuma versão de parâmetros vigente em {date:dd/MM/yyyy}.");
            return new ResolveResult(null, problems);
        }

        var regime = parameters.Entry.ImportRegime;
        var taxRule = loaded.TaxRules
            .Where(item => item.Entry.Regime == regime && item.Entry.ValidFrom <= date && (item.Entry.ValidTo is null || item.Entry.ValidTo >= date))
            .MaxBy(item => item.Entry.ValidFrom);

        if (taxRule is null)
        {
            problems.Add($"Os parâmetros usam o regime '{regime}', mas não há tributos desse regime vigentes em {date:dd/MM/yyyy}.");
            return new ResolveResult(null, problems);
        }

        var p = parameters.Entry;
        var effective = new EffectiveConfiguration(
            date,
            parameters,
            taxRule,
            ToImportTaxRates(taxRule.Entry),
            ToPriceReference(p.PriceModel),
            ToDemand(p.Demand),
            ToOpportunity(p.Opportunity, p.TargetMargin),
            new MarginOptions { TargetMargin = p.TargetMargin, ExchangeSpread = p.ExchangeSpread },
            ToRunway(p.Runway),
            [.. p.SupplyChannels.Select(channel => new SupplyChannel(channel.Name, channel.LeadTimeDays, channel.Imported, channel.Note))],
            p.MinimumSellingWindowDays ?? 30,
            ToCategoryCosts(p.CategoryCosts),
            ToCertifications(p.Certifications),
            p.KitSizes.Count > 0 ? p.KitSizes : [2, 3, 5]);

        return new ResolveResult(effective, problems);
    }

    public Task<WriteResult> AppendTaxRuleAsync(TaxRuleEntry entry, CancellationToken cancellationToken) =>
        AppendAsync(document => document.TaxRules.Add(entry), cancellationToken);

    public Task<WriteResult> AppendParametersAsync(ParametersEntry entry, CancellationToken cancellationToken) =>
        AppendAsync(document => document.Parameters.Add(entry), cancellationToken);

    /// <summary>
    /// Grava um arquivo enviado para a pasta — só depois de validado por inteiro. Arquivo
    /// inválido não chega à pasta; o chamador recebe a lista de erros.
    /// </summary>
    public async Task<WriteResult> SaveFileAsync(string fileName, string content, CancellationToken cancellationToken)
    {
        var name = fileName.Trim().ToLowerInvariant();
        if (!SafeFileName.IsMatch(name) || name.EndsWith(".schema.json", StringComparison.Ordinal))
        {
            return new WriteResult(false, ["Nome de arquivo inválido: use letras minúsculas, números, ponto, hífen ou sublinhado, terminando em .json."]);
        }

        var (document, errors) = Parse(content);
        if (document is null || errors.Count > 0) return new WriteResult(false, errors);

        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            var clashes = FindClashes(document, excludeFile: name);
            if (clashes.Count > 0) return new WriteResult(false, clashes);

            await WriteAtomicallyAsync(Path.Combine(Folder, name), content, cancellationToken);
            return new WriteResult(true, []);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    /// <summary>Lê o arquivo, desserializa em modo estrito e valida cada entrada com as regras do domínio.</summary>
    public static (ConfigDocument? Document, List<string> Errors) Parse(string json)
    {
        ConfigDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ConfigDocument>(json, Strict);
        }
        catch (JsonException exception)
        {
            return (null, [$"JSON inválido: {exception.Message}"]);
        }

        if (document is null) return (null, ["Arquivo vazio."]);

        var errors = new List<string>();

        for (var index = 0; index < document.TaxRules.Count; index++)
        {
            ValidateTaxRule(document.TaxRules[index], $"tributos[{index}]", errors);
        }

        for (var index = 0; index < document.Parameters.Count; index++)
        {
            ValidateParameters(document.Parameters[index], $"parametros[{index}]", errors);
        }

        AddInternalDuplicates(document, errors);
        return (document, errors);
    }

    private static void ValidateTaxRule(TaxRuleEntry rule, string where, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(rule.Regime)) errors.Add($"{where}: 'regime' vazio.");
        if (string.IsNullOrWhiteSpace(rule.Source)) errors.Add($"{where}: 'fonte' é obrigatória — de onde saiu cada número?");
        if (rule.ValidFrom.Year < 2000) errors.Add($"{where}: 'vigenteDesde' inválida.");
        if (rule.ValidTo is { } end && end < rule.ValidFrom) errors.Add($"{where}: 'vigenteAte' é anterior a 'vigenteDesde'.");

        Guard(() => ToImportTaxRates(rule).Validate(), where, errors);
    }

    private static void ValidateParameters(ParametersEntry entry, string where, List<string> errors)
    {
        if (entry.ValidFrom.Year < 2000) errors.Add($"{where}: 'vigenteDesde' inválida.");
        if (string.IsNullOrWhiteSpace(entry.ImportRegime)) errors.Add($"{where}: 'regimeImportacao' vazio.");
        if (string.IsNullOrWhiteSpace(entry.ListingTypeId)) errors.Add($"{where}: 'tipoAnuncio' vazio.");
        if (entry.Categories.Count == 0) errors.Add($"{where}: 'categorias' precisa ter pelo menos uma categoria.");
        if (entry.Categories.Intersect(entry.ExcludedCategories).Any()) errors.Add($"{where}: categoria listada como monitorada e excluída ao mesmo tempo.");
        if (entry.SaleTaxRate is < 0 or >= 1) errors.Add($"{where}: 'impostoSobreVenda' deve ficar em [0, 1).");
        if (entry.FreeShippingThreshold <= 0) errors.Add($"{where}: 'limiarFreteGratis' precisa ser positivo.");
        if (entry.FeeBandLimits.Any(limit => limit <= 1)) errors.Add($"{where}: 'faixasTarifaFixa' só aceita valores acima de R$ 1.");
        if (entry.AbsorbedFreightPerUnit < 0 || entry.DomesticFreightTotal < 0 || entry.CustomsFeesTotal < 0)
        {
            errors.Add($"{where}: fretes e despesas não podem ser negativos.");
        }

        if (entry.TrendTolerance is < 0 or >= 1) errors.Add($"{where}: 'toleranciaSetaTendencia' deve ficar em [0, 1).");
        if (entry.FinalistsPerCycle < 1) errors.Add($"{where}: 'finalistasPorCiclo' precisa ser ao menos 1.");
        if (entry.CandidatesPerCycle < entry.FinalistsPerCycle) errors.Add($"{where}: 'candidatosPorCiclo' não pode ser menor que 'finalistasPorCiclo'.");
        if (entry.ListingsPerProductForSeries < 1) errors.Add($"{where}: 'anunciosPorProdutoNaSerie' precisa ser ao menos 1.");
        if (entry.VisitSampleSize < entry.ListingsPerProductForSeries) errors.Add($"{where}: 'anunciosMedidosPorProduto' não pode ser menor que 'anunciosPorProdutoNaSerie'.");

        Guard(() => new MarginOptions { TargetMargin = entry.TargetMargin, ExchangeSpread = entry.ExchangeSpread }.Validate(), where, errors);
        Guard(() => ToPriceReference(entry.PriceModel).Validate(), where, errors);
        Guard(() => ToDemand(entry.Demand).Validate(), where, errors);
        Guard(() => ToOpportunity(entry.Opportunity, entry.TargetMargin).Validate(), where, errors);
    }

    private static void AddInternalDuplicates(ConfigDocument document, List<string> errors)
    {
        foreach (var group in document.TaxRules.GroupBy(rule => (rule.Regime, rule.ValidFrom)).Where(group => group.Count() > 1))
        {
            errors.Add($"tributos: '{group.Key.Regime}' vigente desde {group.Key.ValidFrom:yyyy-MM-dd} aparece {group.Count()} vezes.");
        }

        foreach (var group in document.Parameters.GroupBy(entry => entry.ValidFrom).Where(group => group.Count() > 1))
        {
            errors.Add($"parametros: vigentes desde {group.Key:yyyy-MM-dd} aparecem {group.Count()} vezes.");
        }
    }

    private List<string> FindClashes(ConfigDocument incoming, string excludeFile)
    {
        var current = Load();
        var clashes = new List<string>();

        foreach (var rule in incoming.TaxRules)
        {
            var clash = current.TaxRules.FirstOrDefault(existing => existing.File != excludeFile && SameVersion(existing.Entry, rule));
            if (clash is not null) clashes.Add($"Tributo '{rule.Regime}' vigente desde {rule.ValidFrom:yyyy-MM-dd} já existe em {clash.File}.");
        }

        foreach (var entry in incoming.Parameters)
        {
            var clash = current.Parameters.FirstOrDefault(existing => existing.File != excludeFile && existing.Entry.ValidFrom == entry.ValidFrom);
            if (clash is not null) clashes.Add($"Parâmetros vigentes desde {entry.ValidFrom:yyyy-MM-dd} já existem em {clash.File}.");
        }

        return clashes;
    }

    private async Task<WriteResult> AppendAsync(Action<ConfigDocument> change, CancellationToken cancellationToken)
    {
        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            ConfigDocument document;
            if (File.Exists(AdminFile))
            {
                var (existing, errors) = Parse(await File.ReadAllTextAsync(AdminFile, cancellationToken));
                if (existing is null || errors.Count > 0)
                {
                    return new WriteResult(false, [$"{options.Value.AdminFileName} está inválido e precisa ser corrigido antes: ", .. errors]);
                }

                document = existing;
            }
            else
            {
                document = new ConfigDocument { Schema = "./configuracoes.schema.json" };
            }

            change(document);

            var json = JsonSerializer.Serialize(document, Pretty);
            var (_, validation) = Parse(json);
            if (validation.Count > 0) return new WriteResult(false, validation);

            var clashes = FindClashes(document, excludeFile: options.Value.AdminFileName);
            if (clashes.Count > 0) return new WriteResult(false, clashes);

            await WriteAtomicallyAsync(AdminFile, json, cancellationToken);
            return new WriteResult(true, []);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    /// <summary>Escreve num temporário e troca: quem lê nunca vê arquivo pela metade.</summary>
    private async Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Folder);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static bool SameVersion(TaxRuleEntry left, TaxRuleEntry right) =>
        left.Regime == right.Regime && left.ValidFrom == right.ValidFrom;

    private static void Guard(Action validate, string where, List<string> errors)
    {
        try
        {
            validate();
        }
        catch (ArgumentException exception)
        {
            errors.Add($"{where}: {exception.Message}");
        }
    }

    internal static ImportTaxRates ToImportTaxRates(TaxRuleEntry rule) => new()
    {
        Regime = rule.Regime,
        ImportDutyBrackets = [.. rule.ImportDutyBrackets.Select(b => new ImportDutyBracket(b.UpToForeign, b.Rate, b.DeductionForeign))],
        Ipi = rule.Ipi,
        Pis = rule.Pis,
        Cofins = rule.Cofins,
        Icms = rule.Icms
    };

    private static PriceReferenceOptions ToPriceReference(PriceModelEntry? model)
    {
        var defaults = new PriceReferenceOptions();
        return model is null ? defaults : new PriceReferenceOptions
        {
            WholeBucketLimit = model.WholeBucketLimit ?? defaults.WholeBucketLimit,
            CoarseBucketStep = model.CoarseBucketStep ?? defaults.CoarseBucketStep,
            TrimRatio = model.TrimRatio ?? defaults.TrimRatio,
            MinSellersForTrim = model.MinSellersForTrim ?? defaults.MinSellersForTrim,
            MinModeStrength = model.MinModeStrength ?? defaults.MinModeStrength,
            MinModalBucketSellers = model.MinModalBucketSellers ?? defaults.MinModalBucketSellers
        };
    }

    private static DemandOptions ToDemand(DemandEntry? demand)
    {
        var defaults = new DemandOptions();
        return demand is null ? defaults : new DemandOptions
        {
            LowDemandFloor = demand.LowDemandFloor ?? defaults.LowDemandFloor,
            VolumeHalfScale = demand.VolumeHalfScale ?? defaults.VolumeHalfScale,
            RateHalfScale = demand.RateHalfScale ?? defaults.RateHalfScale,
            PersistenceWindowWeeks = demand.PersistenceWindowWeeks ?? defaults.PersistenceWindowWeeks,
            RankingHalfScale = demand.RankingHalfScale ?? defaults.RankingHalfScale,
            MinBaseVisitsForRate = demand.MinBaseVisitsForRate ?? defaults.MinBaseVisitsForRate,
            ExplodingGrowthPercent = demand.ExplodingGrowthPercent ?? defaults.ExplodingGrowthPercent,
            MinVisitsForNewDemand = demand.MinVisitsForNewDemand ?? defaults.MinVisitsForNewDemand
        };
    }

    private static CategoryCostTable ToCategoryCosts(IEnumerable<CategoryCostEntry> entries) =>
        new(entries.ToDictionary(
            entry => entry.CategoryId,
            entry => new CategoryCost(entry.PackagingPerUnit, entry.BreakageReserve),
            StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Orgão desconhecido no JSON não derruba a configuração inteira: a linha é ignorada.
    /// Certificação é aviso, e um aviso a menos não pode parar a coleta.
    /// </summary>
    private static CertificationTable ToCertifications(IEnumerable<CertificationEntry> entries) =>
        new(entries
            .Select(entry => Enum.TryParse<CertifyingBody>(entry.Body, ignoreCase: true, out var body)
                ? new CertificationRequirement(entry.CategoryId, body, entry.Note)
                : null)
            .OfType<CertificationRequirement>());

    private static RunwayOptions ToRunway(RunwayEntry? runway)
    {
        var defaults = new RunwayOptions();
        return runway is null ? defaults : new RunwayOptions
        {
            MinimumDays = runway.MinimumDays ?? defaults.MinimumDays,
            AcceleratingLower = runway.AcceleratingLower ?? defaults.AcceleratingLower,
            AcceleratingUpper = runway.AcceleratingUpper ?? defaults.AcceleratingUpper,
            LinearLower = runway.LinearLower ?? defaults.LinearLower,
            LinearUpper = runway.LinearUpper ?? defaults.LinearUpper,
            DeceleratingLower = runway.DeceleratingLower ?? defaults.DeceleratingLower,
            DeceleratingUpper = runway.DeceleratingUpper ?? defaults.DeceleratingUpper,
            FallingLower = runway.FallingLower ?? defaults.FallingLower,
            FallingUpper = runway.FallingUpper ?? defaults.FallingUpper,
            FlatTolerance = runway.FlatTolerance ?? defaults.FlatTolerance
        };
    }

    private static OpportunityOptions ToOpportunity(OpportunityEntry? opportunity, decimal targetMargin)
    {
        var defaults = new OpportunityOptions();
        return new OpportunityOptions
        {
            TargetMargin = targetMargin,
            PriceDropWeight = opportunity?.PriceDropWeight ?? defaults.PriceDropWeight,
            DispersionWeight = opportunity?.DispersionWeight ?? defaults.DispersionWeight,
            ModeStrengthDropWeight = opportunity?.ModeStrengthDropWeight ?? defaults.ModeStrengthDropWeight,
            RisingDemandThreshold = opportunity?.RisingDemandThreshold ?? defaults.RisingDemandThreshold,
            HotCompetitionThreshold = opportunity?.HotCompetitionThreshold ?? defaults.HotCompetitionThreshold
        };
    }
}
