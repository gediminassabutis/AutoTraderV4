using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4;

public sealed class ApplicationDbContext : DbContext
{
    public DbSet<PortfolioPosition> Positions { get; set; }
    public DbSet<MarketSnapshot> MarketSnapshots { get; set; }
    public DbSet<StrategySignalRecord> StrategySignals { get; set; }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortfolioPosition>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Ticker).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Quantity).HasPrecision(18, 6);
            entity.Property(x => x.AveragePrice).HasPrecision(18, 6);
            entity.Property(x => x.Currency).IsRequired().HasMaxLength(10);
            entity.HasIndex(x => x.Ticker).IsUnique();
        });

        modelBuilder.Entity<MarketSnapshot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Ticker).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Price).HasPrecision(18, 6);
            entity.Property(x => x.LastUpdatedUtc).IsRequired();
            entity.HasIndex(x => new { x.Ticker, x.LastUpdatedUtc });
        });

        modelBuilder.Entity<StrategySignalRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Ticker).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Signal).HasPrecision(18, 6);
            entity.Property(x => x.Quantity).HasPrecision(18, 6);
            entity.Property(x => x.Side).HasConversion<string>();
            entity.Property(x => x.OrderType).HasConversion<string>();
            entity.Property(x => x.CreatedUtc).IsRequired();
        });
    }
}

public sealed class PortfolioPosition
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class MarketSnapshot
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

public sealed class StrategySignalRecord
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public decimal Signal { get; set; }
    public decimal Quantity { get; set; }
    public string Side { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
