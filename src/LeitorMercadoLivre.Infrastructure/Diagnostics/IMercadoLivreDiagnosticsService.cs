using LeitorMercadoLivre.Domain.Diagnostics;

namespace LeitorMercadoLivre.Infrastructure.Diagnostics;

public interface IMercadoLivreDiagnosticsService
{
    Task<MercadoLivreDiagnosticsReport> RunAsync(CancellationToken cancellationToken);
}