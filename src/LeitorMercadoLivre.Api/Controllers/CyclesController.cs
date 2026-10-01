using Hangfire;
using LeitorMercadoLivre.Api.Security;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Api.Controllers;

public sealed record CycleView(CycleInfo Cycle, int Analyses, string Stats);

[ApiController]
[Route("api/cycles")]
public sealed class CyclesController(IDbContextFactory<LeitorDbContext> contexts, IBackgroundJobClient jobs) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<CycleView>> List([FromQuery] int take = 10, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var cycles = await db.Cycles.AsNoTracking()
            .OrderByDescending(c => c.StartedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(c => new { Cycle = c, Analyses = db.Analyses.Count(a => a.CycleId == c.Id) })
            .ToListAsync(cancellationToken);

        return [.. cycles.Select(row => new CycleView(ProductsController.ToInfo(row.Cycle)!, row.Analyses, row.Cycle.StatsJson))];
    }

    /// <summary>
    /// Enfileira uma coleta agora, refazendo a janela atual. Quem executa é o Worker; a API só
    /// enfileira. Custa uma coleta inteira de chamadas ao Mercado Livre.
    /// </summary>
    [HttpPost("run")]
    [AdminOnly, RequireOperator]
    public ActionResult<object> Run()
    {
        var jobId = jobs.Enqueue<CollectionJob>(job => job.RunAsync(true, CancellationToken.None));
        return Accepted(new { jobId, message = "Coleta enfileirada. O Worker precisa estar rodando para executá-la." });
    }
}
