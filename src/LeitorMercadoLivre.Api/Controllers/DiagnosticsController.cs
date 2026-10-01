using LeitorMercadoLivre.Domain.Diagnostics;
using LeitorMercadoLivre.Infrastructure.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LeitorMercadoLivre.Api.Controllers;

[ApiController]
[Route("api/diagnostics/mercadolivre")]
public sealed class DiagnosticsController(IMercadoLivreDiagnosticsService diagnosticsService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MercadoLivreDiagnosticsReport>(StatusCodes.Status200OK)]
    public Task<MercadoLivreDiagnosticsReport> Run(CancellationToken cancellationToken) =>
        diagnosticsService.RunAsync(cancellationToken);
}