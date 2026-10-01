using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LeitorMercadoLivre.Api.Security;

/// <summary>
/// Protege o que altera dinheiro ou configuração: tributos, parâmetros, fornecedores, coleta.
/// <para>
/// Com <c>Admin:ApiKey</c> definida (variável <c>Admin__ApiKey</c>), exige o cabeçalho
/// <c>X-Admin-Key</c> com o mesmo valor. Sem ela, só aceita chamadas da própria máquina — a
/// área não fica aberta para a rede por esquecimento de configuração.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Admin-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var expected = http.RequestServices.GetRequiredService<IConfiguration>()["Admin:ApiKey"];

        if (string.IsNullOrEmpty(expected))
        {
            if (!IsLocal(http))
            {
                context.Result = Problem(StatusCodes.Status403Forbidden,
                    "Sem Admin:ApiKey configurada, a área do administrador só aceita chamadas da própria máquina.");
            }

            return;
        }

        var provided = http.Request.Headers[HeaderName].ToString();

        // Comparação em tempo constante: o tempo de resposta não revela quantos caracteres batem.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
        {
            context.Result = Problem(StatusCodes.Status401Unauthorized, $"Chave de administrador ausente ou errada ({HeaderName}).");
        }
    }

    private static bool IsLocal(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote);

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Title = "Acesso de administrador", Detail = detail }) { StatusCode = status };
}
