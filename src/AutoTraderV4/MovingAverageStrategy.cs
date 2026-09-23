namespace AutoTraderV4;

public sealed class MovingAverageStrategy
{
    public StrategySignal Evaluate(string ticker, IEnumerable<decimal> values, decimal quantity, Trading212OrderType orderType)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new ArgumentException("Ticker is required.", nameof(ticker));
        }

        var priceList = (values ?? throw new ArgumentNullException(nameof(values))).ToList();
        if (priceList.Count < 2)
        {
            throw new ArgumentException("At least two values are required to evaluate a moving average signal.", nameof(values));
        }

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        var shortAverage = priceList.TakeLast(3).Average();
        var longAverage = priceList.Average();
        var signalValue = shortAverage - longAverage;

        return new StrategySignal
        {
            Ticker = ticker,
            Signal = signalValue,
            Quantity = quantity,
            OrderType = orderType
        };
    }
}
