namespace AutoTraderV4;

public sealed class StrategyExecutionService
{
    private readonly ApplicationDbContext _context;

    public StrategyExecutionService(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<TradeDecision> ExecuteAsync(StrategySignal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var evaluator = new StrategyEvaluator();
        var decision = evaluator.Evaluate(signal);

        _context.StrategySignals.Add(new StrategySignalRecord
        {
            Id = Guid.NewGuid(),
            Ticker = signal.Ticker,
            Signal = signal.Signal,
            Quantity = signal.Quantity,
            Side = decision.Side.ToString(),
            OrderType = decision.OrderType.ToString(),
            CreatedUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return decision;
    }
}
