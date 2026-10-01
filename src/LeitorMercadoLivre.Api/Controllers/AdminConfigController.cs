using LeitorMercadoLivre.Api.Security;
using LeitorMercadoLivre.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace LeitorMercadoLivre.Api.Controllers;

public sealed record ConfigOverview(
    string Folder,
    string AdminFile,
    IReadOnlyList<ConfigFileStatus> Files,
    IReadOnlyList<string> Errors,
    IReadOnlyList<Sourced<TaxRuleEntry>> TaxRules,
    IReadOnlyList<Sourced<ParametersEntry>> Parameters,
    EffectiveView? Effective,
    IReadOnlyList<string> Problems);

public sealed record EffectiveView(DateOnly Date, Sourced<ParametersEntry> Parameters, Sourced<TaxRuleEntry> TaxRule);

public sealed record FileUpload(string Nome, string Conteudo);

/// <summary>
/// Área do administrador. Lê e escreve a pasta de configuração — a mesma que um agente pode
/// abastecer soltando arquivos nela. Toda escrita é versão nova com vigência; nada é sobrescrito.
/// </summary>
[ApiController]
[Route("api/admin/config")]
public sealed class AdminConfigController(BusinessConfigurationStore store, TimeProvider clock, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public ConfigOverview Get()
    {
        var loaded = store.Load();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var resolved = store.ResolveAt(today, loaded);

        return new ConfigOverview(
            loaded.Folder,
            configuration[$"{ConfigurationFolderOptions.SectionName}:AdminFileName"] ?? new ConfigurationFolderOptions().AdminFileName,
            loaded.Files,
            loaded.Errors,
            [.. loaded.TaxRules.OrderBy(t => t.Entry.Regime).ThenByDescending(t => t.Entry.ValidFrom)],
            [.. loaded.Parameters.OrderByDescending(p => p.Entry.ValidFrom)],
            resolved.Configuration is { } effective ? new EffectiveView(today, effective.Parameters, effective.TaxRule) : null,
            resolved.Problems);
    }

    [HttpGet("files/{name}")]
    public IActionResult Download(string name)
    {
        var file = store.Load().Files.FirstOrDefault(f => f.FileName == name);
        if (file is null) return NotFound();

        return PhysicalFile(Path.Combine(store.Folder, file.FileName), "application/json", file.FileName);
    }

    [HttpPost("tax-rules")]
    [AdminOnly, RequireOperator]
    public async Task<IActionResult> AddTaxRule(TaxRuleEntry entry, CancellationToken cancellationToken) =>
        Outcome(await store.AppendTaxRuleAsync(entry, cancellationToken));

    [HttpPost("parameters")]
    [AdminOnly, RequireOperator]
    public async Task<IActionResult> AddParameters(ParametersEntry entry, CancellationToken cancellationToken) =>
        Outcome(await store.AppendParametersAsync(entry, cancellationToken));

    /// <summary>Recebe um arquivo JSON inteiro. Só chega à pasta se passar na validação completa.</summary>
    [HttpPost("files")]
    [AdminOnly, RequireOperator]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> Upload(FileUpload upload, CancellationToken cancellationToken) =>
        Outcome(await store.SaveFileAsync(upload.Nome, upload.Conteudo, cancellationToken));

    private IActionResult Outcome(WriteResult result) => result.Saved
        ? Ok(Get())
        : UnprocessableEntity(new { errors = result.Errors });
}
