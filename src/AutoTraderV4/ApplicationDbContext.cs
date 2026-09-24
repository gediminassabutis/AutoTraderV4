using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4;

public sealed class ApplicationDbContext : DbContext
{
    public DbSet<PortfolioPosition> Positions { get; set; }
    public DbSet<PortfolioStateRecord> PortfolioStates { get; set; }
    public DbSet<MarketSnapshot> MarketSnapshots { get; set; }
    public DbSet<StrategySignalRecord> StrategySignals { get; set; }
    public DbSet<TradeAuditEntry> TradeAuditEntries { get; set; }

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
            entity.Property(x => x.Sector).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Quantity).HasPrecision(18, 6);
            entity.Property(x => x.AveragePrice).HasPrecision(18, 6);
            entity.Property(x => x.CurrentPrice).HasPrecision(18, 6);
            entity.Property(x => x.Currency).IsRequired().HasMaxLength(10);
            entity.Property(x => x.StopLoss).HasPrecision(18, 6);
            entity.Property(x => x.TakeProfit).HasPrecision(18, 6);
            entity.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
            entity.HasIndex(x => x.Ticker).IsUnique();
        });

        modelBuilder.Entity<PortfolioStateRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TotalValue).HasPrecision(18, 6);
            entity.Property(x => x.Cash).HasPrecision(18, 6);
            entity.Property(x => x.DailyProfitLoss).HasPrecision(18, 6);
            entity.Property(x => x.WeeklyProfitLoss).HasPrecision(18, 6);
            entity.Property(x => x.MonthlyProfitLoss).HasPrecision(18, 6);
            entity.Property(x => x.PeakPortfolioValue).HasPrecision(18, 6);
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasIndex(x => x.UpdatedAtUtc);
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
            entity.Property(x => x.Side).IsRequired().HasMaxLength(16);
            entity.Property(x => x.OrderType).IsRequired().HasMaxLength(32);
            entity.Property(x => x.CreatedUtc).IsRequired();
        });

        modelBuilder.Entity<TradeAuditEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).IsRequired().HasMaxLength(64);
            entity.Property(x => x.Symbol).IsRequired().HasMaxLength(64);
            entity.Property(x => x.StrategyName).IsRequired().HasMaxLength(256);
            entity.Property(x => x.FinalScore).HasPrecision(8, 2);
            entity.Property(x => x.ConfidenceScore).HasPrecision(8, 2);
            entity.Property(x => x.EntryPrice).HasPrecision(18, 6);
            entity.Property(x => x.StopLoss).HasPrecision(18, 6);
            entity.Property(x => x.TakeProfit).HasPrecision(18, 6);
            entity.Property(x => x.Quantity).HasPrecision(18, 6);
            entity.Property(x => x.Side).HasMaxLength(16);
            entity.Property(x => x.OrderType).HasMaxLength(32);
            entity.Property(x => x.SignalScoresJson).IsRequired();
            entity.Property(x => x.ForecastJson).IsRequired();
            entity.Property(x => x.RiskAssessmentJson).IsRequired();
            entity.Property(x => x.RecommendationJson).IsRequired();
            entity.Property(x => x.CreatedUtc).IsRequired();
            entity.HasIndex(x => new { x.Symbol, x.CreatedUtc });
        });
    }
}

public sealed class PortfolioPosition
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Sector { get; set; } = "Unknown";
    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal ConfidenceScore { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class PortfolioStateRecord
{
    public Guid Id { get; set; }
    public decimal TotalValue { get; set; }
    public decimal Cash { get; set; }
    public decimal DailyProfitLoss { get; set; }
    public decimal WeeklyProfitLoss { get; set; }
    public decimal MonthlyProfitLoss { get; set; }
    public decimal PeakPortfolioValue { get; set; }
    public bool DefensiveModeActive { get; set; }
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

public sealed class TradeAuditEntry
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public decimal FinalScore { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal Quantity { get; set; }
    public string Side { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string SignalScoresJson { get; set; } = string.Empty;
    public string ForecastJson { get; set; } = string.Empty;
    public string RiskAssessmentJson { get; set; } = string.Empty;
    public string RecommendationJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
