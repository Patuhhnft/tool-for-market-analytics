using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LeitorMercadoLivre.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.MercadoLivre;

public sealed class MercadoLivreApiOptions
{
    public const string SectionName = "MercadoLivre";

    public string BaseUrl { get; set; } = "https://api.mercadolibre.com";

    public string SiteId { get; set; } = "MLB";

    /// <summary>App ID do DevCenter. Não é segredo — aparece na URL de autorização.</summary>
    public string? ClientId { get; set; }

    /// <summary>Segredo. Só por variável de ambiente (MercadoLivre__ClientSecret).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Credencial inicial, lida uma única vez quando o banco ainda não tem nenhuma.</summary>
    public string? BootstrapAccessToken { get; set; }

    public string? BootstrapRefreshToken { get; set; }

    /// <summary>Renova com esta antecedência, para nenhuma chamada sair com token prestes a vencer.</summary>
    public TimeSpan RefreshMargin { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxRetries { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Dias pedidos ao /visits/time_window. Duas semanas completas medem o delta semanal.</summary>
    public int VisitWindowDays { get; set; } = 15;
}

public sealed record StoredCredential(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public interface ICredentialStore
{
    Task<StoredCredential?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(StoredCredential credential, CancellationToken cancellationToken);
}

/// <summary>Credencial no Postgres, com fábrica de contexto para poder viver num singleton.</summary>
public sealed class DbCredentialStore(IDbContextFactory<LeitorDbContext> contexts, TimeProvider clock) : ICredentialStore
{
    private const string Provider = "mercadolivre";

    public async Task<StoredCredential?> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Credentials.AsNoTracking().SingleOrDefaultAsync(c => c.Provider == Provider, cancellationToken);
        return row is null ? null : new StoredCredential(row.AccessToken, row.RefreshToken, row.ExpiresAt);
    }

    public async Task SaveAsync(StoredCredential credential, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Credentials.SingleOrDefaultAsync(c => c.Provider == Provider, cancellationToken);

        if (row is null)
        {
            row = new OAuthCredential { Provider = Provider, AccessToken = "", RefreshToken = "" };
            db.Credentials.Add(row);
        }

        row.AccessToken = credential.AccessToken;
        row.RefreshToken = credential.RefreshToken;
        row.ExpiresAt = credential.ExpiresAt;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class MercadoLivreAuthException(string message) : Exception(message);

/// <summary>
/// Entrega um access token válido e renova sozinho pelo refresh token.
/// <para>
/// O refresh token do Mercado Livre é de USO ÚNICO: cada renovação devolve um novo, e o
/// antigo morre. Se duas chamadas renovassem ao mesmo tempo, a segunda usaria um token já
/// consumido e todo o ciclo cairia. Por isso a renovação é serializada, e quem pede
/// renovação por ter levado 401 informa QUAL token foi recusado — se outro já renovou
/// nesse meio-tempo, recebe o novo sem gastar outro refresh.
/// </para>
/// </summary>
public sealed class MercadoLivreTokenProvider(
    ICredentialStore store,
    IHttpClientFactory httpClients,
    IOptions<MercadoLivreApiOptions> options,
    TimeProvider clock,
    ILogger<MercadoLivreTokenProvider> logger)
{
    public const string AuthClientName = "mercadolivre-auth";

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly MercadoLivreApiOptions settings = options.Value;
    private StoredCredential? current;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, string? rejectedToken = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            current ??= await store.LoadAsync(cancellationToken) ?? await BootstrapAsync(cancellationToken);

            var rejected = rejectedToken is not null && rejectedToken == current.AccessToken;
            var expiring = current.ExpiresAt <= clock.GetUtcNow() + settings.RefreshMargin;

            if (rejected || expiring)
            {
                current = await RefreshAsync(current, cancellationToken);
            }

            return current.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<StoredCredential> BootstrapAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.BootstrapAccessToken) && string.IsNullOrWhiteSpace(settings.BootstrapRefreshToken))
        {
            throw new MercadoLivreAuthException(
                "Nenhuma credencial do Mercado Livre. Defina MercadoLivre__BootstrapRefreshToken " +
                "(e MercadoLivre__ClientId / MercadoLivre__ClientSecret) no ambiente.");
        }

        // Validade desconhecida: com refresh token, renova já e passa a saber quando vence;
        // sem ele, usa o access token até levar 401.
        var credential = new StoredCredential(
            settings.BootstrapAccessToken ?? "",
            settings.BootstrapRefreshToken ?? "",
            string.IsNullOrWhiteSpace(settings.BootstrapRefreshToken) ? clock.GetUtcNow().AddHours(6) : DateTimeOffset.MinValue);

        await store.SaveAsync(credential, cancellationToken);
        logger.LogInformation("Credencial do Mercado Livre inicializada a partir do ambiente.");
        return credential;
    }

    private async Task<StoredCredential> RefreshAsync(StoredCredential stale, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stale.RefreshToken) ||
            string.IsNullOrWhiteSpace(settings.ClientId) ||
            string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new MercadoLivreAuthException(
                "O token venceu e não há como renovar: faltam refresh token, ClientId ou ClientSecret.");
        }

        var http = httpClients.CreateClient(AuthClientName);
        using var response = await http.PostAsync(
            new Uri(new Uri(settings.BaseUrl), "/oauth/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["refresh_token"] = stale.RefreshToken
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MercadoLivreAuthException(
                $"Renovação do token recusada (HTTP {(int)response.StatusCode}). Refaça a autorização no navegador. {body}");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new MercadoLivreAuthException("Resposta de renovação vazia.");

        var renewed = new StoredCredential(
            token.AccessToken,
            token.RefreshToken ?? stale.RefreshToken,
            clock.GetUtcNow().AddSeconds(token.ExpiresIn));

        // Grava ANTES de devolver: o refresh antigo já morreu do lado da ML.
        await store.SaveAsync(renewed, cancellationToken);
        logger.LogInformation("Token do Mercado Livre renovado; vence em {ExpiresAt:u}.", renewed.ExpiresAt);
        return renewed;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Põe o Bearer em toda chamada e, num 401, renova uma vez e repete.</summary>
public sealed class MercadoLivreAuthHandler(MercadoLivreTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var renewed = await tokens.GetAccessTokenAsync(cancellationToken, rejectedToken: token);

        using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        return await base.SendAsync(retry, cancellationToken);
    }
}

/// <summary>
/// Repete em 429 e 5xx com espera crescente, respeitando Retry-After quando a API manda.
/// Só para GET: repetir um POST poderia duplicar efeito.
/// </summary>
public sealed class RetryHandler(IOptions<MercadoLivreApiOptions> options, TimeProvider clock) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var response = await base.SendAsync(request, cancellationToken);

        for (var attempt = 1; attempt <= settings.MaxRetries && request.Method == HttpMethod.Get && IsTransient(response); attempt++)
        {
            var wait = response.Headers.RetryAfter?.Delta
                ?? TimeSpan.FromTicks(settings.RetryBaseDelay.Ticks * (1L << (attempt - 1)));

            response.Dispose();
            await Task.Delay(wait, clock, cancellationToken);

            using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
            retry.Headers.Authorization = request.Headers.Authorization;
            response = await base.SendAsync(retry, cancellationToken);
        }

        return response;
    }

    private static bool IsTransient(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
}
