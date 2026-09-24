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

        var side = signal.Signal >= 0m ? OrderSide.Buy : OrderSide.Sell;
        var price = Math.Max(Math.Abs(signal.Signal) * 100m, 1m);
        var confidence = Math.Clamp((int)Math.Round(Math.Abs(signal.Signal) * 100m + 50m), 0, 100);
        var rating = side == OrderSide.Buy
            ? confidence switch
            {
                >= 80 => RecommendationRating.StrongBuy,
                >= 65 => RecommendationRating.Buy,
                >= 55 => RecommendationRating.Hold,
                >= 40 => RecommendationRating.Sell,
                _ => RecommendationRating.StrongSell
            }
            : confidence switch
            {
                >= 80 => RecommendationRating.StrongSell,
                >= 65 => RecommendationRating.Sell,
                >= 55 => RecommendationRating.Hold,
                >= 40 => RecommendationRating.Buy,
                _ => RecommendationRating.StrongBuy
            };
        var stopLoss = side == OrderSide.Buy
            ? Math.Max(price * 0.94m, 0.01m)
            : Math.Max(price * 1.06m, 0.01m);
        var takeProfit = side == OrderSide.Buy
            ? Math.Max(price * 1.18m, 0.01m)
            : Math.Max(price * 0.82m, 0.01m);

        return new TradeDecision
        {
            Ticker = signal.Ticker,
            Side = side,
            Quantity = signal.Quantity,
            OrderType = signal.OrderType,
            EntryPrice = price,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            RiskReward = 3.0m,
            FinalScore = confidence,
            CombinedStrategyScore = confidence,
            ConfidenceScore = confidence,
            Confidence = confidence,
            Rating = rating switch
            {
                RecommendationRating.StrongBuy => "Strong Buy",
                RecommendationRating.Buy => "Buy",
                RecommendationRating.Hold => "Hold",
                RecommendationRating.Sell => "Sell",
                _ => "Strong Sell"
            },
            RecommendationRating = rating,
            TopFactors = ["Signal strength", "Execution quality", "Risk-aware sizing"],
            FactorBreakdown = new FactorScoreBreakdown
            {
                TechnicalScore = confidence,
                FundamentalScore = confidence,
                MomentumScore = confidence,
                SentimentScore = confidence,
                MacroScore = confidence
            },
            StrategyBreakdown =
            [
                new StrategyComponentScore
                {
                    Strategy = "Moving Average",
                    WeightPercent = 100m,
                    Score = confidence,
                    Triggered = true,
                    Reasons = ["Short moving average crossed above the long moving average."]
                }
            ],
            Forecast = new ForecastSnapshot
            {
                OneDayReturnPercent = signal.Signal,
                FiveDayReturnPercent = signal.Signal * 2m,
                ThirtyDayReturnPercent = signal.Signal * 4m,
                NinetyDayReturnPercent = signal.Signal * 6m,
                ModelConfidenceScore = confidence
            },
            SignalScores = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Signal"] = Math.Abs(signal.Signal)
            },
            TriggeringStrategy = "moving-average",
            EligibleForExecution = confidence >= 80,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }
}
