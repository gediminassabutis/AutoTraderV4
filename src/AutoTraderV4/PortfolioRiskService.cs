namespace AutoTraderV4;

public sealed class PortfolioRiskPolicy
{
    public decimal MaxPortfolioExposurePct { get; set; } = 95m;
    public decimal MinCashReservePct { get; set; } = 5m;
    public decimal MaxPositionPct { get; set; } = 5m;
    public decimal MaxSectorExposurePct { get; set; } = 20m;
    public decimal MaxDailyLossPct { get; set; } = 2m;
    public decimal MaxPortfolioDrawdownPct { get; set; } = 10m;
}

public sealed class TradeRiskRequest
{
    public decimal PortfolioValue { get; set; }
    public decimal AvailableCash { get; set; }
    public decimal ProposedPositionValue { get; set; }
    public decimal ExistingExposureValue { get; set; }
    public decimal SectorExposureValue { get; set; }
    public decimal DailyPortfolioLoss { get; set; }
    public decimal PortfolioDrawdownPct { get; set; }
}

public class TradeReductionPlan
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

public class RiskCheckResult
{
    public bool Allowed { get; set; }
    public bool DefensiveMode { get; set; }
    public decimal ExposurePercent { get; set; }
    public decimal CashReservePercent { get; set; }
    public decimal ProposedPositionPercent { get; set; }
    public TradeReductionPlan? ReductionPlan { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class PortfolioRiskAssessment
{
    public bool IsAllowed { get; set; }
    public bool DefensiveMode { get; set; }
    public decimal PortfolioValue { get; set; }
    public decimal AvailableCash { get; set; }
    public decimal ProposedPositionValue { get; set; }
    public decimal ProposedPositionPct { get; set; }
    public decimal ExposureAfterTradePct { get; set; }
    public decimal ExposurePercent { get; set; }
    public decimal CashReservePercent { get; set; }
    public List<string> Violations { get; } = new();
    public List<string> Warnings { get; } = new();
    public TradeReductionPlan? ReductionPlan { get; set; }
}

public sealed class PortfolioRiskService
{
    private readonly PortfolioRiskPolicy _policy;

    public PortfolioRiskService() : this(new PortfolioRiskPolicy())
    {
    }

    public PortfolioRiskService(PortfolioRiskPolicy policy)
    {
        _policy = policy ?? new PortfolioRiskPolicy();
    }

    public PortfolioRiskAssessment EvaluateTrade(TradeRiskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EvaluateTrade(
            request.PortfolioValue,
            request.AvailableCash,
            request.ProposedPositionValue,
            request.ExistingExposureValue,
            request.SectorExposureValue,
            request.DailyPortfolioLoss,
            request.PortfolioDrawdownPct);
    }

    public PortfolioRiskAssessment EvaluateTrade(
        decimal portfolioValue,
        decimal availableCash,
        decimal proposedPositionValue,
        decimal existingExposureValue = 0m,
        decimal sectorExposureValue = 0m,
        decimal dailyPortfolioLoss = 0m,
        decimal portfolioDrawdownPct = 0m)
    {
        if (portfolioValue <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(portfolioValue), "Portfolio value must be greater than zero.");
        }

        var projectedCashValue = availableCash - proposedPositionValue;
        var projectedExposureValue = existingExposureValue + proposedPositionValue;
        var proposedPositionPct = proposedPositionValue / portfolioValue * 100m;
        var projectedExposurePct = projectedExposureValue / portfolioValue * 100m;
        var projectedCashPct = projectedCashValue / portfolioValue * 100m;
        var projectedSectorPct = (sectorExposureValue + proposedPositionValue) / portfolioValue * 100m;
        var dailyLossPct = (dailyPortfolioLoss / portfolioValue) * 100m;

        var assessment = new PortfolioRiskAssessment
        {
            PortfolioValue = portfolioValue,
            AvailableCash = availableCash,
            ProposedPositionValue = proposedPositionValue,
            ProposedPositionPct = proposedPositionPct,
            ExposureAfterTradePct = projectedExposurePct
        };

        var minimumCash = portfolioValue * _policy.MinCashReservePct / 100m;
        if (availableCash < minimumCash)
        {
            assessment.Violations.Add($"Available cash is below the { _policy.MinCashReservePct }% reserve requirement.");
        }

        var maxPosition = portfolioValue * _policy.MaxPositionPct / 100m;
        if (proposedPositionValue > maxPosition)
        {
            assessment.Violations.Add($"Proposed position exceeds the {_policy.MaxPositionPct}% maximum position size.");
        }

        var maxExposure = portfolioValue * _policy.MaxPortfolioExposurePct / 100m;
        if (projectedExposureValue > maxExposure)
        {
            assessment.Violations.Add($"Portfolio exposure would exceed the { _policy.MaxPortfolioExposurePct }% limit.");
        }

        var maxSectorExposure = portfolioValue * _policy.MaxSectorExposurePct / 100m;
        if (projectedSectorPct > _policy.MaxSectorExposurePct)
        {
            assessment.Violations.Add($"Sector concentration would exceed the { _policy.MaxSectorExposurePct }% cap.");
        }

        if (dailyLossPct < -_policy.MaxDailyLossPct)
        {
            assessment.Violations.Add($"Daily portfolio loss is outside the {_policy.MaxDailyLossPct}% limit.");
        }

        if (projectedCashPct < _policy.MinCashReservePct)
        {
            assessment.Violations.Add($"Projected cash reserve {projectedCashPct:F2}% is below the {_policy.MinCashReservePct:F2}% minimum.");
        }

        if (portfolioDrawdownPct > _policy.MaxPortfolioDrawdownPct)
        {
            assessment.DefensiveMode = true;
            assessment.Warnings.Add($"Portfolio drawdown exceeds the {_policy.MaxPortfolioDrawdownPct}% guardrail.");
        }

        assessment.ReductionPlan = BuildTradeReductionPlan(
            portfolioValue,
            availableCash,
            proposedPositionValue,
            existingExposureValue,
            sectorExposureValue,
            dailyPortfolioLoss,
            portfolioDrawdownPct);

        if (assessment.ReductionPlan.RequiresReduction)
        {
            assessment.Warnings.AddRange(assessment.ReductionPlan.Reasons);
        }

        assessment.IsAllowed = assessment.Violations.Count == 0 && !assessment.DefensiveMode && !assessment.ReductionPlan.RequiresReduction;
        assessment.DefensiveMode = assessment.DefensiveMode || assessment.Violations.Count > 0 || assessment.ReductionPlan.RequiresReduction;

        return assessment;
    }

    public TradeReductionPlan BuildTradeReductionPlan(
        decimal portfolioValue,
        decimal availableCash,
        decimal proposedPositionValue,
        decimal existingExposureValue = 0m,
        decimal sectorExposureValue = 0m,
        decimal dailyPortfolioLoss = 0m,
        decimal portfolioDrawdownPct = 0m,
        OrderSide side = OrderSide.Buy)
    {
        if (portfolioValue <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(portfolioValue), "Portfolio value must be greater than zero.");
        }

        var maxPositionValue = portfolioValue * _policy.MaxPositionPct / 100m;
        var maxExposureValue = portfolioValue * _policy.MaxPortfolioExposurePct / 100m;
        var maxSectorValue = portfolioValue * _policy.MaxSectorExposurePct / 100m;
        var minimumCashValue = portfolioValue * _policy.MinCashReservePct / 100m;
        var requiredReduction = 0m;
        var reasons = new List<string>();

        if (proposedPositionValue > maxPositionValue)
        {
            var reduction = proposedPositionValue - maxPositionValue;
            requiredReduction = Math.Max(requiredReduction, reduction);
            reasons.Add($"Reduce position size by {reduction:F2} to stay within the {_policy.MaxPositionPct}% cap.");
        }

        if (existingExposureValue + proposedPositionValue > maxExposureValue)
        {
            var reduction = (existingExposureValue + proposedPositionValue) - maxExposureValue;
            requiredReduction = Math.Max(requiredReduction, reduction);
            reasons.Add($"Reduce exposure by {reduction:F2} to respect the {_policy.MaxPortfolioExposurePct}% portfolio limit.");
        }

        if ((sectorExposureValue + proposedPositionValue) > maxSectorValue)
        {
            var reduction = (sectorExposureValue + proposedPositionValue) - maxSectorValue;
            requiredReduction = Math.Max(requiredReduction, reduction);
            reasons.Add($"Reduce sector concentration by {reduction:F2} to remain below the {_policy.MaxSectorExposurePct}% sector cap.");
        }

        if ((availableCash - proposedPositionValue) < minimumCashValue)
        {
            var reduction = minimumCashValue - (availableCash - proposedPositionValue);
            requiredReduction = Math.Max(requiredReduction, reduction);
            reasons.Add($"Raise cash by {reduction:F2} to maintain the {_policy.MinCashReservePct}% reserve requirement.");
        }

        if (dailyPortfolioLoss / portfolioValue * 100m < -_policy.MaxDailyLossPct)
        {
            reasons.Add($"Daily loss is below the {_policy.MaxDailyLossPct}% threshold; enter defensive mode.");
        }

        if (portfolioDrawdownPct > _policy.MaxPortfolioDrawdownPct)
        {
            reasons.Add($"Portfolio drawdown is above the {_policy.MaxPortfolioDrawdownPct}% threshold; reduce exposure.");
        }

        var reducedTradeValue = Math.Max(0m, proposedPositionValue - requiredReduction);
        var reducedCashValue = Math.Max(0m, availableCash - reducedTradeValue);
        var plan = new TradeReductionPlan
        {
            RequiresReduction = requiredReduction > 0m,
            OriginalTradeValue = proposedPositionValue,
            ReducedTradeValue = reducedTradeValue,
            ReducedQuantity = reducedTradeValue,
            PositionPercentAfterReduction = portfolioValue > 0m ? (reducedTradeValue / portfolioValue) * 100m : 0m,
            ExposurePercentAfterReduction = portfolioValue > 0m ? ((existingExposureValue + reducedTradeValue) / portfolioValue) * 100m : 0m,
            CashReservePercentAfterReduction = portfolioValue > 0m ? (reducedCashValue / portfolioValue) * 100m : 0m
        };

        foreach (var reason in reasons)
        {
            plan.Reasons.Add(reason);
        }

        return plan;
    }

