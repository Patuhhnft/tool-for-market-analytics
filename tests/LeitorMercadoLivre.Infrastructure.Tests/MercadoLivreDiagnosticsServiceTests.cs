using System.Net;
using LeitorMercadoLivre.Domain.Diagnostics;
using LeitorMercadoLivre.Infrastructure.Diagnostics;
using Microsoft.Extensions.Options;

namespace LeitorMercadoLivre.Infrastructure.Tests;

public sealed class MercadoLivreDiagnosticsServiceTests
{
    private const int ProbeCount = 10;

    [Test]
    public async Task RunAsync_WithoutToken_SkipsRequestsAndReportsMissingConfiguration()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, new MercadoLivreDiagnosticsOptions());

        var report = await service.RunAsync(CancellationToken.None);

        Assert.That(report.TokenConfigured, Is.False);
        Assert.That(report.Endpoints, Has.Count.EqualTo(ProbeCount));
        Assert.That(report.Endpoints, Has.All.Matches<EndpointDiagnosticResult>(endpoint => !endpoint.Executed));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task RunAsync_WithConfiguration_UsesExpectedPathsAndBearerToken()
    {
        var handler = new RecordingHandler();
        var options = new MercadoLivreDiagnosticsOptions
        {
            AccessToken = "test-token",
            CategoryId = "MLB123",
            ProductId = "MLB-PRODUCT",
            ItemIds = "MLB1,MLB2"
        };
        var service = CreateService(handler, options);

        var report = await service.RunAsync(CancellationToken.None);

        Assert.That(report.TokenConfigured, Is.True);
        Assert.That(report.Endpoints, Has.All.Matches<EndpointDiagnosticResult>(endpoint => endpoint.Executed && endpoint.Accessible));
        Assert.That(handler.Requests.Select(request => request.RequestUri!.PathAndQuery), Is.EqualTo(new[]
        {
            "/trends/MLB",
            "/trends/MLB/MLB123",
            "/highlights/MLB/category/MLB123",
            "/sites/MLB/listing_prices?price=100",
            "/items/bulk?ids=MLB1%2CMLB2",
            "/items/MLB1",
            "/items/MLB1?attributes=id,price,sold_quantity,available_quantity",
            "/visits/items?ids=MLB1",
            "/items/MLB1/visits/time_window?last=7&unit=day",
            "/products/MLB-PRODUCT/items"
        }));
        Assert.That(handler.Requests, Has.All.Matches<HttpRequestMessage>(request =>
            request.Headers.Authorization?.Scheme == "Bearer" &&
            request.Headers.Authorization.Parameter == "test-token"));
    }

    [Test]
    public async Task RunAsync_WhenEndpointReturnsForbidden_ReportsStatusWithoutThrowing()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("not allowed")
        });
        var service = CreateService(handler, new MercadoLivreDiagnosticsOptions { AccessToken = "test-token" });

        var report = await service.RunAsync(CancellationToken.None);

        Assert.That(report.Endpoints.Where(endpoint => endpoint.Executed), Has.All.Matches<EndpointDiagnosticResult>(endpoint =>
            endpoint.Executed && !endpoint.Accessible && endpoint.StatusCode == (int)HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RunAsync_BulkEnvelope200WithForbiddenItems_IsNotReportedAsAccessible()
    {
        // O /items/bulk responde 200 no envelope mesmo quando cada item falha. Foi exatamente
        // o que o diagnóstico real de 24/09 recebeu — e o que antes virava falso positivo.
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/items/bulk"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"id":"MLB1","status_code":403,"error":{"message":"Access to the requested resource is forbidden"}}]""")
            }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var service = CreateService(handler, new MercadoLivreDiagnosticsOptions { AccessToken = "t", ItemIds = "MLB1" });

        var report = await service.RunAsync(CancellationToken.None);
        var bulk = report.Endpoints.Single(endpoint => endpoint.Name == "Itens em lote");

        Assert.Multiple(() =>
        {
            Assert.That(bulk.StatusCode, Is.EqualTo(200));
            Assert.That(bulk.Accessible, Is.False);
            Assert.That(bulk.Detail, Does.StartWith("Envelope HTTP 200, mas todos os itens falharam (403)."));
        });
    }

    private static MercadoLivreDiagnosticsService CreateService(
        RecordingHandler handler,
        MercadoLivreDiagnosticsOptions options) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.test.local") }, Options.Create(options));

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }
}
