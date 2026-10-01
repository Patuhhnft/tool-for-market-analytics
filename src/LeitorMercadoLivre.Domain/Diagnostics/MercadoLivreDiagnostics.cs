namespace LeitorMercadoLivre.Domain.Diagnostics;

public sealed record EndpointDiagnosticResult(
    string Name,
    string Method,
    string Path,
    bool Executed,
    int? StatusCode,
    long? ElapsedMilliseconds,
    bool Accessible,
    string? Detail);

public sealed record MercadoLivreDiagnosticsReport(
    DateTimeOffset CheckedAt,
    bool TokenConfigured,
    IReadOnlyList<EndpointDiagnosticResult> Endpoints);