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
