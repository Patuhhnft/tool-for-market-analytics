using System.ComponentModel.DataAnnotations;
using LeitorMercadoLivre.Api.Security;
using LeitorMercadoLivre.Domain.Suppliers;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Api.Controllers;

// Os limites decimais são lidos em cultura invariante: numa máquina em pt-BR, "0.0001" seria
// inválido (o separador é vírgula) e todo cadastro quebraria em tempo de execução.
public sealed record SupplierInput(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Range(typeof(decimal), "0.0001", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal UnitPrice,
    [Required, RegularExpression("^[A-Za-z]{3}$")] string Currency,
    [Range(1, 1_000_000)] int MinimumOrder,
    [Range(1, 1_000_000)] int ShipmentQuantity,
    [Range(typeof(decimal), "0", "10000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal InternationalFreightTotal,
    [StringLength(200)] string? Contact,
    [StringLength(40)] string? WhatsApp,
    [StringLength(500), Url] string? Url);

/// <summary>
/// Fornecedores cadastrados à mão. Não há fonte automática ativa: a AliExpress Affiliate API
/// exige cadastro e aprovação que ainda não existem, e scraping o §12 manda evitar.
/// </summary>
[ApiController]
[Route("api")]
public sealed class SuppliersController(
    IDbContextFactory<LeitorDbContext> contexts,
    MarginAssessor margins,
    TimeProvider clock,
    ILogger<SuppliersController> logger) : ControllerBase
{
    [HttpGet("products/{productId}/suppliers")]
    public async Task<IReadOnlyList<SupplierView>> List(string productId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(s => s.ProductId == productId && s.Active)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        return [.. suppliers.Select(ProductsController.ToView)];
    }

    /// <summary>Cadastra e já recalcula a margem do produto, para o card mostrar o número na hora.</summary>
    [HttpPost("products/{productId}/suppliers")]
    [AdminOnly, RequireOperator]
    public async Task<ActionResult<SupplierView>> Create(string productId, SupplierInput input, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(input.WhatsApp) && WhatsAppLink.From(input.WhatsApp) is null)
        {
            ModelState.AddModelError(nameof(input.WhatsApp),
                "Número de WhatsApp não reconhecido. Use o formato internacional (+86 138 1234 5678) ou brasileiro com DDD.");
            return ValidationProblem(ModelState);
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken)) return NotFound();

        var now = clock.GetUtcNow();
        var supplier = new Supplier
        {
            ProductId = productId,
            Name = input.Name.Trim(),
            UnitPrice = input.UnitPrice,
            Currency = input.Currency.ToUpperInvariant(),
            MinimumOrder = input.MinimumOrder,
            ShipmentQuantity = Math.Max(input.ShipmentQuantity, input.MinimumOrder),
            InternationalFreightTotal = input.InternationalFreightTotal,
            Contact = input.Contact?.Trim(),
            WhatsApp = input.WhatsApp?.Trim(),
            Url = input.Url?.Trim(),
            Source = SupplierSource.Manual,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByOperatorId = HttpContext.OperatorId(),
            UpdatedByOperatorId = HttpContext.OperatorId()
        };

        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshQuietlyAsync(db, productId, cancellationToken);

        return CreatedAtAction(nameof(List), new { productId }, ProductsController.ToView(supplier));
    }

    /// <summary>Desativa em vez de apagar: análises antigas que usaram este fornecedor continuam auditáveis (I7).</summary>
    [HttpDelete("suppliers/{supplierId:long}")]
    [AdminOnly, RequireOperator]
    public async Task<IActionResult> Deactivate(long supplierId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var supplier = await db.Suppliers.FindAsync([supplierId], cancellationToken);
        if (supplier is null) return NotFound();

        supplier.Active = false;
        supplier.UpdatedAt = clock.GetUtcNow();
        supplier.UpdatedByOperatorId = HttpContext.OperatorId();
        await db.SaveChangesAsync(cancellationToken);
        await RefreshQuietlyAsync(db, supplier.ProductId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// O fornecedor já está salvo; se o recálculo falhar (configuração inválida, por exemplo),
    /// o cadastro não vira erro 500 — a margem é recalculada no próximo ciclo.
    /// </summary>
    private async Task RefreshQuietlyAsync(LeitorDbContext db, string productId, CancellationToken cancellationToken)
    {
        try
        {
            await margins.RefreshLatestAsync(db, productId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Margem de {ProductId} não recalculada agora.", productId);
        }
    }
}
