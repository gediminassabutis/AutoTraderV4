namespace AutoTraderV4;

public sealed class WeightedStrategyMetrics
{
    public decimal TrendScore { get; set; }
    public decimal MomentumScore { get; set; }
    public decimal MeanReversionScore { get; set; }
    public decimal EarningsSurpriseScore { get; set; }
    public decimal SentimentScore { get; set; }
}

public sealed class WeightedStrategyScoreResult
{
    public string Ticker { get; set; } = string.Empty;
    public decimal TrendScore { get; set; }
    public decimal MomentumScore { get; set; }
    public decimal MeanReversionScore { get; set; }
    public decimal EarningsSurpriseScore { get; set; }
    public decimal SentimentScore { get; set; }
    public decimal FinalScore { get; set; }
    public string Rating { get; set; } = string.Empty;
    public bool TradeEligible { get; set; }
    public string[] TopFactors { get; set; } = Array.Empty<string>();
}

public sealed class WeightedStrategyRequest
{
    public string Ticker { get; set; } = string.Empty;
    public decimal TrendScore { get; set; }
    public decimal MomentumScore { get; set; }
    public decimal MeanReversionScore { get; set; }
    public decimal EarningsSurpriseScore { get; set; }
    public decimal SentimentScore { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public Trading212OrderType OrderType { get; set; } = Trading212OrderType.Market;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
}

public sealed class WeightedStrategyScoringService
{
    public WeightedStrategyScoreResult Evaluate(string ticker, WeightedStrategyMetrics metrics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ArgumentNullException.ThrowIfNull(metrics);

        var trendScore = Clamp(metrics.TrendScore);
        var momentumScore = Clamp(metrics.MomentumScore);
        var meanReversionScore = Clamp(metrics.MeanReversionScore);
        var earningsSurpriseScore = Clamp(metrics.EarningsSurpriseScore);
        var sentimentScore = Clamp(metrics.SentimentScore);

        var finalScore = trendScore * 0.35m
            + momentumScore * 0.25m
            + meanReversionScore * 0.15m
            + earningsSurpriseScore * 0.15m
            + sentimentScore * 0.10m;

        var topFactors = new List<string>();
        if (trendScore >= 70m) topFactors.Add("Strong trend confirmation");
        if (momentumScore >= 70m) topFactors.Add("Momentum leadership");
        if (meanReversionScore >= 60m) topFactors.Add("Mean reversion support");
        if (earningsSurpriseScore >= 70m) topFactors.Add("Earnings surprise strength");
        if (sentimentScore >= 65m) topFactors.Add("Positive sentiment flow");

        if (topFactors.Count == 0)
        {
            topFactors.Add("Risk-managed setup");
        }

        return new WeightedStrategyScoreResult
        {
            Ticker = ticker,
            TrendScore = trendScore,
            MomentumScore = momentumScore,
            MeanReversionScore = meanReversionScore,
            EarningsSurpriseScore = earningsSurpriseScore,
            SentimentScore = sentimentScore,
            FinalScore = finalScore,
            Rating = GetRating(finalScore),
            TradeEligible = finalScore >= 75m,
            TopFactors = topFactors.ToArray()
        };
    }

    public static string GetRating(decimal finalScore)
    {
        if (finalScore < 40m) return "Strong Sell";
        if (finalScore < 55m) return "Sell";
        if (finalScore < 65m) return "Hold";
        if (finalScore < 80m) return "Buy";
        return "Strong Buy";
    }

    private static decimal Clamp(decimal value)
    {
        return Math.Clamp(value, 0m, 100m);
    }
}