    public RiskCheckResult Evaluate(global::AutoTraderV4.Services.PortfolioDashboard dashboard, TradeDecision? decision = null)
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

        if (exposurePercent >= _policy.MaxPortfolioExposurePct)
        {
            defensiveMode = true;
            warnings.Add("Portfolio exposure exceeds the 95% cap.");
        }

        if (cashReservePercent < _policy.MinCashReservePct)
        {
            defensiveMode = true;
            warnings.Add("Cash reserve has fallen below the 5% minimum.");
        }

        if (dailyLossPercent < -_policy.MaxDailyLossPct)
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

            if (proposedPositionPercent > _policy.MaxPositionPct)
            {
                warnings.Add("This trade would exceed the 5% max position size limit.");
            }

            if (projectedExposurePercent > _policy.MaxPortfolioExposurePct)
            {
                warnings.Add("This trade would exceed the 95% portfolio exposure cap.");
            }

            if (projectedCashReservePercent < _policy.MinCashReservePct)
            {
                warnings.Add("This trade would violate the 5% cash reserve minimum.");
            }

            var sectorExposureValue = dashboard.Positions
                .Where(p => !string.IsNullOrWhiteSpace(decision.Sector) && string.Equals(p.Sector, decision.Sector, StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Quantity * p.CurrentPrice);

            var projectedSectorExposure = ((sectorExposureValue + (decision.Side == OrderSide.Buy ? tradeValue : -tradeValue)) / totalPortfolioValue) * 100m;
            if (projectedSectorExposure > _policy.MaxSectorExposurePct)
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
        var result = new RiskCheckResult
        {
            Allowed = allowed,
            DefensiveMode = defensiveMode || !allowed || reductionPlan.RequiresReduction,
            ExposurePercent = exposurePercent,
            CashReservePercent = cashReservePercent,
            ProposedPositionPercent = decision is null ? 0m : (Math.Abs(decision.Quantity * decision.EntryPrice) / totalPortfolioValue) * 100m,
            ReductionPlan = reductionPlan,
            Warnings = warnings
        };

        return result;
    }

}
