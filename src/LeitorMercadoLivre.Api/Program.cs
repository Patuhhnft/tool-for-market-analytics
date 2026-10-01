using System.Text.Json.Serialization;
using Hangfire;
using LeitorMercadoLivre.Infrastructure;
using LeitorMercadoLivre.Infrastructure.Diagnostics;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
// Origens do front vêm da configuração (Cors:Origins); o padrão cobre o Vite local.
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://127.0.0.1:5173", "http://localhost:5173"];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// Diagnóstico de endpoints (primeira entrega): usa o token direto do ambiente.
builder.Services.Configure<MercadoLivreDiagnosticsOptions>(
    builder.Configuration.GetSection(MercadoLivreDiagnosticsOptions.SectionName));
builder.Services.PostConfigure<MercadoLivreDiagnosticsOptions>(options =>
{
    options.AccessToken ??= Environment.GetEnvironmentVariable("MELI_ACCESS_TOKEN");
});
builder.Services.AddHttpClient<IMercadoLivreDiagnosticsService, MercadoLivreDiagnosticsService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MercadoLivreDiagnosticsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Banco, pipeline e configuração — os mesmos do Worker.
builder.Services.AddLeitorInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

// A API só ENFILEIRA coletas no Hangfire; quem executa é o Worker.
var connection = builder.Configuration.GetConnectionString(LeitorDbContext.ConnectionStringName)!;
builder.Services.AddHangfire(configuration => configuration.UseLeitorStorage(connection));

var app = builder.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<LeitorDbContext>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}

app.MapOpenApi();

// Em desenvolvimento só há endpoint HTTP: redirecionar para HTTPS só gerava o aviso
// "Failed to determine the https port" a cada requisição.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");
app.UseAuthorization();
app.MapControllers();

// Painel do Hangfire. Sem filtro próprio, ele só aceita chamadas da própria máquina.
app.UseHangfireDashboard("/hangfire");

app.Run();

// Exposto para os testes de API (WebApplicationFactory).
public partial class Program;
