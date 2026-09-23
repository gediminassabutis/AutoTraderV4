namespace AutoTraderV4;

public sealed class MovingAverageSignalRequest
{
    public string Ticker { get; set; } = string.Empty;
    public IEnumerable<decimal> Prices { get; set; } = Array.Empty<decimal>();
    public decimal Quantity { get; set; }
    public Trading212OrderType OrderType { get; set; }
}

public sealed class MovingAverageStrategyService
{
    public StrategySignal Evaluate(MovingAverageSignalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Ticker))
        {
            throw new ArgumentException("Ticker is required.", nameof(request));
        }

        var prices = (request.Prices ?? throw new ArgumentNullException(nameof(request.Prices))).ToList();
        if (prices.Count < 2)
        {
            throw new ArgumentException("At least two prices are required.", nameof(request.Prices));
        }

        if (request.Quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request.Quantity), "Quantity must be greater than zero.");
        }

        var shortAverage = prices.TakeLast(3).Average();
        var longAverage = prices.Average();

        return new StrategySignal
        {
            Ticker = request.Ticker,
            Signal = shortAverage - longAverage,
            Quantity = request.Quantity,
            OrderType = request.OrderType
        };
    }
}
