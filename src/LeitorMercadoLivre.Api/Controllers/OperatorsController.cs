using LeitorMercadoLivre.Domain.Operators;
using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Api.Controllers;

public sealed record OperatorView(long Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt);

/// <summary>
/// Sem <c>[Required]</c> nem <c>[StringLength]</c> de propósito: quem valida nome de operador
/// é <see cref="OperatorName"/>, e só ele. Com os dois, o campo teria dois validadores falando
/// com vozes diferentes — o do framework ("The Nome field is required") ganharia do nosso
/// ("Digite um nome"), e a mensagem boa nunca apareceria.
/// </summary>
public sealed record NewOperator(string? Nome);

/// <summary>
/// Quem está trabalhando. A tela de seleção abre antes de tudo e pergunta o nome.
/// <para>
/// <b>Identificação por boa-fé, não autenticação.</b> A empresa tem uma conta só, e a pessoa
/// se identifica escolhendo o próprio nome — sem senha individual. Serve para a auditoria
/// responder "quem mudou isto?", nunca para decidir quem pode o quê.
/// </para>
/// <para>
/// Estes três endpoints ficam FORA do <c>AdminOnly</c> de propósito: a tela de seleção é a
/// primeira coisa que aparece, antes de haver qualquer chave digitada, e sem ela ninguém
/// consegue nem se identificar. Quem protege o conjunto é o perímetro — o <c>AdminOnly</c>
/// só aceita chamada da própria máquina quando não há chave configurada.
/// </para>
/// </summary>
[ApiController]
[Route("api/operadores")]
public sealed class OperatorsController(IDbContextFactory<LeitorDbContext> contexts, TimeProvider clock) : ControllerBase
{
    /// <summary>Os operadores ativos, com a última vez que cada um se identificou.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<OperatorView>> List(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // A última sessão de cada um serve para a tela destacar quem estava trabalhando.
        var lastSeen = await db.WorkSessions.AsNoTracking()
            .GroupBy(session => session.OperatorId)
            .Select(group => new { OperatorId = group.Key, At = group.Max(s => s.StartedAt) })
            .ToDictionaryAsync(item => item.OperatorId, item => item.At, cancellationToken);

        var operators = await db.Operators.AsNoTracking()
            .Where(op => op.Active)
            .ToListAsync(cancellationToken);

        return
        [
            .. operators
                .Select(op => new OperatorView(op.Id, op.Name, op.CreatedAt, lastSeen.GetValueOrDefault(op.Id)))
                // Quem trabalhou por último primeiro; quem nunca trabalhou, por nome.
                .OrderByDescending(view => view.LastSeenAt ?? DateTimeOffset.MinValue)
                .ThenBy(view => view.Name, StringComparer.CurrentCulture)
        ];
    }

    /// <summary>
    /// Cadastra um nome novo. Nome repetido não cria outro operador: devolve o que já existe.
    /// <para>
    /// "José", "JOSE" e " jose " são a mesma pessoa — se virassem três linhas, a auditoria se
    /// partiria em três e nenhuma contaria a história inteira.
    /// </para>
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OperatorView>> Create(NewOperator input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var (name, error) = OperatorName.TryParse(input.Nome);
        if (name is null) return Problem(error, statusCode: StatusCodes.Status400BadRequest, title: "Nome inválido");

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        var existing = await db.Operators.FirstOrDefaultAsync(op => op.NameKey == name.Key, cancellationToken);
        if (existing is not null)
        {
            // Reativa em vez de duplicar: o histórico dele continua valendo.
            if (!existing.Active)
            {
                existing.Active = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new OperatorView(existing.Id, existing.Name, existing.CreatedAt, null));
        }

        var created = new Operator { Name = name.Display, NameKey = name.Key, CreatedAt = clock.GetUtcNow(), Active = true };
        db.Operators.Add(created);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), new OperatorView(created.Id, created.Name, created.CreatedAt, null));
    }

    /// <summary>
    /// Registra que este operador começou a trabalhar. Uma linha por escolha na tela — é o que
    /// permite reconstruir quem estava no sistema em cada momento.
    /// </summary>
    [HttpPost("{operatorId:long}/sessoes")]
    public async Task<IActionResult> StartSession(long operatorId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        if (!await db.Operators.AnyAsync(op => op.Id == operatorId && op.Active, cancellationToken)) return NotFound();

        db.WorkSessions.Add(new WorkSession { OperatorId = operatorId, StartedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}
