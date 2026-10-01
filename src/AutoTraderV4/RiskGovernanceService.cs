namespace AutoTraderV4;

public sealed class RiskGovernanceService
{
    private const decimal MaximumPortfolioExposurePercent = 95m;
    private const decimal MinimumCashReservePercent = 5m;
    private const decimal MaximumPositionSizePercent = 5m;
    private const decimal MaximumSectorExposurePercent = 20m;
    private const decimal MaximumDailyLossPercent = 2m;
    private const decimal MaximumDrawdownPercent = 10m;
    private const decimal MinimumFinalScore = 75m;
    private const decimal MinimumConfidenceScore = 80m;
    private const decimal MinimumRiskReward = 2.5m;
    private const decimal MinimumLiquidityScore = 60m;
    private const decimal MaximumSpreadPercent = 1m;
    private static readonly TimeSpan MaximumDataAge = TimeSpan.FromMinutes(15);

    private readonly IPortfolioRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly PortfolioStateSyncService _portfolioStateSyncService;
    private readonly bool _useDemoData;

    public RiskGovernanceService(
        IPortfolioRepository repository,
        TimeProvider timeProvider,
        PortfolioStateSyncService portfolioStateSyncService,
        bool useDemoData)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _portfolioStateSyncService = portfolioStateSyncService ?? throw new ArgumentNullException(nameof(portfolioStateSyncService));
        _useDemoData = useDemoData;
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

        await _portfolioStateSyncService.EnsureFreshAsync(cancellationToken);
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
        var isSell = candidateTrade?.Side == OrderSide.Sell;
        var hasExistingRiskBreach = false;

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

        void RecordPortfolioBreach(string message)
        {
            hasExistingRiskBreach = true;
            if (isSell)
            {
                result.RecommendedActions.Add(message);
            }
            else
            {
                result.Violations.Add(message);
            }
        }

        if (dailyLossPercent > MaximumDailyLossPercent)
        {
            RecordPortfolioBreach($"Daily portfolio loss {dailyLossPercent:F2}% exceeds {MaximumDailyLossPercent:F2}%.");
        }

        if (drawdownPercent > MaximumDrawdownPercent)
        {
            RecordPortfolioBreach($"Portfolio drawdown {drawdownPercent:F2}% exceeds {MaximumDrawdownPercent:F2}%.");
        }

        if (currentExposurePercent > MaximumPortfolioExposurePercent)
        {
            RecordPortfolioBreach($"Current portfolio exposure {currentExposurePercent:F2}% exceeds {MaximumPortfolioExposurePercent:F2}%.");
        }

