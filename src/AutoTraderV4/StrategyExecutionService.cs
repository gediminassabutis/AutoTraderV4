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
        decision.GeneratedAtUtc = DateTimeOffset.UtcNow;
        decision.CreatedUtc = DateTimeOffset.UtcNow;

        _context.StrategySignals.Add(new StrategySignalRecord
        {
            Id = Guid.NewGuid(),
            Ticker = signal.Ticker,
            Signal = signal.Signal,
            Quantity = signal.Quantity,
            Side = decision.Side.ToString(),
            OrderType = decision.OrderType.ToString(),
            CreatedUtc = decision.CreatedUtc
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (_auditLogService is not null)
        {
            var recommendation = new StrategyEngineService().BuildRecommendation(decision.Ticker, decision.EntryPrice);
            _auditLogService.Record(decision, decision.TriggeringStrategy, recommendation.SignalScores, recommendation.Rating, "Within trading constraints");
        }

        return decision;
    }
}
