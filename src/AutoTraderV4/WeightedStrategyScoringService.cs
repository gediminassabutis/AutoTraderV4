namespace AutoTraderV4;

public sealed class WeightedStrategySignal
{
    public string Strategy { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal Score { get; set; }
    public int Confidence { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public bool Triggered { get; set; }
}

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
    public int Confidence { get; set; }
    public bool TradeEligible { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public WeightedStrategySignal[] StrategySignals { get; set; } = Array.Empty<WeightedStrategySignal>();
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
    public const decimal MinimumExecutionScore = 75m;

    public WeightedStrategyScoreResult Evaluate(string ticker, WeightedStrategyMetrics metrics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ArgumentNullException.ThrowIfNull(metrics);

        var trendScore = Clamp(metrics.TrendScore);
        var momentumScore = Clamp(metrics.MomentumScore);
        var meanReversionScore = Clamp(metrics.MeanReversionScore);
        var earningsSurpriseScore = Clamp(metrics.EarningsSurpriseScore);
        var sentimentScore = Clamp(metrics.SentimentScore);

        var finalScore = Math.Round(
            trendScore * 0.35m
            + momentumScore * 0.25m
            + meanReversionScore * 0.15m
            + earningsSurpriseScore * 0.15m
            + sentimentScore * 0.10m,
            2,
            MidpointRounding.AwayFromZero);

        var strategySignals = new[]
        {
            BuildStrategySignal("Trend Following", 35m, trendScore, "uptrend quality, moving-average confirmation, and volume support"),
            BuildStrategySignal("Momentum", 25m, momentumScore, "relative strength, earnings acceleration, and persistence in price action"),
            BuildStrategySignal("Mean Reversion", 15m, meanReversionScore, "oversold quality and a temporary dislocation that may revert toward fair value"),
            BuildStrategySignal("Earnings Surprise", 15m, earningsSurpriseScore, "guidance uplift and positive analyst revisions around earnings"),
            BuildStrategySignal("AI Sentiment", 10m, sentimentScore, "market sentiment, macro backdrop, and narrative momentum")
        };

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

        var tradeEligible = finalScore >= MinimumExecutionScore;
        var rating = GetRating(finalScore);
        var rationale = tradeEligible
            ? $"Weighted composite score {finalScore:F2}/100 passes the minimum execution threshold ({MinimumExecutionScore:F0}). Leading drivers: {string.Join(", ", topFactors.Take(3))}."
            : $"Weighted composite score {finalScore:F2}/100 is below the minimum execution threshold ({MinimumExecutionScore:F0}). Monitor for re-entry until trend, momentum, and sentiment align.";

        return new WeightedStrategyScoreResult
        {
            Ticker = ticker,
            TrendScore = trendScore,
            MomentumScore = momentumScore,
            MeanReversionScore = meanReversionScore,
            EarningsSurpriseScore = earningsSurpriseScore,
            SentimentScore = sentimentScore,
            FinalScore = finalScore,
            Rating = rating,
            Confidence = (int)Math.Round(finalScore, MidpointRounding.AwayFromZero),
            TradeEligible = tradeEligible,
            Rationale = rationale,
            StrategySignals = strategySignals,
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

    private static WeightedStrategySignal BuildStrategySignal(string strategyName, decimal weight, decimal score, string rationale)
    {
        var normalizedScore = Clamp(score);
        return new WeightedStrategySignal
        {
            Strategy = strategyName,
            Weight = weight,
            Score = normalizedScore,
            Confidence = (int)Math.Round(normalizedScore, MidpointRounding.AwayFromZero),
            Triggered = normalizedScore >= 65m,
            Rationale = $"{strategyName} score {normalizedScore:F1}/100 based on {rationale}."
        };
    }

    private static decimal Clamp(decimal value)
    {
        return Math.Clamp(value, 0m, 100m);
    }
}
