namespace AutoTraderV4;

public sealed class RiskGovernanceService
{
    private const decimal MaximumPortfolioExposurePercent = 95m;
    private const decimal MinimumCashReservePercent = 5m;
    private const decimal MaximumPositionSizePercent = 5m;
    private const decimal MaximumSectorExposurePercent = 20m;
    private const decimal MaximumDailyLossPercent = 2m;
    private const decimal MaximumDrawdownPercent = 10m;
    private const decimal MinimumConfidenceScore = 80m;
    private const decimal MinimumRiskReward = 2.5m;
    private const decimal MinimumLiquidityScore = 60m;
    private const decimal MaximumSpreadPercent = 1m;
    private static readonly TimeSpan MaximumDataAge = TimeSpan.FromMinutes(15);

    private readonly IPortfolioRepository _repository;
    private readonly TimeProvider _timeProvider;

    public RiskGovernanceService(IPortfolioRepository repository, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<RiskAssessmentResult> EvaluateAsync(
        TradeDecision candidateTrade,
        RiskContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateTrade);
        ArgumentNullException.ThrowIfNull(context);

        candidateTrade.Validate();
        var contextErrors = context.Validate();
        if (contextErrors.Count > 0)
        {
            throw new ArgumentException("Risk context is invalid.", nameof(context));
        }

        var state = await _repository.GetPortfolioStateAsync(cancellationToken);
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        return EvaluateInternal(state, positions, candidateTrade, context);
    }

    public RiskAssessmentResult EvaluatePortfolioHealth(
        PortfolioStateRecord? state,
        IReadOnlyCollection<PortfolioPosition> positions)
    {
        return EvaluateInternal(state, positions, candidateTrade: null, context: null);
    }

