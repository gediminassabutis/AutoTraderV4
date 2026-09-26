using AutoTraderV4.Services;

namespace AutoTraderV4;

public sealed class StrategyExecutionService
{
    private readonly ApplicationDbContext _context;
    private readonly AuditLogService? _auditLogService;
    private readonly RiskGovernanceService? _riskGovernanceService;

    public StrategyExecutionService(
        ApplicationDbContext context,
        AuditLogService? auditLogService = null,
        RiskGovernanceService? riskGovernanceService = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditLogService = auditLogService;
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

        if (_auditLogService is not null)
        {
            var recommendation = new StrategyEngineService().BuildRecommendation(decision.Ticker, decision.EntryPrice);
            decision.ApplyAuditMetadata(
                decision.TriggeringStrategy,
                recommendation.SignalScores,
                decision.ForecastOutput,
                decision.RiskAssessment,
                recommendation.Rating);
            _auditLogService.Record(decision, decision.TriggeringStrategy, recommendation.SignalScores, decision.ForecastOutput, decision.RiskAssessment, recommendation.Rating);
        }

        var orderRecord = decision.ToOrderExecutionRecord();
        _context.OrderExecutionRecords.Add(orderRecord);

        await _context.SaveChangesAsync(cancellationToken);

        return decision;
    }
}
