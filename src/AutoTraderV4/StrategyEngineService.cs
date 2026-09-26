namespace AutoTraderV4;

public sealed class WeightedStrategyEngineService
{
    private readonly TimeProvider _timeProvider;

    public WeightedStrategyEngineService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public TradeDecision Evaluate(StrategyEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = request.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException("Strategy evaluation request is invalid.", nameof(request));
        }

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var sector = request.Sector.Trim();

        var trend = ScoreTrendFollowing(request);
        var momentum = ScoreMomentum(request);
        var meanReversion = ScoreMeanReversion(request);
        var earnings = ScoreEarningsSurprise(request);
        var sentiment = ScoreSentiment(request);

        var strategies = new List<StrategyComponentScore>
        {
            trend,
            momentum,
            meanReversion,
            earnings,
            sentiment
        };

        var combinedStrategyScore = Math.Round(strategies.Sum(x => x.Score * x.WeightPercent / 100m), 2, MidpointRounding.AwayFromZero);
        var factorBreakdown = BuildFactorBreakdown(request, trend, momentum, meanReversion, earnings, sentiment);
        var factorComposite =
            factorBreakdown.TechnicalScore * 0.30m +
            factorBreakdown.FundamentalScore * 0.30m +
            factorBreakdown.MomentumScore * 0.20m +
            factorBreakdown.SentimentScore * 0.10m +
            factorBreakdown.MacroScore * 0.10m;

        var forecastScore = ScoreForecast(request.Forecast);
        var finalScore = Clamp(Math.Round((combinedStrategyScore * 0.50m) + (factorComposite * 0.40m) + (forecastScore * 0.10m), 2, MidpointRounding.AwayFromZero));
        var rating = GetRating(finalScore);
        var side = rating is RecommendationRating.Sell or RecommendationRating.StrongSell ? OrderSide.Sell : OrderSide.Buy;

        var perShareRisk = Math.Max(request.CurrentPrice * 0.04m, request.CurrentPrice * (Math.Max(request.Signals.AtrPercent, 1m) / 100m));
        var entryPrice = Math.Round(request.CurrentPrice, 4, MidpointRounding.AwayFromZero);
        var stopLoss = side == OrderSide.Buy
            ? Math.Round(entryPrice - perShareRisk, 4, MidpointRounding.AwayFromZero)
            : Math.Round(entryPrice + perShareRisk, 4, MidpointRounding.AwayFromZero);
        var takeProfit = side == OrderSide.Buy
            ? Math.Round(entryPrice + (perShareRisk * 3m), 4, MidpointRounding.AwayFromZero)
            : Math.Round(entryPrice - (perShareRisk * 3m), 4, MidpointRounding.AwayFromZero);
        var riskReward = CalculateRiskReward(side, entryPrice, stopLoss, takeProfit);
        var confidenceScore = Clamp(Math.Round((finalScore * 0.60m) + (forecastScore * 0.10m) + (request.Forecast.ModelConfidenceScore * 0.30m), 2, MidpointRounding.AwayFromZero));

        var forecastSummary = !string.IsNullOrWhiteSpace(request.Forecast.Summary)
            ? request.Forecast.Summary
            : $"Weighted forecast return: {request.Forecast.OneDayReturnPercent:F1}% 1D, {request.Forecast.FiveDayReturnPercent:F1}% 5D, {request.Forecast.ThirtyDayReturnPercent:F1}% 30D, {request.Forecast.NinetyDayReturnPercent:F1}% 90D.";

