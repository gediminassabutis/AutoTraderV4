using System.Collections.Concurrent;

namespace AutoTraderV4.Services;

public sealed class AuditLogEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Symbol { get; set; } = string.Empty;
    public string TriggeringStrategy { get; set; } = string.Empty;
    public Dictionary<string, decimal> SignalScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ForecastOutput { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
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
        Record(decision, strategy, signalScores, forecastOutput, riskAssessment, decision.Recommendation);
    }

    public void Record(TradeDecision decision, string strategy, Dictionary<string, decimal>? signalScores, string forecastOutput, string riskAssessment, string recommendation)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var normalizedForecast = string.IsNullOrWhiteSpace(forecastOutput)
            ? decision.BuildForecastOutput()
            : forecastOutput;

        var normalizedStrategy = string.IsNullOrWhiteSpace(strategy) ? decision.TriggeringStrategy : strategy;
        var normalizedSignalScores = signalScores is { Count: > 0 }
            ? signalScores
            : decision.SignalScores is { Count: > 0 }
                ? decision.SignalScores
                : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var normalizedRecommendation = string.IsNullOrWhiteSpace(recommendation)
            ? string.IsNullOrWhiteSpace(decision.RecommendationSummary)
                ? decision.Recommendation
                : decision.RecommendationSummary
            : recommendation;
        var normalizedRiskAssessment = string.IsNullOrWhiteSpace(riskAssessment)
            ? string.IsNullOrWhiteSpace(decision.RiskAssessment)
                ? "Within trading constraints"
                : decision.RiskAssessment
            : riskAssessment;

        _entries.Enqueue(new AuditLogEntry
        {
            TimestampUtc = decision.TimestampUtc == default ? DateTimeOffset.UtcNow : decision.TimestampUtc,
            Symbol = decision.Ticker,
            TriggeringStrategy = normalizedStrategy,
            SignalScores = normalizedSignalScores,
            ForecastOutput = normalizedForecast,
            Recommendation = normalizedRecommendation,
            EntryPrice = decision.EntryPrice,
            ExitPrice = decision.TakeProfit,
            RiskAssessment = normalizedRiskAssessment,
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