    private RiskAssessmentResult EvaluateInternal(
        PortfolioStateRecord? state,
        IReadOnlyCollection<PortfolioPosition> positions,
        TradeDecision? candidateTrade,
        RiskContextSnapshot? context)
    {
        var normalizedPositions = positions ?? [];
        var totalValue = ResolveTotalValue(state, normalizedPositions);
        var currentExposureValue = normalizedPositions.Sum(GetMarketValue);
        var currentExposurePercent = Percentage(currentExposureValue, totalValue);
        var cash = state?.Cash ?? 0m;
        var dailyLossPercent = totalValue <= 0m || state is null || state.DailyProfitLoss >= 0m
            ? 0m
            : Math.Round(Math.Abs(state.DailyProfitLoss) / totalValue * 100m, 2, MidpointRounding.AwayFromZero);
        var drawdownPercent = totalValue <= 0m || state is null || state.PeakPortfolioValue <= 0m
            ? 0m
            : Math.Round(Math.Max(0m, (state.PeakPortfolioValue - totalValue) / state.PeakPortfolioValue * 100m), 2, MidpointRounding.AwayFromZero);

        var result = new RiskAssessmentResult
        {
            Approved = true,
            CurrentExposurePercent = currentExposurePercent,
            ProjectedExposurePercent = currentExposurePercent,
            ProjectedCashPercent = Percentage(cash, totalValue),
            DailyLossPercent = dailyLossPercent,
            DrawdownPercent = drawdownPercent,
            ProjectedSectorExposurePercent = 0m
        };

        if (dailyLossPercent > MaximumDailyLossPercent)
        {
            result.Violations.Add($"Daily portfolio loss {dailyLossPercent:F2}% exceeds {MaximumDailyLossPercent:F2}%.");
        }

        if (drawdownPercent > MaximumDrawdownPercent)
        {
            result.Violations.Add($"Portfolio drawdown {drawdownPercent:F2}% exceeds {MaximumDrawdownPercent:F2}%.");
        }

        if (currentExposurePercent > MaximumPortfolioExposurePercent)
        {
            result.Violations.Add($"Current portfolio exposure {currentExposurePercent:F2}% exceeds {MaximumPortfolioExposurePercent:F2}%.");
        }

        if (candidateTrade is not null && context is not null)
        {
            if (totalValue <= 0m || state is null)
            {
                result.Violations.Add("Portfolio state is missing or total portfolio value is unavailable.");
            }

            if (candidateTrade.EntryPrice <= 0m)
            {
                result.Violations.Add("Candidate trade must include a positive entry price.");
            }

            var candidateValue = candidateTrade.EntryPrice * candidateTrade.Quantity;
            var signedCandidateValue = candidateTrade.Side == OrderSide.Buy ? candidateValue : -candidateValue;
            var projectedExposureValue = Math.Max(0m, currentExposureValue + signedCandidateValue);
            var projectedCash = candidateTrade.Side == OrderSide.Buy ? cash - candidateValue : cash + candidateValue;
            var projectedExposurePercent = Percentage(projectedExposureValue, totalValue);
            var projectedCashPercent = Percentage(projectedCash, totalValue);
            var sectorExposureValue = normalizedPositions
                .Where(x => x.Sector.Equals(context.Sector, StringComparison.OrdinalIgnoreCase))
                .Sum(GetMarketValue);
            var projectedSectorExposureValue = Math.Max(0m, sectorExposureValue + signedCandidateValue);
            var projectedSectorExposurePercent = Percentage(projectedSectorExposureValue, totalValue);
            var positionSizePercent = Percentage(candidateValue, totalValue);

            result.ProjectedExposurePercent = projectedExposurePercent;
            result.ProjectedCashPercent = projectedCashPercent;
            result.ProjectedSectorExposurePercent = projectedSectorExposurePercent;

            if (positionSizePercent > MaximumPositionSizePercent)
            {
                result.Violations.Add($"Position size {positionSizePercent:F2}% exceeds {MaximumPositionSizePercent:F2}%.");
            }

            if (projectedExposurePercent > MaximumPortfolioExposurePercent)
            {
                result.Violations.Add($"Projected portfolio exposure {projectedExposurePercent:F2}% exceeds {MaximumPortfolioExposurePercent:F2}%.");
            }

            if (projectedCashPercent < MinimumCashReservePercent)
            {
                result.Violations.Add($"Projected cash reserve {projectedCashPercent:F2}% is below {MinimumCashReservePercent:F2}%.");
            }

            if (projectedSectorExposurePercent > MaximumSectorExposurePercent)
            {
                result.Violations.Add($"Projected sector exposure {projectedSectorExposurePercent:F2}% exceeds {MaximumSectorExposurePercent:F2}% for {context.Sector}.");
            }

            if (candidateTrade.ConfidenceScore < MinimumConfidenceScore)
            {
                result.Violations.Add($"Confidence score {candidateTrade.ConfidenceScore:F2} is below {MinimumConfidenceScore:F2}.");
            }

            if (candidateTrade.RiskReward < MinimumRiskReward)
            {
                result.Violations.Add($"Risk/reward {candidateTrade.RiskReward:F2} is below {MinimumRiskReward:F2}.");
            }

            if (context.LiquidityScore < MinimumLiquidityScore)
            {
                result.Violations.Add($"Liquidity score {context.LiquidityScore:F2} is below {MinimumLiquidityScore:F2}.");
            }

            if (context.SpreadPercent > MaximumSpreadPercent)
            {
                result.Violations.Add($"Spread {context.SpreadPercent:F2}% exceeds {MaximumSpreadPercent:F2}%.");
            }

            if (_timeProvider.GetUtcNow() - context.ObservedAtUtc > MaximumDataAge)
            {
                result.Violations.Add("Market data is stale and cannot be used for execution.");
            }
        }

        if (result.Violations.Count > 0 || state?.DefensiveModeActive == true)
        {
            result.Approved = false;
            result.DefensiveModeActivated = result.Violations.Count > 0 || state?.DefensiveModeActive == true;
            result.RecommendedActions.Add("Suspend opening new positions.");
            result.RecommendedActions.Add("Reduce exposure and raise cash.");
            result.RecommendedActions.Add("Close weakest positions first.");
            result.RecommendedActions.Add("Emit dashboard risk alerts.");
        }

        return result;
    }

    private static decimal ResolveTotalValue(PortfolioStateRecord? state, IReadOnlyCollection<PortfolioPosition> positions)
    {
        if (state is not null && state.TotalValue > 0m)
        {
            return state.TotalValue;
        }

        return positions.Sum(GetMarketValue);
    }

    private static decimal Percentage(decimal value, decimal total)
    {
        if (total <= 0m)
        {
            return 0m;
        }

        return Math.Round(value / total * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal GetMarketValue(PortfolioPosition position)
    {
        var effectivePrice = position.CurrentPrice > 0m ? position.CurrentPrice : position.AveragePrice;
        return Math.Abs(position.Quantity) * effectivePrice;
    }
}