        if (candidateTrade is not null && context is not null)
        {
            if (totalValue <= 0m || state is null)
            {
                result.Violations.Add("Portfolio state is missing or total portfolio value is unavailable.");
            }

            if (!string.Equals(candidateTrade.Ticker.Trim(), context.Symbol.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                result.Violations.Add("Risk context symbol does not match the candidate trade.");
            }

            PortfolioPosition? heldPosition = null;
            var executionPrice = candidateTrade.EntryPrice;
            if (candidateTrade.Side == OrderSide.Sell)
            {
                heldPosition = normalizedPositions.FirstOrDefault(position =>
                    string.Equals(position.Ticker, candidateTrade.Ticker.Trim(), StringComparison.OrdinalIgnoreCase));
                if (heldPosition is null)
                {
                    result.Violations.Add("Sell quantity cannot exceed a synchronized open position.");
                }
                else
                {
                    if (candidateTrade.Quantity > Math.Abs(heldPosition.Quantity))
                    {
                        result.Violations.Add("Sell quantity exceeds the synchronized open position.");
                    }

                    executionPrice = heldPosition.CurrentPrice > 0m
                        ? heldPosition.CurrentPrice
                        : heldPosition.AveragePrice;
                }
            }
            else if (executionPrice <= 0m)
            {
                result.Violations.Add("Candidate trade must include a positive entry price.");
            }

            if (executionPrice <= 0m)
            {
                result.Violations.Add("A positive broker-confirmed position price is required.");
            }

            var candidateValue = executionPrice > 0m
                ? executionPrice * candidateTrade.Quantity
                : 0m;
            var signedCandidateValue = candidateTrade.Side == OrderSide.Buy ? candidateValue : -candidateValue;
            var projectedExposureValue = Math.Max(0m, currentExposureValue + signedCandidateValue);
            var projectedCash = candidateTrade.Side == OrderSide.Buy ? cash - candidateValue : cash + candidateValue;
            var projectedExposurePercent = Percentage(projectedExposureValue, totalValue);
            var projectedCashPercent = Percentage(projectedCash, totalValue);
            var sector = heldPosition?.Sector ?? context.Sector;
            var sectorExposureValue = normalizedPositions
                .Where(position => position.Sector.Equals(sector, StringComparison.OrdinalIgnoreCase))
                .Sum(GetMarketValue);
            var projectedSectorExposureValue = Math.Max(0m, sectorExposureValue + signedCandidateValue);
            var projectedSectorExposurePercent = Percentage(projectedSectorExposureValue, totalValue);
            var positionSizePercent = Percentage(candidateValue, totalValue);

            result.ProjectedExposurePercent = projectedExposurePercent;
            result.ProjectedCashPercent = projectedCashPercent;
            result.ProjectedSectorExposurePercent = projectedSectorExposurePercent;

            if (candidateTrade.Side == OrderSide.Buy)
            {
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
                    result.Violations.Add($"Projected sector exposure {projectedSectorExposurePercent:F2}% exceeds {MaximumSectorExposurePercent:F2}% for {sector}.");
                }

                if (candidateTrade.FinalScore < MinimumFinalScore)
                {
                    result.Violations.Add($"Final score {candidateTrade.FinalScore:F2} is below {MinimumFinalScore:F2}.");
                }

                if (!candidateTrade.EligibleForExecution)
                {
                    result.Violations.Add("Candidate trade is not marked eligible for execution.");
                }

                if (candidateTrade.ConfidenceScore < MinimumConfidenceScore)
                {
                    result.Violations.Add($"Confidence score {candidateTrade.ConfidenceScore:F2} is below {MinimumConfidenceScore:F2}.");
                }

                var protectiveLevelsValid = candidateTrade.EntryPrice > 0m
                    && candidateTrade.StopLoss > 0m
                    && candidateTrade.TakeProfit > candidateTrade.EntryPrice
                    && candidateTrade.StopLoss < candidateTrade.EntryPrice;
                if (!protectiveLevelsValid)
                {
                    result.Violations.Add("Opening trades must include a positive entry, stop-loss below entry, and take-profit above entry.");
                }
                else
                {
                    var calculatedRiskReward =
                        (candidateTrade.TakeProfit - candidateTrade.EntryPrice)
                        / (candidateTrade.EntryPrice - candidateTrade.StopLoss);
                    result.CalculatedRiskReward = calculatedRiskReward;
                    if (calculatedRiskReward < MinimumRiskReward)
                    {
                        result.Violations.Add($"Calculated risk/reward {calculatedRiskReward:F2} is below {MinimumRiskReward:F2}.");
                    }
                }

                if (!_useDemoData)
                {
                    result.Violations.Add("Live entries are disabled because daily-loss and drawdown telemetry is not available from the broker snapshot.");
                }
            }
            else if (projectedExposurePercent >= currentExposurePercent)
            {
                result.Violations.Add("Sell orders must reduce synchronized portfolio exposure.");
            }

            if (context.LiquidityScore < MinimumLiquidityScore)
            {
                result.Violations.Add($"Liquidity score {context.LiquidityScore:F2} is below {MinimumLiquidityScore:F2}.");
            }

            if (context.SpreadPercent > MaximumSpreadPercent)
            {
                result.Violations.Add($"Spread {context.SpreadPercent:F2}% exceeds {MaximumSpreadPercent:F2}%.");
            }

            var dataAge = _timeProvider.GetUtcNow() - context.ObservedAtUtc;
            if (dataAge < TimeSpan.Zero || dataAge > MaximumDataAge)
            {
                result.Violations.Add("Market data is stale or has an invalid future timestamp and cannot be used for execution.");
            }
        }

        var isRiskReducingSell = isSell
            && result.ProjectedExposurePercent < result.CurrentExposurePercent;
        if (state?.DefensiveModeActive == true && !isRiskReducingSell)
        {
            result.Violations.Add("Portfolio is in defensive mode; opening or non-reducing trades are blocked.");
        }

        if (result.Violations.Count > 0 || hasExistingRiskBreach || state?.DefensiveModeActive == true)
        {
            result.DefensiveModeActivated = true;
            result.RecommendedActions.Add("Suspend opening new positions.");
            result.RecommendedActions.Add("Reduce exposure and raise cash.");
            result.RecommendedActions.Add("Close weakest positions first.");
            result.RecommendedActions.Add("Emit dashboard risk alerts.");
        }

        result.Approved = result.Violations.Count == 0
            && (isRiskReducingSell || !hasExistingRiskBreach && state?.DefensiveModeActive != true);
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
