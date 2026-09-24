namespace AutoTraderV4;

public enum OrderSide
{
    Buy,
    Sell
}

public sealed class TradeDecision
{
    public string Ticker { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public Trading212OrderType OrderType { get; set; }
    public int ConfidenceScore { get; set; } = 75;
    public string Rating { get; set; } = "Hold";
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskReward { get; set; }
    public string[] TopFactors { get; set; } = Array.Empty<string>();
    public string TriggeringStrategy { get; set; } = string.Empty;
    public string RiskAssessment { get; set; } = string.Empty;
    public string ForecastOutput { get; set; } = string.Empty;

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
