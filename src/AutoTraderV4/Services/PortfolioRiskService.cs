namespace AutoTraderV4.Services;

public sealed class TradeReductionPlan
{
    public bool RequiresReduction { get; set; }
    public decimal OriginalTradeValue { get; set; }
    public decimal ReducedTradeValue { get; set; }
    public decimal ReducedQuantity { get; set; }
    public decimal PositionPercentAfterReduction { get; set; }
    public decimal ExposurePercentAfterReduction { get; set; }
    public decimal CashReservePercentAfterReduction { get; set; }
    public List<string> Reasons { get; } = [];
}

public sealed class RiskCheckResult
{
    public bool Allowed { get; set; }
    public bool DefensiveMode { get; set; }
    public decimal ExposurePercent { get; set; }
    public decimal CashReservePercent { get; set; }
    public decimal ProposedPositionPercent { get; set; }
    public TradeReductionPlan? ReductionPlan { get; set; }
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
        if (totalPortfolioValue <= 0m)
        {
            return new RiskCheckResult
            {
                Allowed = false,
                DefensiveMode = true,
                ExposurePercent = dashboard.Summary.TotalExposurePercent,
                CashReservePercent = 0m,
                ProposedPositionPercent = 0m,
                Warnings = ["Portfolio value is unavailable; risk governance cannot validate the trade."]
            };
        }

        var cashReservePercent = (dashboard.Summary.AvailableCash / totalPortfolioValue) * 100m;
        var exposurePercent = dashboard.Summary.TotalExposurePercent;
        var warnings = new List<string>();
        var defensiveMode = dashboard.Summary.DefensiveMode;
        var dailyLossPercent = (dashboard.Summary.DailyPnL / totalPortfolioValue) * 100m;
        var reductionPlan = new TradeReductionPlan();

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

        if (dailyLossPercent < -MaxDailyLossPercent)
        {
            defensiveMode = true;
            warnings.Add("Daily drawdown is beyond the 2% limit.");
        }

        if (decision is not null)
        {
            var tradeValue = Math.Abs(decision.Quantity * decision.EntryPrice);
            var proposedPositionPercent = (tradeValue / totalPortfolioValue) * 100m;
            var currentExposureValue = exposurePercent / 100m * totalPortfolioValue;
            var projectedExposureValue = currentExposureValue + (decision.Side == OrderSide.Sell ? -tradeValue : tradeValue);
            var projectedExposurePercent = (projectedExposureValue / totalPortfolioValue) * 100m;
            var projectedCashValue = dashboard.Summary.AvailableCash - (decision.Side == OrderSide.Buy ? tradeValue : -tradeValue);
            var projectedCashReservePercent = (projectedCashValue / totalPortfolioValue) * 100m;

            if (proposedPositionPercent > MaxPositionSizePercent)
            {
                warnings.Add("This trade would exceed the 5% max position size limit.");
            }

            if (projectedExposurePercent > MaxPortfolioExposurePercent)
            {
                warnings.Add("This trade would exceed the 95% portfolio exposure cap.");
            }

            if (projectedCashReservePercent < MinimumCashReservePercent)
            {
                warnings.Add("This trade would violate the 5% cash reserve minimum.");
            }

            var sectorExposureValue = dashboard.Positions
                .Where(p => !string.IsNullOrWhiteSpace(decision.Sector) && string.Equals(p.Sector, decision.Sector, StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Quantity * p.CurrentPrice);

            var projectedSectorExposure = ((sectorExposureValue + (decision.Side == OrderSide.Buy ? tradeValue : -tradeValue)) / totalPortfolioValue) * 100m;
            if (projectedSectorExposure > MaxSectorExposurePercent)
            {
                warnings.Add("This trade would exceed the sector exposure cap.");
            }

            reductionPlan = BuildTradeReductionPlan(
                totalPortfolioValue,
                dashboard.Summary.AvailableCash,
                tradeValue,
                currentExposureValue,
                sectorExposureValue,
                dashboard.Summary.DailyPnL,
                exposurePercent,
                decision.Side);
        }

        var allowed = warnings.Count == 0 && !reductionPlan.RequiresReduction;
        return new RiskCheckResult
        {
            Allowed = allowed,
            DefensiveMode = defensiveMode || !allowed || reductionPlan.RequiresReduction,
            ExposurePercent = exposurePercent,
            CashReservePercent = cashReservePercent,
            ProposedPositionPercent = decision is null ? 0m : (Math.Abs(decision.Quantity * decision.EntryPrice) / totalPortfolioValue) * 100m,
            ReductionPlan = reductionPlan,
            Warnings = warnings
        };
    }

