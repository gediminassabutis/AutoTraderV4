using AutoTraderV4.Services;

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

public sealed class PortfolioRiskAssessment
{
    public bool IsAllowed { get; set; }
    public bool DefensiveMode { get; set; }
    public decimal PortfolioValue { get; set; }
    public decimal AvailableCash { get; set; }
    public decimal ProposedPositionValue { get; set; }
    public decimal ProposedPositionPct { get; set; }
    public decimal ExposureAfterTradePct { get; set; }
    public List<string> Violations { get; } = new();
    public List<string> Warnings { get; } = new();
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

        var assessment = new PortfolioRiskAssessment
        {
            PortfolioValue = portfolioValue,
            AvailableCash = availableCash,
            ProposedPositionValue = proposedPositionValue,
            ProposedPositionPct = proposedPositionValue / portfolioValue * 100m,
            ExposureAfterTradePct = (existingExposureValue + proposedPositionValue) / portfolioValue * 100m
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
        if (existingExposureValue + proposedPositionValue > maxExposure)
        {
            assessment.Violations.Add($"Portfolio exposure would exceed the { _policy.MaxPortfolioExposurePct }% limit.");
        }

        var maxSectorExposure = portfolioValue * _policy.MaxSectorExposurePct / 100m;
        if (sectorExposureValue + proposedPositionValue > maxSectorExposure)
        {
            assessment.Violations.Add($"Sector concentration would exceed the { _policy.MaxSectorExposurePct }% cap.");
        }

        var dailyLossPct = (dailyPortfolioLoss / portfolioValue) * 100m;
        if (dailyLossPct < -_policy.MaxDailyLossPct)
        {
            assessment.Violations.Add($"Daily portfolio loss is outside the {_policy.MaxDailyLossPct}% limit.");
        }

        if (portfolioDrawdownPct > _policy.MaxPortfolioDrawdownPct)
        {
            assessment.DefensiveMode = true;
            assessment.Warnings.Add($"Portfolio drawdown exceeds the {_policy.MaxPortfolioDrawdownPct}% guardrail.");
        }

        assessment.IsAllowed = assessment.Violations.Count == 0;
        assessment.DefensiveMode = assessment.DefensiveMode || assessment.Violations.Count > 0;

        return assessment;
    }

    public RiskCheckResult Evaluate(PortfolioDashboard dashboard, TradeDecision? decision = null)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        var totalPortfolioValue = dashboard.Summary.TotalPortfolioValue;
        var cashReservePercent = totalPortfolioValue > 0m ? (dashboard.Summary.AvailableCash / totalPortfolioValue) * 100m : 0m;
        var exposurePercent = dashboard.Summary.TotalExposurePercent;
        var warnings = new List<string>();
        var defensiveMode = false;

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

        if (dashboard.Summary.DailyPnL < -_policy.MaxDailyLossPct)
        {
            defensiveMode = true;
            warnings.Add("Daily drawdown is beyond the 2% limit.");
        }

        if (decision is not null)
        {
            var proposedValue = decision.Quantity * decision.EntryPrice;
            var proposedPositionPercent = totalPortfolioValue > 0m ? (proposedValue / totalPortfolioValue) * 100m : 0m;

            if (proposedPositionPercent > _policy.MaxPositionPct)
            {
                warnings.Add("This trade would exceed the 5% max position size limit.");
            }

            if (!string.IsNullOrWhiteSpace(decision.Ticker) && dashboard.Positions.Any(p => p.Symbol == decision.Ticker))
            {
                var currentExposure = dashboard.Positions.Where(p => p.Symbol == decision.Ticker).Sum(p => p.Quantity * p.CurrentPrice);
                var combinedExposurePercent = totalPortfolioValue > 0m ? ((currentExposure + proposedValue) / totalPortfolioValue) * 100m : 0m;
                if (combinedExposurePercent > _policy.MaxSectorExposurePct)
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
