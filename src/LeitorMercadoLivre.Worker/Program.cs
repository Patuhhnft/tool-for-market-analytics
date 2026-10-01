using System.Reflection;
using Hangfire;
using LeitorMercadoLivre.Infrastructure;
using LeitorMercadoLivre.Infrastructure.Persistence;
using LeitorMercadoLivre.Infrastructure.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Cofre de segredos do .NET, em qualquer ambiente (o padrão só carrega em Development).
// É onde a chave secreta do Mercado Livre vive: fora do repositório e persistente entre
// reinícios, para não ser redigitada a cada execução.
//   dotnet user-secrets set "MercadoLivre:ClientSecret" "..." --project src/LeitorMercadoLivre.Worker
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);

builder.Services.AddLeitorInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

var connection = builder.Configuration.GetConnectionString(LeitorDbContext.ConnectionStringName)!;
builder.Services.AddHangfire(configuration => configuration.UseLeitorStorage(connection));

// Um worker só: o ciclo é sequencial por natureza, e o refresh token de uso único não
// admite duas coletas renovando ao mesmo tempo.
builder.Services.AddHangfireServer(options => options.WorkerCount = 1);

var host = builder.Build();

// O banco precisa estar na versão do código antes de qualquer ciclo. As migrations do
// EF Core pegam trava exclusiva, então API e Worker subindo juntos não se atropelam.
await using (var db = await host.Services.GetRequiredService<IDbContextFactory<LeitorDbContext>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}

var interval = host.Services.GetRequiredService<IOptions<CollectionOptions>>().Value.IntervalHours;
host.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<CollectionJob>(
    CollectionJob.RecurringId,
    job => job.RunAsync(false, CancellationToken.None),
    $"0 */{interval} * * *");

// Ao subir, tenta a janela atual. Se ela já foi coletada, o pipeline só registra e sai (I10).
host.Services.GetRequiredService<IBackgroundJobClient>().Enqueue<CollectionJob>(job => job.RunAsync(false, CancellationToken.None));

await host.RunAsync();