    public TradeReductionPlan BuildTradeReductionPlan(
        decimal portfolioValue,
        decimal availableCash,
        decimal proposedTradeValue,
        decimal currentExposureValue,
        decimal sectorExposureValue,
        decimal dailyPortfolioLoss,
        decimal currentExposurePercent,
        OrderSide side = OrderSide.Buy)
    {
        if (portfolioValue <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(portfolioValue), "Portfolio value must be greater than zero.");
        }

        var maxPositionValue = portfolioValue * MaxPositionSizePercent / 100m;
        var maxPortfolioValue = portfolioValue * MaxPortfolioExposurePercent / 100m;
        var maxSectorValue = portfolioValue * MaxSectorExposurePercent / 100m;
        var minimumCashValue = portfolioValue * MinimumCashReservePercent / 100m;
        var requiredReduction = 0m;
        var reasons = new List<string>();

        var effectiveExposureValue = currentExposureValue + (side == OrderSide.Buy ? proposedTradeValue : -proposedTradeValue);
        if (proposedTradeValue > maxPositionValue)
        {
            requiredReduction = Math.Max(requiredReduction, proposedTradeValue - maxPositionValue);
            reasons.Add($"Reduce position size by {proposedTradeValue - maxPositionValue:F2} to stay within the {MaxPositionSizePercent}% cap.");
        }

        if (effectiveExposureValue > maxPortfolioValue)
        {
            requiredReduction = Math.Max(requiredReduction, effectiveExposureValue - maxPortfolioValue);
            reasons.Add($"Reduce exposure by {effectiveExposureValue - maxPortfolioValue:F2} to respect the {MaxPortfolioExposurePercent}% portfolio limit.");
        }

        var effectiveSectorExposure = sectorExposureValue + (side == OrderSide.Buy ? proposedTradeValue : -proposedTradeValue);
        if (effectiveSectorExposure > maxSectorValue)
        {
            requiredReduction = Math.Max(requiredReduction, effectiveSectorExposure - maxSectorValue);
            reasons.Add($"Reduce sector concentration by {effectiveSectorExposure - maxSectorValue:F2} to remain below the {MaxSectorExposurePercent}% cap.");
        }

        var projectedCash = availableCash - (side == OrderSide.Buy ? proposedTradeValue : -proposedTradeValue);
        if (projectedCash < minimumCashValue)
        {
            requiredReduction = Math.Max(requiredReduction, minimumCashValue - projectedCash);
            reasons.Add($"Raise cash by {minimumCashValue - projectedCash:F2} to maintain the {MinimumCashReservePercent}% reserve requirement.");
        }

        var dailyLossPercent = (dailyPortfolioLoss / portfolioValue) * 100m;
        if (dailyLossPercent < -MaxDailyLossPercent)
        {
            reasons.Add($"Daily loss is below the {MaxDailyLossPercent}% threshold; enter defensive mode.");
        }

        if (currentExposurePercent > MaxPortfolioExposurePercent)
        {
            reasons.Add($"Portfolio drawdown is above the {MaxDrawdownPercent}% threshold; reduce exposure.");
        }

        var reducedTradeValue = Math.Max(0m, proposedTradeValue - requiredReduction);
        var reducedCashValue = availableCash - (side == OrderSide.Buy ? reducedTradeValue : -reducedTradeValue);
        var plan = new TradeReductionPlan
        {
            RequiresReduction = requiredReduction > 0m,
            OriginalTradeValue = proposedTradeValue,
            ReducedTradeValue = reducedTradeValue,
            ReducedQuantity = reducedTradeValue,
            PositionPercentAfterReduction = (reducedTradeValue / portfolioValue) * 100m,
            ExposurePercentAfterReduction = ((currentExposureValue + (side == OrderSide.Buy ? reducedTradeValue : -reducedTradeValue)) / portfolioValue) * 100m,
            CashReservePercentAfterReduction = (reducedCashValue / portfolioValue) * 100m
        };

        foreach (var reason in reasons)
        {
            plan.Reasons.Add(reason);
        }

        return plan;
    }
}
