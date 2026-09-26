using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4.Services;

public interface IPortfolioDashboardService
{
    Task<PortfolioSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<PortfolioDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
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
    public DateTimeOffset LastUpdatedUtc { get; set; }
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
    public string Currency { get; set; } = string.Empty;
    public decimal AllocationPercent { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

public sealed class MarketOverviewCard
{
    public string Symbol { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal ChangePercent { get; set; }
    public string Trend { get; set; } = string.Empty;
    public DateTimeOffset LastUpdatedUtc { get; set; }
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
    public DateTimeOffset LastUpdatedUtc { get; set; }
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

public sealed class PortfolioChartPoint
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class PortfolioAllocation
{
    public string Name { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string Color { get; set; } = string.Empty;
}

public sealed class ForecastProjection
{
    public string Symbol { get; set; } = string.Empty;
    public string Horizon { get; set; } = string.Empty;
    public decimal ProjectedPrice { get; set; }
    public decimal ProjectedReturnPercent { get; set; }
}

public sealed class PortfolioDashboard
{
    public PortfolioSummary Summary { get; set; } = new();
    public List<PortfolioPositionView> Positions { get; set; } = [];
    public List<WatchlistOpportunity> Watchlist { get; set; } = [];
    public List<MarketOverviewCard> MarketOverview { get; set; } = [];
    public List<DashboardInsight> Insights { get; set; } = [];
    public List<DashboardAlert> Alerts { get; set; } = [];
    public List<PortfolioChartPoint> GrowthCurve { get; set; } = [];
    public List<PortfolioChartPoint> EquityCurve { get; set; } = [];
    public List<PortfolioChartPoint> DrawdownHistory { get; set; } = [];
    public List<PortfolioChartPoint> MonthlyReturns { get; set; } = [];
    public List<PortfolioAllocation> Allocation { get; set; } = [];
    public List<PortfolioAllocation> SectorAllocation { get; set; } = [];
    public List<ForecastProjection> ForecastProjections { get; set; } = [];
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

public sealed class PortfolioDashboardService : IPortfolioDashboardService
{
    private static readonly string[] ProjectionHorizons = ["1D", "5D", "30D", "90D"];

    private static readonly (string Symbol, string Label)[] MarketBenchmarks =
    [
        ("SPX", "S&P 500"),
        ("^GSPC", "S&P 500"),
        ("IXIC", "Nasdaq"),
        ("^IXIC", "Nasdaq"),
        ("DJI", "Dow Jones"),
        ("^DJI", "Dow Jones"),
        ("FTSE", "FTSE 100"),
        ("^FTSE", "FTSE 100"),
        ("VIX", "VIX"),
        ("^VIX", "VIX")
    ];

    private static readonly string[] AllocationColors =
    [
        "#4f46e5",
        "#06b6d4",
        "#14b8a6",
        "#22c55e",
        "#f59e0b",
        "#f97316",
        "#e11d48",
        "#8b5cf6"
    ];

    private readonly ApplicationDbContext _context;
    private readonly ITrading212Client _trading212Client;

    public PortfolioDashboardService(ApplicationDbContext context, ITrading212Client trading212Client)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _trading212Client = trading212Client ?? throw new ArgumentNullException(nameof(trading212Client));
    }

    public async Task<PortfolioSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var dashboardContext = await LoadDashboardContextAsync(cancellationToken);
        var accountSummary = await _trading212Client.GetAccountSummaryAsync(cancellationToken);
        return BuildSummary(dashboardContext, accountSummary);
    }

    public async Task<PortfolioDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var dashboardContext = await LoadDashboardContextAsync(cancellationToken);
        var accountSummary = await _trading212Client.GetAccountSummaryAsync(cancellationToken);
        var positions = BuildPositions(dashboardContext.Positions, dashboardContext.LatestSnapshotsByTicker, dashboardContext.LatestSignalsByTicker);
        var portfolioHistory = BuildPortfolioHistory(dashboardContext.Positions, dashboardContext.Snapshots);
        var watchlist = BuildWatchlist(
                dashboardContext.Positions,
                dashboardContext.Signals,
                dashboardContext.SnapshotsByTicker,
                dashboardContext.LatestSnapshotsByTicker)
            .Take(20)
            .ToList();
        var marketOverview = BuildMarketOverview(dashboardContext.SnapshotsByTicker).ToList();
        var alerts = BuildAlerts(positions, watchlist, dashboardContext.Snapshots, dashboardContext.Signals).ToList();

        return new PortfolioDashboard
        {
            Summary = BuildSummary(dashboardContext, accountSummary),
            Positions = positions,
            Watchlist = watchlist,
            MarketOverview = marketOverview,
            Insights = BuildInsights(watchlist, marketOverview, alerts),
            Alerts = alerts,
            GrowthCurve = Downsample(portfolioHistory, 12),
            EquityCurve = Downsample(portfolioHistory, 30),
            DrawdownHistory = BuildDrawdownHistory(portfolioHistory),
            MonthlyReturns = BuildMonthlyReturns(portfolioHistory),
            Allocation = BuildAssetAllocation(positions),
            SectorAllocation = BuildSectorAllocation(positions),
            ForecastProjections = BuildForecasts(watchlist),
            LastUpdatedUtc = GetLastUpdatedUtc(dashboardContext)
        };
    }

    private async Task<DashboardContext> LoadDashboardContextAsync(CancellationToken cancellationToken)
    {
        var positions = await _context.Positions
            .AsNoTracking()
            .OrderBy(position => position.Ticker)
            .ToListAsync(cancellationToken);
        var snapshots = await _context.MarketSnapshots
            .AsNoTracking()
            .OrderBy(snapshot => snapshot.LastUpdatedUtc)
            .ToListAsync(cancellationToken);
        var signals = await _context.StrategySignals
            .AsNoTracking()
            .OrderByDescending(signal => signal.CreatedUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        var latestSignalsByTicker = signals
            .GroupBy(signal => signal.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var snapshotsByTicker = snapshots
            .GroupBy(snapshot => snapshot.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(snapshot => snapshot.LastUpdatedUtc).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var latestSnapshotsByTicker = snapshotsByTicker.ToDictionary(
            pair => pair.Key,
            pair => pair.Value[^1],
            StringComparer.OrdinalIgnoreCase);

        return new DashboardContext(positions, snapshots, signals, latestSignalsByTicker, snapshotsByTicker, latestSnapshotsByTicker);
    }

    private static PortfolioSummary BuildSummary(DashboardContext dashboardContext, Trading212AccountSummary accountSummary)
    {
        var positions = BuildPositions(dashboardContext.Positions, dashboardContext.LatestSnapshotsByTicker, dashboardContext.LatestSignalsByTicker);
        var portfolioHistory = BuildPortfolioHistory(dashboardContext.Positions, dashboardContext.Snapshots);
        var investedCapital = positions.Sum(position => position.Quantity * position.CurrentPrice);
        var totalPortfolioValue = accountSummary.Equity > 0m
            ? accountSummary.Equity
            : investedCapital + accountSummary.Cash;
        var exposure = totalPortfolioValue > 0m
            ? RoundTo2((investedCapital / totalPortfolioValue) * 100m)
            : 0m;
        var cashReservePercent = totalPortfolioValue > 0m
            ? (accountSummary.Cash / totalPortfolioValue) * 100m
            : 100m;

        return new PortfolioSummary
        {
            Currency = accountSummary.Currency,
            TotalPortfolioValue = RoundTo2(totalPortfolioValue),
            DailyPnL = CalculatePeriodPnL(portfolioHistory, TimeSpan.FromDays(1)),
            WeeklyPnL = CalculatePeriodPnL(portfolioHistory, TimeSpan.FromDays(7)),
            MonthlyPnL = CalculatePeriodPnL(portfolioHistory, TimeSpan.FromDays(30)),
            AvailableCash = RoundTo2(accountSummary.Cash),
            TotalExposurePercent = exposure,
            DefensiveMode = exposure > 95m || cashReservePercent < 5m || MaxDrawdownPercent(portfolioHistory) <= -10m,
            LastUpdatedUtc = GetLastUpdatedUtc(dashboardContext)
        };
    }

    private static List<PortfolioPositionView> BuildPositions(
        IReadOnlyCollection<PortfolioPosition> positions,
        IReadOnlyDictionary<string, MarketSnapshot> latestSnapshotsByTicker,
        IReadOnlyDictionary<string, StrategySignalRecord> latestSignalsByTicker)
    {
        var marketValues = positions
            .Select(position =>
            {
                var currentPrice = latestSnapshotsByTicker.TryGetValue(position.Ticker, out var snapshot)
                    ? snapshot.Price
                    : position.AveragePrice;
                return new
                {
                    Position = position,
                    CurrentPrice = currentPrice,
                    MarketValue = position.Quantity * currentPrice
                };
            })
            .ToList();
        var totalMarketValue = marketValues.Sum(item => item.MarketValue);

        return marketValues
            .Select(item =>
            {
                latestSignalsByTicker.TryGetValue(item.Position.Ticker, out var latestSignal);
                int? confidence = latestSignal is null ? null : CalculateConfidence(latestSignal.Signal);
                decimal? stopLoss = latestSignal is null ? null : RoundTo2(item.CurrentPrice * 0.95m);
                decimal? takeProfit = latestSignal is null ? null : RoundTo2(item.CurrentPrice * 1.125m);
                var unrealizedPnl = (item.CurrentPrice - item.Position.AveragePrice) * item.Position.Quantity;

                return new PortfolioPositionView
                {
                    Symbol = item.Position.Ticker,
                    Quantity = item.Position.Quantity,
                    EntryPrice = RoundTo2(item.Position.AveragePrice),
                    CurrentPrice = RoundTo2(item.CurrentPrice),
                    UnrealizedPnl = RoundTo2(unrealizedPnl),
                    StopLoss = stopLoss,
                    TakeProfit = takeProfit,
                    Confidence = confidence,
                    Sector = InferSector(item.Position.Ticker),
                    Currency = item.Position.Currency,
                    AllocationPercent = totalMarketValue > 0m
                        ? RoundTo2((item.MarketValue / totalMarketValue) * 100m)
                        : 0m,
                    LastUpdatedUtc = latestSnapshotsByTicker.TryGetValue(item.Position.Ticker, out var snapshot)
                        ? snapshot.LastUpdatedUtc
                        : item.Position.UpdatedAtUtc
                };
            })
            .OrderByDescending(position => position.AllocationPercent)
            .ThenBy(position => position.Symbol)
            .ToList();
    }

    private static IEnumerable<WatchlistOpportunity> BuildWatchlist(
        IReadOnlyCollection<PortfolioPosition> positions,
        IReadOnlyCollection<StrategySignalRecord> signals,
        IReadOnlyDictionary<string, List<MarketSnapshot>> snapshotsByTicker,
        IReadOnlyDictionary<string, MarketSnapshot> latestSnapshotsByTicker)
    {
        var positionLookup = positions.ToDictionary(position => position.Ticker, StringComparer.OrdinalIgnoreCase);

        return signals
            .GroupBy(signal => signal.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var latestSignal = group.OrderByDescending(signal => signal.CreatedUtc).First();
                var signedScore = CalculateSignedScore(latestSignal.Signal);
                var confidence = CalculateConfidence(latestSignal.Signal);
                var latestPrice = latestSnapshotsByTicker.TryGetValue(latestSignal.Ticker, out var snapshot)
                    ? snapshot.Price
                    : positionLookup.TryGetValue(latestSignal.Ticker, out var position)
                        ? position.AveragePrice
                        : 0m;
                var volatility = snapshotsByTicker.TryGetValue(latestSignal.Ticker, out var tickerSnapshots)
                    ? CalculateVolatilityPercent(tickerSnapshots)
                    : 0m;

                return new WatchlistOpportunity
                {
                    Symbol = latestSignal.Ticker,
                    Rating = GetRating(signedScore),
                    Confidence = confidence,
                    ForecastReturn = RoundTo2(latestSignal.Signal * 8m),
                    RiskScore = RoundTo2(Math.Min(100m, 20m + volatility)),
                    Price = RoundTo2(latestPrice),
                    Sector = InferSector(latestSignal.Ticker),
                    Strategy = ChooseStrategy(latestSignal.Signal, volatility),
                    TopFactors = BuildTopFactors(latestSignal.Signal, volatility),
                    LastUpdatedUtc = latestSignal.CreatedUtc
                };
            })
            .OrderByDescending(opportunity => opportunity.Confidence)
            .ThenByDescending(opportunity => opportunity.ForecastReturn);
    }

    private static IEnumerable<MarketOverviewCard> BuildMarketOverview(
        IReadOnlyDictionary<string, List<MarketSnapshot>> snapshotsByTicker)
    {
        var emittedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var benchmark in MarketBenchmarks)
        {
            if (!snapshotsByTicker.TryGetValue(benchmark.Symbol, out var snapshots) || snapshots.Count == 0)
            {
                continue;
            }

            if (!emittedLabels.Add(benchmark.Label))
            {
                continue;
            }

            var latest = snapshots[^1];
            var previous = snapshots.Count > 1 ? snapshots[^2] : latest;
            var changePercent = previous.Price == 0m
                ? 0m
                : RoundTo2(((latest.Price - previous.Price) / previous.Price) * 100m);

            yield return new MarketOverviewCard
            {
                Symbol = benchmark.Symbol,
                Label = benchmark.Label,
                Value = RoundTo2(latest.Price),
                ChangePercent = changePercent,
                Trend = changePercent >= 0m ? "up" : "down",
                LastUpdatedUtc = latest.LastUpdatedUtc
            };
        }
    }

    private static IEnumerable<DashboardAlert> BuildAlerts(
        IReadOnlyCollection<PortfolioPositionView> positions,
        IReadOnlyCollection<WatchlistOpportunity> watchlist,
        IReadOnlyCollection<MarketSnapshot> snapshots,
        IReadOnlyCollection<StrategySignalRecord> signals)
    {
        var now = DateTimeOffset.UtcNow;
        var alerts = new List<DashboardAlert>();

        var staleCutoff = now.AddMinutes(-15);
        var latestSnapshot = snapshots.OrderByDescending(snapshot => snapshot.LastUpdatedUtc).FirstOrDefault();
        if (latestSnapshot is null || latestSnapshot.LastUpdatedUtc < staleCutoff)
        {
            alerts.Add(new DashboardAlert
            {
                Title = "API/Data Source Issue",
                Message = latestSnapshot is null
                    ? "No market snapshots are available yet. Awaiting the first market data refresh."
                    : $"Latest market snapshot is stale from {latestSnapshot.LastUpdatedUtc:u}.",
                Type = "data",
                Severity = "warning",
                TimeUtc = latestSnapshot?.LastUpdatedUtc ?? now
            });
        }

        foreach (var position in positions.Where(position => position.CurrentPrice > 0m))
        {
            var drawdownPercent = position.EntryPrice == 0m
                ? 0m
                : ((position.CurrentPrice - position.EntryPrice) / position.EntryPrice) * 100m;
            if (drawdownPercent <= -5m)
            {
                alerts.Add(new DashboardAlert
                {
                    Title = "Risk Warning",
                    Message = $"{position.Symbol} is down {RoundTo2(drawdownPercent)}% from entry and should be reviewed against stop discipline.",
                    Type = "risk",
                    Severity = "critical",
                    TimeUtc = position.LastUpdatedUtc
                });
            }
        }

        foreach (var opportunity in watchlist.Where(opportunity => opportunity.Confidence >= 80).Take(3))
        {
            alerts.Add(new DashboardAlert
            {
                Title = "High-conviction setup",
                Message = $"{opportunity.Symbol} is ranked {opportunity.Rating} with {opportunity.Confidence}% confidence and {opportunity.ForecastReturn}% forecast return.",
                Type = "signal",
                Severity = "info",
                TimeUtc = opportunity.LastUpdatedUtc
            });
        }

        foreach (var signal in signals.Take(2))
        {
            alerts.Add(new DashboardAlert
            {
                Title = "Strategy update",
                Message = $"{signal.Ticker} produced a {signal.Side.ToLowerInvariant()} signal of {RoundTo2(signal.Signal)} using {signal.OrderType}.",
                Type = "signal",
                Severity = Math.Abs(signal.Signal) >= 1m ? "warning" : "info",
                TimeUtc = signal.CreatedUtc
            });
        }

        return alerts
            .OrderByDescending(alert => alert.TimeUtc)
            .ThenBy(alert => alert.Title)
            .Take(8);
    }

    private static List<DashboardInsight> BuildInsights(
        IReadOnlyList<WatchlistOpportunity> watchlist,
        IReadOnlyList<MarketOverviewCard> marketOverview,
        IReadOnlyList<DashboardAlert> alerts)
    {
        var insights = new List<DashboardInsight>();
        var topBuy = watchlist
            .Where(opportunity => opportunity.ForecastReturn > 0m)
            .OrderByDescending(opportunity => opportunity.Confidence)
            .FirstOrDefault();
        var topSell = watchlist
            .Where(opportunity => opportunity.ForecastReturn < 0m)
            .OrderBy(opportunity => opportunity.ForecastReturn)
            .FirstOrDefault();
        var warningCount = alerts.Count(alert => !string.Equals(alert.Severity, "info", StringComparison.OrdinalIgnoreCase));
        var dominantSector = watchlist
            .GroupBy(opportunity => opportunity.Sector, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        var advancingBenchmarks = marketOverview.Count(card => card.ChangePercent >= 0m);

        if (topBuy is not null)
        {
            insights.Add(new DashboardInsight
            {
                Title = "Top buy opportunity",
                Summary = $"{topBuy.Symbol} leads the watchlist with {topBuy.Confidence}% confidence, a {topBuy.ForecastReturn}% projected return, and strength in {string.Join(", ", topBuy.TopFactors.Take(2))}.",
                Type = "buy",
                Symbol = topBuy.Symbol
            });
        }

        if (topSell is not null)
        {
            insights.Add(new DashboardInsight
            {
                Title = "Top sell opportunity",
                Summary = $"{topSell.Symbol} is the weakest signal on the board with {topSell.ForecastReturn}% projected downside and a {topSell.RiskScore} risk score.",
                Type = "sell",
                Symbol = topSell.Symbol
            });
        }

        insights.Add(new DashboardInsight
        {
            Title = "Emerging risks",
            Summary = warningCount == 0
                ? "No active risk or data warnings are currently elevated."
                : $"{warningCount} alerts require attention across risk controls or data freshness.",
            Type = "risk"
        });

        insights.Add(new DashboardInsight
        {
            Title = "Sector rotation analysis",
            Summary = string.IsNullOrWhiteSpace(dominantSector)
                ? "Sector rotation will populate once watchlist signals are available."
                : $"{dominantSector} is currently contributing the largest share of ranked opportunities.",
            Type = "macro"
        });

        insights.Add(new DashboardInsight
        {
            Title = "Macro outlook",
            Summary = marketOverview.Count == 0
                ? "Macro benchmark snapshots are not available yet."
                : $"{advancingBenchmarks} of {marketOverview.Count} tracked benchmarks are trading higher on the latest update.",
            Type = "macro"
        });

        return insights;
    }

    private static List<PortfolioChartPoint> BuildPortfolioHistory(
        IReadOnlyCollection<PortfolioPosition> positions,
        IReadOnlyCollection<MarketSnapshot> snapshots)
    {
        if (positions.Count == 0)
        {
            return [];
        }

        var quantityByTicker = positions.ToDictionary(position => position.Ticker, position => position.Quantity, StringComparer.OrdinalIgnoreCase);
        var latestPrices = positions.ToDictionary(position => position.Ticker, position => position.AveragePrice, StringComparer.OrdinalIgnoreCase);
        var relevantSnapshots = snapshots
            .Where(snapshot => quantityByTicker.ContainsKey(snapshot.Ticker))
            .OrderBy(snapshot => snapshot.LastUpdatedUtc)
            .ToList();

        if (relevantSnapshots.Count == 0)
        {
            return
            [
                new PortfolioChartPoint
                {
                    Label = positions.Max(position => position.UpdatedAtUtc).ToString("MMM d"),
                    Value = RoundTo2(positions.Sum(position => position.Quantity * position.AveragePrice)),
                    TimestampUtc = positions.Max(position => position.UpdatedAtUtc)
                }
            ];
        }

        var history = new List<PortfolioChartPoint>();
        foreach (var group in relevantSnapshots.GroupBy(snapshot => snapshot.LastUpdatedUtc))
        {
            foreach (var snapshot in group)
            {
                latestPrices[snapshot.Ticker] = snapshot.Price;
            }

            var portfolioValue = positions.Sum(position => position.Quantity * latestPrices[position.Ticker]);
            history.Add(new PortfolioChartPoint
            {
                Label = group.Key.ToString("MMM d"),
                Value = RoundTo2(portfolioValue),
                TimestampUtc = group.Key
            });
        }

        return history;
    }

    private static List<PortfolioChartPoint> Downsample(IReadOnlyList<PortfolioChartPoint> points, int targetCount)
    {
        if (points.Count <= targetCount)
        {
            return [.. points];
        }

        var step = (decimal)(points.Count - 1) / (targetCount - 1);
        var sampled = new List<PortfolioChartPoint>();

        for (var index = 0; index < targetCount; index++)
        {
            var pointIndex = (int)Math.Round(index * step, MidpointRounding.AwayFromZero);
            sampled.Add(points[Math.Min(pointIndex, points.Count - 1)]);
        }

        return sampled;
    }

    private static List<PortfolioChartPoint> BuildDrawdownHistory(IReadOnlyList<PortfolioChartPoint> history)
    {
        var peak = 0m;
        var drawdowns = new List<PortfolioChartPoint>(history.Count);

        foreach (var point in history)
        {
            peak = Math.Max(peak, point.Value);
            var drawdownPercent = peak == 0m ? 0m : RoundTo2(((point.Value - peak) / peak) * 100m);
            drawdowns.Add(new PortfolioChartPoint
            {
                Label = point.Label,
                Value = drawdownPercent,
                TimestampUtc = point.TimestampUtc
            });
        }

        return drawdowns;
    }

    private static List<PortfolioChartPoint> BuildMonthlyReturns(IReadOnlyList<PortfolioChartPoint> history)
    {
        return history
            .GroupBy(point => new { point.TimestampUtc.Year, point.TimestampUtc.Month })
            .Select(group =>
            {
                var ordered = group.OrderBy(point => point.TimestampUtc).ToList();
                var first = ordered[0].Value;
                var last = ordered[^1].Value;
                var returnPercent = first == 0m ? 0m : RoundTo2(((last - first) / first) * 100m);

                return new PortfolioChartPoint
                {
                    Label = new DateTime(ordered[0].TimestampUtc.Year, ordered[0].TimestampUtc.Month, 1).ToString("MMM"),
                    Value = returnPercent,
                    TimestampUtc = ordered[^1].TimestampUtc
                };
            })
            .ToList();
    }

    private static List<PortfolioAllocation> BuildAssetAllocation(IReadOnlyList<PortfolioPositionView> positions)
    {
        return positions
            .Select((position, index) => new PortfolioAllocation
            {
                Name = position.Symbol,
                Value = RoundTo2(position.AllocationPercent),
                Color = AllocationColors[index % AllocationColors.Length]
            })
            .ToList();
    }

    private static List<PortfolioAllocation> BuildSectorAllocation(IReadOnlyList<PortfolioPositionView> positions)
    {
        return positions
            .GroupBy(position => position.Sector, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new PortfolioAllocation
            {
                Name = group.Key,
                Value = RoundTo2(group.Sum(position => position.AllocationPercent)),
                Color = AllocationColors[index % AllocationColors.Length]
            })
            .OrderByDescending(allocation => allocation.Value)
            .ToList();
    }

    private static List<ForecastProjection> BuildForecasts(IReadOnlyList<WatchlistOpportunity> watchlist)
    {
        return watchlist
            .Take(5)
            .SelectMany(opportunity => ProjectionHorizons.Select(horizon => new ForecastProjection
            {
                Symbol = opportunity.Symbol,
                Horizon = horizon,
                ProjectedReturnPercent = RoundTo2(opportunity.ForecastReturn * ProjectionMultiplier(horizon)),
                ProjectedPrice = opportunity.Price <= 0m
                    ? 0m
                    : RoundTo2(opportunity.Price * (1m + ((opportunity.ForecastReturn * ProjectionMultiplier(horizon)) / 100m)))
            }))
            .ToList();
    }

    private static decimal CalculatePeriodPnL(IReadOnlyList<PortfolioChartPoint> history, TimeSpan lookback)
    {
        if (history.Count == 0)
        {
            return 0m;
        }

        var latest = history[^1];
        var targetTime = latest.TimestampUtc - lookback;
        var comparison = history.LastOrDefault(point => point.TimestampUtc <= targetTime) ?? history[0];
        return RoundTo2(latest.Value - comparison.Value);
    }

    private static decimal MaxDrawdownPercent(IReadOnlyList<PortfolioChartPoint> history)
    {
        var peak = 0m;
        var worstDrawdown = 0m;

        foreach (var point in history)
        {
            peak = Math.Max(peak, point.Value);
            if (peak == 0m)
            {
                continue;
            }

            var drawdown = ((point.Value - peak) / peak) * 100m;
            if (drawdown < worstDrawdown)
            {
                worstDrawdown = drawdown;
            }
        }

        return RoundTo2(worstDrawdown);
    }

    private static int CalculateConfidence(decimal signal)
    {
        var confidence = 50m + Math.Min(45m, Math.Abs(signal) * 30m);
        return (int)Math.Round(confidence, MidpointRounding.AwayFromZero);
    }

    private static int CalculateSignedScore(decimal signal)
    {
        var signedScore = 50m + (signal * 25m);
        return (int)Math.Clamp(Math.Round(signedScore, MidpointRounding.AwayFromZero), 0m, 100m);
    }

    private static string GetRating(int score)
    {
        return score switch
        {
            < 40 => "Strong Sell",
            < 55 => "Sell",
            < 65 => "Hold",
            < 80 => "Buy",
            _ => "Strong Buy"
        };
    }

    private static decimal CalculateVolatilityPercent(IReadOnlyList<MarketSnapshot> snapshots)
    {
        if (snapshots.Count < 2)
        {
            return 0m;
        }

        var prices = snapshots.Select(snapshot => snapshot.Price).ToList();
        var average = prices.Average();
        if (average == 0m)
        {
            return 0m;
        }

        var variance = prices.Average(price => Math.Pow((double)(price - average), 2));
        return RoundTo2((decimal)Math.Sqrt(variance) / average * 100m);
    }

    private static string ChooseStrategy(decimal signal, decimal volatilityPercent)
    {
        if (signal > 0.75m)
        {
            return "Trend Following";
        }

        if (signal < -0.75m)
        {
            return "Mean Reversion";
        }

        if (volatilityPercent > 2.5m)
        {
            return "Earnings Surprise";
        }

        return "Momentum";
    }

    private static List<string> BuildTopFactors(decimal signal, decimal volatilityPercent)
    {
        var factors = new List<string>();

        if (signal > 0.5m)
        {
            factors.Add("Positive multi-strategy signal");
        }
        else if (signal < -0.5m)
        {
            factors.Add("Negative momentum unwind");
        }
        else
        {
            factors.Add("Neutral setup awaiting confirmation");
        }

        factors.Add(volatilityPercent > 2.5m ? "Elevated volatility" : "Contained volatility");
        factors.Add(Math.Abs(signal) >= 1m ? "High conviction" : "Incremental conviction");

        return factors;
    }

    private static decimal ProjectionMultiplier(string horizon)
    {
        return horizon switch
        {
            "1D" => 0.25m,
            "5D" => 0.6m,
            "30D" => 1m,
            "90D" => 1.4m,
            _ => 1m
        };
    }

    private static string InferSector(string symbol)
    {
        var normalized = symbol.Split('_')[0].ToUpperInvariant();

        return normalized switch
        {
            "NVDA" or "MSFT" or "AAPL" or "AMD" or "META" or "GOOGL" => "Technology",
            "V" or "MA" or "JPM" or "GS" => "Financials",
            "LLY" or "JNJ" or "PFE" => "Healthcare",
            "XOM" or "CVX" => "Energy",
            "PG" or "KO" or "PEP" => "Consumer",
            "SPX" or "^GSPC" or "IXIC" or "^IXIC" or "DJI" or "^DJI" or "FTSE" or "^FTSE" or "VIX" or "^VIX" => "Market",
            _ => "Unclassified"
        };
    }

    private static DateTimeOffset GetLastUpdatedUtc(DashboardContext dashboardContext)
    {
        return new[]
            {
                dashboardContext.Positions.Count > 0 ? dashboardContext.Positions.Max(position => position.UpdatedAtUtc) : DateTimeOffset.MinValue,
                dashboardContext.Snapshots.Count > 0 ? dashboardContext.Snapshots.Max(snapshot => snapshot.LastUpdatedUtc) : DateTimeOffset.MinValue,
                dashboardContext.Signals.Count > 0 ? dashboardContext.Signals.Max(signal => signal.CreatedUtc) : DateTimeOffset.MinValue
            }
            .Max();
    }

    private static decimal RoundTo2(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private sealed record DashboardContext(
        List<PortfolioPosition> Positions,
        List<MarketSnapshot> Snapshots,
        List<StrategySignalRecord> Signals,
        IReadOnlyDictionary<string, StrategySignalRecord> LatestSignalsByTicker,
        IReadOnlyDictionary<string, List<MarketSnapshot>> SnapshotsByTicker,
        IReadOnlyDictionary<string, MarketSnapshot> LatestSnapshotsByTicker);
}
