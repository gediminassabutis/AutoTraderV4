namespace AutoTraderV4;

public sealed class StrategySignal
{
    public string Ticker { get; set; } = string.Empty;
    public decimal Signal { get; set; }
    public decimal Quantity { get; set; }
    public Trading212OrderType OrderType { get; set; }
}

public sealed class StrategyEvaluator
{
    public TradeDecision Evaluate(StrategySignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (string.IsNullOrWhiteSpace(signal.Ticker))
        {
            throw new ArgumentException("Ticker is required.", nameof(signal));
        }

        if (signal.Quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(signal.Quantity), "Quantity must be greater than zero.");
        }

        return new TradeDecision
        {
            Ticker = signal.Ticker,
            Side = signal.Signal >= 0m ? OrderSide.Buy : OrderSide.Sell,
            Quantity = signal.Quantity,
            OrderType = signal.OrderType
        };
    }
}
