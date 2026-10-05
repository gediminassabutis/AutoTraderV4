namespace AutoTraderV4.Services;

public static class StrategySignalScoring
{
    public static decimal NormalizePriceDelta(decimal priceDelta, decimal currentPrice)
    {
        if (currentPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPrice), "Current price must be positive.");
        }

        return priceDelta / currentPrice * 100m;
    }

    public static int CalculateConfidence(decimal signal)
    {
        var confidence = 50m + Math.Min(45m, Math.Abs(signal) * 30m);
        return (int)Math.Round(confidence, MidpointRounding.AwayFromZero);
    }

    public static int CalculateSignedScore(decimal signal)
    {
        var signedScore = 50m + (signal * 25m);
        return (int)Math.Clamp(Math.Round(signedScore, MidpointRounding.AwayFromZero), 0m, 100m);
    }

    public static string GetRating(int score)
    {
        return score switch
        {
            < 40 => "Strong Sell",
            < 55 => "Sell",
            < 65 => "Hold",
            < 80 => "Buy",
            _ => "Strong Buy"
        };
    }
}