        return new TradeDecision
        {
            Ticker = symbol,
            Sector = sector,
            Side = side,
            Quantity = request.SuggestedQuantity,
            OrderType = request.OrderType,
            Rating = GetRatingLabel(rating),
            RecommendationRating = rating,
            FinalScore = finalScore,
            CombinedStrategyScore = combinedStrategyScore,
            ConfidenceScore = confidenceScore,
            Confidence = (int)Math.Round(confidenceScore, MidpointRounding.AwayFromZero),
            EntryPrice = entryPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            RiskReward = riskReward,
            FactorBreakdown = factorBreakdown,
            StrategyBreakdown = strategies,
            Forecast = request.Forecast,
            ForecastOutput = forecastSummary,
            TopFactors = BuildTopFactors(strategies, factorBreakdown, request),
            EligibleForExecution = finalScore >= 75m && confidenceScore >= 80m && riskReward >= 2.5m,
            GeneratedAtUtc = _timeProvider.GetUtcNow(),
            CreatedUtc = _timeProvider.GetUtcNow(),
            TriggeringStrategy = "weighted-strategy",
            SignalScores = strategies.ToDictionary(
                x => x.Strategy.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase),
                x => x.Score,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static StrategyComponentScore ScoreTrendFollowing(StrategyEvaluationRequest request)
    {
        var currentVs50 = PercentageDiffScore(request.CurrentPrice, request.Signals.FiftyDayMovingAverage, -10m, 10m);
        var shortVsLong = PercentageDiffScore(request.Signals.FiftyDayMovingAverage, request.Signals.TwoHundredDayMovingAverage, -10m, 10m);
        var rsiTrend = SignedScore(request.Signals.RsiTrendScore);
        var volumeTrend = SignedScore(request.Signals.VolumeTrendScore);
        var score = Average(currentVs50, shortVsLong, rsiTrend, volumeTrend);

        return new StrategyComponentScore
        {
            Strategy = "Trend Following",
            WeightPercent = 35m,
            Score = score,
            Triggered = score >= 65m,
            Reasons =
            [
                $"Price vs 50DMA score: {currentVs50:F1}",
                $"50DMA vs 200DMA score: {shortVsLong:F1}",
                $"RSI trend score: {rsiTrend:F1}",
                $"Volume trend score: {volumeTrend:F1}"
            ]
        };
    }

    private static StrategyComponentScore ScoreMomentum(StrategyEvaluationRequest request)
    {
        var relativeStrength = Clamp(request.Signals.RelativeStrengthPercentile);
        var earningsGrowth = LinearScore(request.Signals.EarningsGrowthPercent, -10m, 30m);
        var volumeTrend = SignedScore(request.Signals.VolumeTrendScore);
        var score = Average(relativeStrength, earningsGrowth, volumeTrend);

        return new StrategyComponentScore
        {
            Strategy = "Momentum",
            WeightPercent = 25m,
            Score = score,
            Triggered = score >= 65m,
            Reasons =
            [
                $"Relative strength percentile: {relativeStrength:F1}",
                $"Earnings growth score: {earningsGrowth:F1}",
                $"Volume confirmation score: {volumeTrend:F1}"
            ]
        };
    }

    private static StrategyComponentScore ScoreMeanReversion(StrategyEvaluationRequest request)
    {
        var oversoldScore = Clamp(((35m - request.Signals.RsiValue) / 15m) * 100m);
        var quality = Clamp(request.Signals.QualityScore);
        var dislocation = LinearScore(request.Signals.PriceDislocationPercent, 0m, 20m);
        var score = Average(oversoldScore, quality, dislocation);

        return new StrategyComponentScore
        {
            Strategy = "Mean Reversion",
            WeightPercent = 15m,
            Score = score,
            Triggered = score >= 65m,
            Reasons =
            [
                $"Oversold score: {oversoldScore:F1}",
                $"Quality score: {quality:F1}",
                $"Price dislocation score: {dislocation:F1}"
            ]
        };
    }

    private static StrategyComponentScore ScoreEarningsSurprise(StrategyEvaluationRequest request)
    {
        var surprise = LinearScore(request.Signals.EarningsSurprisePercent, -5m, 20m);
        var guidance = SignedScore(request.Signals.GuidanceChangeScore);
        var analystRevisions = SignedScore(request.Signals.AnalystRevisionScore);
        var score = Average(surprise, guidance, analystRevisions);

        return new StrategyComponentScore
        {
            Strategy = "Earnings Surprise",
            WeightPercent = 15m,
            Score = score,
            Triggered = score >= 65m,
            Reasons =
            [
                $"Earnings surprise score: {surprise:F1}",
                $"Guidance change score: {guidance:F1}",
                $"Analyst revisions score: {analystRevisions:F1}"
            ]
        };
    }

    private static StrategyComponentScore ScoreSentiment(StrategyEvaluationRequest request)
    {
        var sentiment = SignedScore(request.Signals.SentimentScore);
        var macro = SignedScore(request.Signals.MacroScore);
        var forecast = ScoreForecast(request.Forecast);
        var score = Average(sentiment, macro, forecast);

        return new StrategyComponentScore
        {
            Strategy = "AI Sentiment",
            WeightPercent = 10m,
            Score = score,
            Triggered = score >= 65m,
            Reasons =
            [
                $"Sentiment score: {sentiment:F1}",
                $"Macro context score: {macro:F1}",
                $"Forecast score: {forecast:F1}"
            ]
        };
    }

    private static FactorScoreBreakdown BuildFactorBreakdown(
        StrategyEvaluationRequest request,
        StrategyComponentScore trend,
        StrategyComponentScore momentum,
        StrategyComponentScore meanReversion,
        StrategyComponentScore earnings,
        StrategyComponentScore sentiment)
    {
        var technical = Average(
            trend.Score,
            Clamp(100m - Math.Abs(request.Signals.RsiValue - 55m) * 2m),
            SignedScore(request.Signals.VolumeTrendScore),
            LinearScore(8m - request.Signals.AtrPercent, -5m, 8m));

        var fundamental = Average(
            LinearScore(request.Signals.RevenueGrowthPercent, -10m, 30m),
            LinearScore(request.Signals.EarningsGrowthPercent, -10m, 30m),
            LinearScore(request.Signals.FreeCashFlowMarginPercent, 0m, 30m),
            Clamp(100m - (request.Signals.DebtToEquityRatio * 10m)),
            LinearScore(request.Signals.ReturnOnEquityPercent, 0m, 30m),
            Clamp(100m - (request.Signals.PegRatio * 20m)),
            Clamp(request.Signals.QualityScore));

        var momentumScore = Average(
            momentum.Score,
            Clamp(request.Signals.RelativeStrengthPercentile),
            SignedScore(request.Signals.VolumeTrendScore),
            ScoreForecast(request.Forecast));

        var sentimentScore = Average(
            sentiment.Score,
            SignedScore(request.Signals.SentimentScore),
            SignedScore(request.Signals.AnalystRevisionScore));

        var macroScore = Average(
            SignedScore(request.Signals.MacroScore),
            ScoreForecast(request.Forecast));

        return new FactorScoreBreakdown
        {
            TechnicalScore = Math.Round(technical, 2, MidpointRounding.AwayFromZero),
            FundamentalScore = Math.Round(fundamental, 2, MidpointRounding.AwayFromZero),
            MomentumScore = Math.Round(momentumScore, 2, MidpointRounding.AwayFromZero),
            SentimentScore = Math.Round(sentimentScore, 2, MidpointRounding.AwayFromZero),
            MacroScore = Math.Round(macroScore, 2, MidpointRounding.AwayFromZero)
        };
    }

    private static List<string> BuildTopFactors(
        IReadOnlyCollection<StrategyComponentScore> strategies,
        FactorScoreBreakdown factorBreakdown,
        StrategyEvaluationRequest request)
    {
        var factors = new List<(decimal Score, string Text)>
        {
            (factorBreakdown.TechnicalScore, $"Technical composite {factorBreakdown.TechnicalScore:F1}"),
            (factorBreakdown.FundamentalScore, $"Fundamental composite {factorBreakdown.FundamentalScore:F1}"),
            (factorBreakdown.MomentumScore, $"Momentum composite {factorBreakdown.MomentumScore:F1}"),
            (factorBreakdown.SentimentScore, $"Sentiment composite {factorBreakdown.SentimentScore:F1}"),
            (factorBreakdown.MacroScore, $"Macro composite {factorBreakdown.MacroScore:F1}"),
            (request.Forecast.ModelConfidenceScore, $"Forecast confidence {request.Forecast.ModelConfidenceScore:F1}")
        };

        factors.AddRange(strategies.Select(x => (x.Score, $"{x.Strategy} strategy score {x.Score:F1}")));

        return factors
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select(x => x.Text)
            .ToList();
    }

    private static RecommendationRating GetRating(decimal finalScore)
    {
        return finalScore switch
        {
            < 40m => RecommendationRating.StrongSell,
            < 55m => RecommendationRating.Sell,
            < 65m => RecommendationRating.Hold,
            < 80m => RecommendationRating.Buy,
            _ => RecommendationRating.StrongBuy
        };
    }

    private static string GetRatingLabel(RecommendationRating rating)
    {
        return rating switch
        {
            RecommendationRating.StrongSell => "Strong Sell",
            RecommendationRating.Sell => "Sell",
            RecommendationRating.Hold => "Hold",
            RecommendationRating.Buy => "Buy",
            RecommendationRating.StrongBuy => "Strong Buy",
            _ => "Hold"
        };
    }

    private static decimal ScoreForecast(ForecastSnapshot forecast)
    {
        var returnComposite =
            LinearScore(forecast.OneDayReturnPercent, -5m, 5m) * 0.20m +
            LinearScore(forecast.FiveDayReturnPercent, -10m, 10m) * 0.30m +
            LinearScore(forecast.ThirtyDayReturnPercent, -15m, 20m) * 0.25m +
            LinearScore(forecast.NinetyDayReturnPercent, -20m, 30m) * 0.25m;

        return Clamp(Math.Round((returnComposite * 0.75m) + (forecast.ModelConfidenceScore * 0.25m), 2, MidpointRounding.AwayFromZero));
    }

    private static decimal CalculateRiskReward(OrderSide side, decimal entryPrice, decimal stopLoss, decimal takeProfit)
    {
        var risk = side == OrderSide.Buy ? entryPrice - stopLoss : stopLoss - entryPrice;
        var reward = side == OrderSide.Buy ? takeProfit - entryPrice : entryPrice - takeProfit;

        if (risk <= 0m || reward <= 0m)
        {
            return 0m;
        }

        return Math.Round(reward / risk, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal PercentageDiffScore(decimal left, decimal right, decimal minPercent, decimal maxPercent)
    {
        var percentageDiff = right == 0m ? 0m : ((left - right) / right) * 100m;
        return LinearScore(percentageDiff, minPercent, maxPercent);
    }

    private static decimal SignedScore(decimal value)
    {
        return Clamp((value + 100m) / 2m);
    }

    private static decimal LinearScore(decimal value, decimal min, decimal max)
    {
        if (min >= max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "min must be less than max.");
        }

        var scaled = ((value - min) / (max - min)) * 100m;
        return Clamp(scaled);
    }

    private static decimal Average(params decimal[] values)
    {
        return values.Length == 0 ? 0m : Math.Round(values.Average(), 2, MidpointRounding.AwayFromZero);
    }

    private static decimal Clamp(decimal value)
    {
        return Math.Min(100m, Math.Max(0m, value));
    }
}
