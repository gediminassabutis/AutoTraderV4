namespace AutoTraderV4.Services;

public sealed class RiskCheckResult
{
    public bool Allowed { get; set; }
    public bool DefensiveMode { get; set; }
    public decimal ExposurePercent { get; set; }
    public decimal CashReservePercent { get; set; }
    public decimal ProposedPositionPercent { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class PortfolioRiskService
{
    private const decimal MaxPortfolioExposurePercent = 95m;
    private const decimal MinimumCashReservePercent = 5m;
    private const decimal MaxPositionSizePercent = 5m;
    private const decimal MaxSectorExposurePercent = 20m;
    private const decimal MaxDailyLossPercent = 2m;
    private const decimal MaxDrawdownPercent = 10m;

    public RiskCheckResult Evaluate(PortfolioDashboard dashboard, TradeDecision? decision = null)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        var totalPortfolioValue = dashboard.Summary.TotalPortfolioValue;
        var cashReservePercent = (dashboard.Summary.AvailableCash / totalPortfolioValue) * 100m;
        var exposurePercent = dashboard.Summary.TotalExposurePercent;
        var warnings = new List<string>();
        var defensiveMode = false;

        if (exposurePercent >= MaxPortfolioExposurePercent)
        {
            defensiveMode = true;
            warnings.Add("Portfolio exposure exceeds the 95% cap.");
        }

        if (cashReservePercent < MinimumCashReservePercent)
        {
            defensiveMode = true;
            warnings.Add("Cash reserve has fallen below the 5% minimum.");
        }

        if (dashboard.Summary.DailyPnL < -MaxDailyLossPercent)
        {
            defensiveMode = true;
            warnings.Add("Daily drawdown is beyond the 2% limit.");
        }

        if (decision is not null)
        {
            var proposedValue = decision.Quantity * decision.EntryPrice;
            var proposedPositionPercent = (proposedValue / totalPortfolioValue) * 100m;

            if (proposedPositionPercent > MaxPositionSizePercent)
            {
                warnings.Add("This trade would exceed the 5% max position size limit.");
            }

            if (decision.Ticker is not null && dashboard.Positions.Any(p => p.Symbol == decision.Ticker))
            {
                var currentExposure = dashboard.Positions.Where(p => p.Symbol == decision.Ticker).Sum(p => p.Quantity * p.CurrentPrice);
                var combinedExposurePercent = ((currentExposure + proposedValue) / totalPortfolioValue) * 100m;
                if (combinedExposurePercent > MaxSectorExposurePercent)
                {
                    warnings.Add("This trade would exceed the sector exposure cap.");
                }
            }
        }

        var allowed = warnings.Count == 0;
        return new RiskCheckResult
        {
            Allowed = allowed,
            DefensiveMode = defensiveMode || !allowed,
            ExposurePercent = exposurePercent,
            CashReservePercent = cashReservePercent,
            ProposedPositionPercent = decision is null ? 0m : (decision.Quantity * decision.EntryPrice / totalPortfolioValue) * 100m,
            Warnings = warnings
        };
    }
}
