using System.Collections.Concurrent;

namespace AutoTraderV4.Services;

public sealed class AuditLogEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Symbol { get; set; } = string.Empty;
    public string TriggeringStrategy { get; set; } = string.Empty;
    public Dictionary<string, decimal> SignalScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ForecastOutput { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public string RiskAssessment { get; set; } = string.Empty;
    public int Confidence { get; set; }
}

public sealed class AuditLogService
{
    private readonly ConcurrentQueue<AuditLogEntry> _entries = new();

    public void Record(TradeDecision decision, string strategy, Dictionary<string, decimal>? signalScores, string forecastOutput, string riskAssessment)
    {
        ArgumentNullException.ThrowIfNull(decision);

        _entries.Enqueue(new AuditLogEntry
        {
            TimestampUtc = decision.CreatedUtc,
            Symbol = decision.Ticker,
            TriggeringStrategy = strategy,
            SignalScores = signalScores ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
            ForecastOutput = forecastOutput,
            EntryPrice = decision.EntryPrice,
            ExitPrice = decision.TakeProfit,
            RiskAssessment = riskAssessment,
            Confidence = decision.Confidence
        });

        while (_entries.Count > 25)
        {
            _entries.TryDequeue(out _);
        }
    }

    public IReadOnlyList<AuditLogEntry> GetRecent()
    {
        return _entries.ToList();
    }
}
