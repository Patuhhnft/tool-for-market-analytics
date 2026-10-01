using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeitorMercadoLivre.Infrastructure.Persistence;

public sealed class LeitorDbContext(DbContextOptions<LeitorDbContext> options) : DbContext(options)
{
    public const string ConnectionStringName = "Leitor";

    public DbSet<CollectionCycle> Cycles => Set<CollectionCycle>();

    public DbSet<CatalogProduct> Products => Set<CatalogProduct>();

    public DbSet<HighlightSnapshot> Highlights => Set<HighlightSnapshot>();

    public DbSet<ListingSnapshot> Listings => Set<ListingSnapshot>();

    public DbSet<SeenSeller> SeenSellers => Set<SeenSeller>();

    public DbSet<DailyVisits> Visits => Set<DailyVisits>();

    public DbSet<VisitCoverage> VisitCoverage => Set<VisitCoverage>();

    public DbSet<ProductAnalysis> Analyses => Set<ProductAnalysis>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<Operator> Operators => Set<Operator>();

    public DbSet<WorkSession> WorkSessions => Set<WorkSession>();

    public DbSet<OAuthCredential> Credentials => Set<OAuthCredential>();

    public DbSet<ExchangeRateQuote> ExchangeRates => Set<ExchangeRateQuote>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CollectionCycle>(cycle =>
        {
            cycle.HasIndex(c => c.WindowStart).IsUnique();
            cycle.Property(c => c.Status).HasMaxLength(16);
            cycle.Property(c => c.StatsJson).HasColumnType("jsonb");
        });

        model.Entity<CatalogProduct>(product =>
        {
            product.HasKey(p => p.Id);
            product.Property(p => p.Id).HasMaxLength(32);
            product.Property(p => p.CategoryId).HasMaxLength(32);
        });

        model.Entity<HighlightSnapshot>(highlight =>
        {
            highlight.HasIndex(h => new { h.CycleId, h.CategoryId, h.ProductId }).IsUnique();
            highlight.HasIndex(h => new { h.ProductId, h.CollectedAt });
        });

        model.Entity<ListingSnapshot>(listing =>
        {
            listing.HasIndex(l => new { l.CycleId, l.ItemId }).IsUnique();
            listing.HasIndex(l => new { l.ProductId, l.CycleId });
            listing.Property(l => l.Price).HasPrecision(18, 2);
        });

        model.Entity<SeenSeller>(seller =>
        {
            seller.HasKey(s => new { s.ProductId, s.SellerId });
            seller.HasIndex(s => new { s.ProductId, s.FirstSeenAt });
        });

        model.Entity<DailyVisits>(visits => visits.HasKey(v => new { v.ItemId, v.Date }));

        model.Entity<VisitCoverage>(coverage => coverage.HasKey(c => c.ItemId));

        model.Entity<ProductAnalysis>(analysis =>
        {
            analysis.HasIndex(a => new { a.CycleId, a.ProductId }).IsUnique();
            analysis.HasIndex(a => new { a.ProductId, a.CalculatedAt });
            // "Explorar oportunidades" começa por max(id) de cada produto. Sem este índice a
            // tabela cresce um registro por produto a cada ciclo e a busca passa a varrê-la.
            analysis.HasIndex(a => new { a.ProductId, a.Id });
            analysis.Property(a => a.PriceConfidence).HasMaxLength(16);
            analysis.Property(a => a.PriceJson).HasColumnType("jsonb");
            analysis.Property(a => a.DemandJson).HasColumnType("jsonb");
            analysis.Property(a => a.MarginJson).HasColumnType("jsonb");
            analysis.Property(a => a.CeilingJson).HasColumnType("jsonb");
            analysis.Property(a => a.RunwayJson).HasColumnType("jsonb");
            analysis.Property(a => a.RiskJson).HasColumnType("jsonb");
            analysis.Property(a => a.DemandPhase).HasMaxLength(16);
            analysis.Property(a => a.OpportunityJson).HasColumnType("jsonb");
        });

        model.Entity<Supplier>(supplier =>
        {
            supplier.HasIndex(s => new { s.ProductId, s.Active });
            supplier.Property(s => s.Currency).HasMaxLength(3);
        });

        model.Entity<Operator>(op =>
        {
            op.Property(o => o.Name).HasMaxLength(60);
            op.Property(o => o.NameKey).HasMaxLength(60);
            // Sobre a chave normalizada, não sobre o nome: é ela que define "a mesma pessoa".
            op.HasIndex(o => o.NameKey).IsUnique();
        });

        model.Entity<WorkSession>(session => session.HasIndex(s => new { s.OperatorId, s.StartedAt }));

        model.Entity<OAuthCredential>(credential => credential.HasIndex(c => c.Provider).IsUnique());

        model.Entity<ExchangeRateQuote>(quote => quote.HasKey(q => new { q.Currency, q.RequestedDate }));

        // snake_case no banco, PascalCase no código. Por último, depois de toda a configuração.
        foreach (var entity in model.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnake(entity.GetTableName()!));
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnake(property.Name));
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder conventions)
    {
        // I1: todo decimal vira numeric com escala suficiente para alíquota e câmbio.
        conventions.Properties<decimal>().HavePrecision(28, 10);
    }

    private static string ToSnake(string name) =>
        string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}

/// <summary>Só para o <c>dotnet ef</c> gerar migrations sem subir a aplicação.</summary>
public sealed class LeitorDbContextDesignFactory : IDesignTimeDbContextFactory<LeitorDbContext>
{
    public LeitorDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Leitor")
            ?? "Host=localhost;Port=5432;Database=leitor;Username=leitor;Password=leitor_dev";

        return new LeitorDbContext(new DbContextOptionsBuilder<LeitorDbContext>().UseNpgsql(connection).Options);
    }
}
