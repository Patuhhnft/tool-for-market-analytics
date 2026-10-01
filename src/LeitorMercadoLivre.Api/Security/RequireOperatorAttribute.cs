using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace LeitorMercadoLivre.Api.Security;

/// <summary>
/// Exige que a escrita venha assinada por um operador (<c>X-Operador-Id</c>).
/// <para>
/// <b>Isto NÃO é autenticação.</b> O cabeçalho não prova nada: qualquer um que alcance a API
/// pode declarar qualquer operador. É uma regra de INTEGRIDADE — não gravar dado sem dono —,
/// não de acesso. Quem guarda a porta é o <see cref="AdminOnlyAttribute"/>, com a chave única
/// da empresa; este filtro só garante que o que passou por lá fique atribuído a alguém.
/// </para>
/// <para>
/// Por isso a resposta de recusa é <c>400</c>, e não <c>401</c>: o pedido está malformado
/// (falta dizer quem está trabalhando), não sem credencial.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireOperatorAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Operador-Id";

    /// <summary>Onde o operador resolvido fica para o controller ler.</summary>
    public const string ItemKey = "operador";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var raw = http.Request.Headers[HeaderName].ToString();

        if (!long.TryParse(raw, out var operatorId))
        {
            context.Result = Problem($"A escrita precisa do cabeçalho {HeaderName} com o operador que está trabalhando.");
            return;
        }

        var contexts = http.RequestServices.GetRequiredService<IDbContextFactory<LeitorDbContext>>();
        await using var db = await contexts.CreateDbContextAsync(http.RequestAborted);

        var op = await db.Operators.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == operatorId && o.Active, http.RequestAborted);

        if (op is null)
        {
            // Operador apagado ou desativado enquanto a aba ficou aberta: a tela manda escolher
            // de novo, em vez de gravar com um id que não explica mais nada.
            context.Result = Problem($"Operador {operatorId} não existe ou está inativo. Escolha quem está trabalhando de novo.");
            return;
        }

        http.Items[ItemKey] = op;
        await next();
    }

    private static ObjectResult Problem(string detail) =>
        new(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Operador não informado", Detail = detail })
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
}

public static class OperatorContext
{
    /// <summary>
    /// O operador que assinou a requisição. Só existe onde <see cref="RequireOperatorAttribute"/>
    /// rodou — em qualquer outro lugar é <c>null</c>, e gravar autoria ali seria inventar.
    /// </summary>
    public static Operator? Operator(this HttpContext http) =>
        http?.Items.TryGetValue(RequireOperatorAttribute.ItemKey, out var value) == true ? value as Operator : null;

    public static long? OperatorId(this HttpContext http) => http.Operator()?.Id;
}
