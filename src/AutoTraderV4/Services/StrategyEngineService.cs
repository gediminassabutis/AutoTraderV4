namespace AutoTraderV4.Services;

public sealed class StrategySignalScores
{
    public decimal Trend { get; set; }
    public decimal Momentum { get; set; }
    public decimal MeanReversion { get; set; }
    public decimal EarningsSurprise { get; set; }
    public decimal Sentiment { get; set; }
    public decimal FinalScore { get; set; }
    public string Rating { get; set; } = string.Empty;
}

public sealed class TradeRecommendation
{
    public string Symbol { get; set; } = string.Empty;
    public string Rating { get; set; } = string.Empty;
    public int Confidence { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskReward { get; set; }
    public List<string> TopFactors { get; set; } = [];
    public Dictionary<string, decimal> SignalScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RecommendationRequest
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public sealed class StrategyEngineService
{
    public StrategySignalScores Score(string symbol, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var changePercent = 1.25m;
        var rsi = 68m;
        var earningsGrowth = 18m;
        var sentiment = 82m;

        var trend = Math.Clamp(52m + (price > 100m ? 15m : 5m) + changePercent * 8m, 0m, 100m);
        var momentum = Math.Clamp(48m + earningsGrowth + (rsi > 60m ? 18m : 6m), 0m, 100m);
        var meanReversion = Math.Clamp(66m - (rsi > 70m ? 12m : 0m), 0m, 100m);
        var earnings = Math.Clamp(36m + earningsGrowth * 2m, 0m, 100m);
        var sentimentScore = Math.Clamp(sentiment, 0m, 100m);
        var finalScore = Math.Clamp(
            trend * 0.35m +
            momentum * 0.25m +
            meanReversion * 0.15m +
            earnings * 0.15m +
            sentimentScore * 0.10m,
            0m,
            100m);

        return new StrategySignalScores
        {
            Trend = trend,
            Momentum = momentum,
            MeanReversion = meanReversion,
            EarningsSurprise = earnings,
            Sentiment = sentimentScore,
            FinalScore = finalScore,
            Rating = GetRating(finalScore)
        };
    }

    public TradeRecommendation BuildRecommendation(string symbol, decimal price)
    {
        var normalizedPrice = price > 0m ? price : 100m;
        var scores = Score(symbol, normalizedPrice);
        var confidence = (int)Math.Round(scores.FinalScore, MidpointRounding.AwayFromZero);
        var stopLoss = normalizedPrice * 0.94m;
        var takeProfit = normalizedPrice * 1.18m;
        var riskReward = (takeProfit - normalizedPrice) / (normalizedPrice - stopLoss);

        return new TradeRecommendation
        {
            Symbol = symbol,
            Rating = scores.Rating,
            Confidence = confidence,
            EntryPrice = normalizedPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            RiskReward = Math.Round(riskReward, 2),
            TopFactors = ["Strong earnings growth", "Positive momentum", "Healthy sentiment backdrop"],
            SignalScores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Trend"] = scores.Trend,
                ["Momentum"] = scores.Momentum,
                ["MeanReversion"] = scores.MeanReversion,
                ["EarningsSurprise"] = scores.EarningsSurprise,
                ["Sentiment"] = scores.Sentiment
            }
        };
    }

    public IReadOnlyList<WatchlistOpportunity> BuildWatchlist()
    {
        return new List<WatchlistOpportunity>
        {
            new() { Symbol = "NVDA", Rating = "Strong Buy", Confidence = 92, ForecastReturn = 12.5m, RiskScore = 22m, Price = 132.40m, Sector = "Technology" },
            new() { Symbol = "AMD", Rating = "Buy", Confidence = 87, ForecastReturn = 10.8m, RiskScore = 29m, Price = 168.20m, Sector = "Technology" },
            new() { Symbol = "META", Rating = "Buy", Confidence = 85, ForecastReturn = 9.4m, RiskScore = 31m, Price = 514.30m, Sector = "Communication" },
            new() { Symbol = "LLY", Rating = "Buy", Confidence = 81, ForecastReturn = 8.6m, RiskScore = 35m, Price = 864.50m, Sector = "Healthcare" }
        };
    }

    private static string GetRating(decimal score)
    {
        return score switch
        {
            >= 80m => "Strong Buy",
            >= 65m => "Buy",
            >= 55m => "Hold",
            >= 40m => "Sell",
            _ => "Strong Sell"
        };
    }
}
