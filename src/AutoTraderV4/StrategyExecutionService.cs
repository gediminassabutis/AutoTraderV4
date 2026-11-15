using AutoTraderV4.Services;

namespace AutoTraderV4;

public sealed class StrategyExecutionService
{
    private readonly ApplicationDbContext _context;
    private readonly RiskGovernanceService? _riskGovernanceService;

    public StrategyExecutionService(
        ApplicationDbContext context,
        RiskGovernanceService? riskGovernanceService = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _riskGovernanceService = riskGovernanceService;
    }

    public async Task<TradeDecision> ExecuteAsync(StrategySignal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var evaluator = new StrategyEvaluator();
        var decision = evaluator.Evaluate(signal);
        var now = DateTimeOffset.UtcNow;
        decision.GeneratedAtUtc = now;
        decision.CreatedUtc = now;
        decision.TimestampUtc = now;
        decision.ForecastOutput = decision.BuildForecastOutput();

        if (_riskGovernanceService is not null)
        {
            var riskContext = new RiskContextSnapshot
            {
                Symbol = decision.Ticker,
                Sector = string.IsNullOrWhiteSpace(decision.Sector) ? "Unknown" : decision.Sector,
                LiquidityScore = 85m,
                SpreadPercent = 0.25m,
                ObservedAtUtc = now
            };

            var riskAssessment = await _riskGovernanceService.EvaluateAsync(decision, riskContext, cancellationToken);
            decision.ExecutionStatus = riskAssessment.Approved ? OrderExecutionStatus.Validated : OrderExecutionStatus.Rejected;
            decision.EligibleForExecution = riskAssessment.Approved && decision.EligibleForExecution;
            decision.RiskAssessment = riskAssessment.Violations.Count > 0
                ? string.Join("; ", riskAssessment.Violations)
                : "Within trading constraints";
        }
        else
        {
            decision.ExecutionStatus = OrderExecutionStatus.Validated;
            decision.RiskAssessment = "Within trading constraints";
        }

        _context.StrategySignals.Add(new StrategySignalRecord
        {
            Id = Guid.NewGuid(),
            Ticker = signal.Ticker,
            Signal = signal.Signal,
            Quantity = signal.Quantity,
            Side = decision.Side.ToString(),
            OrderType = decision.OrderType.ToString(),
            CreatedUtc = now
        });

        decision.ApplyAuditMetadata(
            decision.TriggeringStrategy,
            decision.SignalScores,
            decision.ForecastOutput,
            decision.RiskAssessment,
            decision.Rating);

        var orderRecord = decision.ToOrderExecutionRecord();
        _context.OrderExecutionRecords.Add(orderRecord);

        // Persist the decision to the audit trail in the same save as the signal and order record.
        _context.TradeAuditEntries.Add(new TradeAuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = "strategy-signal-evaluation",
            Symbol = decision.Ticker.Trim().ToUpperInvariant(),
            StrategyName = decision.TriggeringStrategy,
            FinalScore = decision.FinalScore,
            ConfidenceScore = decision.ConfidenceScore,
            EntryPrice = decision.EntryPrice,
            StopLoss = decision.StopLoss,
            TakeProfit = decision.TakeProfit,
            Quantity = decision.Quantity,
            Side = decision.Side.ToString(),
            OrderType = decision.OrderType.ToString(),
            SignalScoresJson = orderRecord.SignalScoresJson,
            ForecastJson = orderRecord.ForecastJson,
            RiskAssessmentJson = orderRecord.RiskAssessmentJson,
            RecommendationJson = orderRecord.RecommendationJson,
            CreatedUtc = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        return decision;
    }
}
