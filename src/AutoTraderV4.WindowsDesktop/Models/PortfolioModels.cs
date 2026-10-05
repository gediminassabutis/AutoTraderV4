namespace AutoTraderV4.WindowsDesktop.Models;

public sealed class PortfolioDashboard
{
    public PortfolioSummary Summary { get; set; } = new();
    public List<PortfolioPositionView> Positions { get; set; } = [];
    public List<WatchlistOpportunity> Watchlist { get; set; } = [];
    public List<MarketOverviewCard> MarketOverview { get; set; } = [];
    public List<DashboardInsight> Insights { get; set; } = [];
    public List<DashboardAlert> Alerts { get; set; } = [];
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PortfolioSummary
{
    public string Currency { get; set; } = "USD";
    public decimal TotalPortfolioValue { get; set; }
    public decimal DailyPnL { get; set; }
    public decimal WeeklyPnL { get; set; }
    public decimal MonthlyPnL { get; set; }
    public decimal AvailableCash { get; set; }
    public decimal TotalExposurePercent { get; set; }
    public bool DefensiveMode { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PortfolioPositionView
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public int? Confidence { get; set; }
    public string Sector { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal AllocationPercent { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WatchlistOpportunity
{
    public string Symbol { get; set; } = string.Empty;
    public string Rating { get; set; } = string.Empty;
    public int Confidence { get; set; }
    public decimal ForecastReturn { get; set; }
    public decimal RiskScore { get; set; }
    public decimal Price { get; set; }
    public string Sector { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public List<string> TopFactors { get; set; } = [];
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MarketOverviewCard
{
    public string Symbol { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal ChangePercent { get; set; }
    public string Trend { get; set; } = string.Empty;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardInsight
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Symbol { get; set; }
}

public sealed class DashboardAlert
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public DateTimeOffset TimeUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StockTrackingCycleResult
{
    public MarketDataReadinessView MarketDataReadiness { get; set; } = new();
    public List<string> UpdatedTickers { get; set; } = [];
    public int TrackedStockCount { get; set; }
    public int NewlyTrackedStockCount { get; set; }
    public int StockTicksCollected { get; set; }
    public List<StockTrackingTickerResult> SellRecommendations { get; set; } = [];
    public List<StockTrackingTickerResult> BuyOpportunities { get; set; } = [];
    public List<StockTrackingNewStockResult> NewStocks { get; set; } = [];
    public string? NewStockDiscoveryError { get; set; }
}

public sealed class MarketDataReadinessView
{
    public bool LiveProviderConfigured { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class StockTrackingTickerResult
{
    public string Ticker { get; set; } = string.Empty;
}

public sealed class StockTrackingNewStockResult
{
    public DateTimeOffset? AddedOn { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Isin { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
