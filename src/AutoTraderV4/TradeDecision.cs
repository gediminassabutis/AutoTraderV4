namespace AutoTraderV4;

public enum OrderSide
{
    Buy,
    Sell
}

public sealed class TradeDecision
{
    public string Ticker { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public Trading212OrderType OrderType { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskReward { get; set; }
    public decimal FinalScore { get; set; }
    public decimal CombinedStrategyScore { get; set; }
    public decimal ConfidenceScore { get; set; }
    public int Confidence { get; set; }
    public string Rating { get; set; } = "Hold";
    public RecommendationRating RecommendationRating { get; set; } = RecommendationRating.Hold;
    public List<string> TopFactors { get; set; } = [];
    public Dictionary<string, decimal> SignalScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public FactorScoreBreakdown FactorBreakdown { get; set; } = new();
    public List<StrategyComponentScore> StrategyBreakdown { get; set; } = [];
    public ForecastSnapshot Forecast { get; set; } = new();
    public string TriggeringStrategy { get; set; } = "weighted-strategy";
    public bool EligibleForExecution { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Ticker))
        {
            throw new ArgumentException("Ticker is required.", nameof(Ticker));
        }

        if (Quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(Quantity), "Quantity must be greater than zero.");
        }

        if (EntryPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryPrice), "EntryPrice cannot be negative.");
        }

        if (StopLoss < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(StopLoss), "StopLoss cannot be negative.");
        }

        if (TakeProfit < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(TakeProfit), "TakeProfit cannot be negative.");
        }

        if (Confidence is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(Confidence), "Confidence must be between 0 and 100.");
        }

        if (ConfidenceScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(ConfidenceScore), "ConfidenceScore must be between 0 and 100.");
        }

        if (EntryPrice > 0m && StopLoss > 0m && TakeProfit > 0m)
        {
            if (Side == OrderSide.Buy && !(StopLoss < EntryPrice && TakeProfit > EntryPrice))
            {
                throw new ArgumentException("Buy decisions must have StopLoss below EntryPrice and TakeProfit above EntryPrice.", nameof(StopLoss));
            }

            if (Side == OrderSide.Sell && !(StopLoss > EntryPrice && TakeProfit < EntryPrice))
            {
                throw new ArgumentException("Sell decisions must have StopLoss above EntryPrice and TakeProfit below EntryPrice.", nameof(StopLoss));
            }
        }
    }
}

public static class OrderExecutionService
{
    public static Trading212OrderRequest CreateRequest(TradeDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        decision.Validate();

        var normalizedQuantity = Math.Abs(decision.Quantity);

        return decision.Side switch
        {
            OrderSide.Buy => Trading212OrderRequest.CreateBuy(decision.Ticker, normalizedQuantity, decision.OrderType),
            OrderSide.Sell => Trading212OrderRequest.CreateSell(decision.Ticker, normalizedQuantity, decision.OrderType),
            _ => throw new InvalidOperationException($"Unsupported order side: {decision.Side}")
        };
    }
}
