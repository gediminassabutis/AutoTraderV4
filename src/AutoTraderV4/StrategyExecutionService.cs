using AutoTraderV4.Services;

namespace AutoTraderV4;

public sealed class StrategyExecutionService
{
    private readonly ApplicationDbContext _context;
    private readonly AuditLogService? _auditLogService;

    public StrategyExecutionService(ApplicationDbContext context, AuditLogService? auditLogService = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditLogService = auditLogService;
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
        decision.ExecutionStatus = OrderExecutionStatus.Validated;
        decision.RiskAssessment = "Within trading constraints";
        decision.ForecastOutput = decision.BuildForecastOutput();

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
                decision.BuildForecastOutput(),
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
