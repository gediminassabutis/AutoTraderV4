namespace AutoTraderV4;

public enum RecommendationRating
{
    StrongSell,
    Sell,
    Hold,
    Buy,
    StrongBuy
}

public sealed class StrategyEvaluationRequest
{
    public string Symbol { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal SuggestedQuantity { get; set; }
    public Trading212OrderType OrderType { get; set; } = Trading212OrderType.Market;
    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public MarketSignalSnapshot Signals { get; set; } = new();
    public ForecastSnapshot Forecast { get; set; } = new();

    public Dictionary<string, string[]> Validate()
    {
        return ApiValidation.Build(errors =>
        {
            if (string.IsNullOrWhiteSpace(Symbol))
            {
                errors.AddError(nameof(Symbol), "Symbol is required.");
            }
            else if (Symbol.Trim().Length > 64)
            {
                errors.AddError(nameof(Symbol), "Symbol must be 64 characters or fewer.");
            }

            if (string.IsNullOrWhiteSpace(Sector))
            {
                errors.AddError(nameof(Sector), "Sector is required.");
            }
            else if (Sector.Trim().Length > 64)
            {
                errors.AddError(nameof(Sector), "Sector must be 64 characters or fewer.");
            }

            if (CurrentPrice <= 0m)
            {
                errors.AddError(nameof(CurrentPrice), "CurrentPrice must be greater than zero.");
            }

            if (SuggestedQuantity <= 0m)
            {
                errors.AddError(nameof(SuggestedQuantity), "SuggestedQuantity must be greater than zero.");
            }

            if (ObservedAtUtc == default)
            {
                errors.AddError(nameof(ObservedAtUtc), "ObservedAtUtc is required.");
            }

            foreach (var error in Signals.Validate(nameof(Signals)))
            {
                foreach (var message in error.Value)
                {
                    errors.AddError(error.Key, message);
                }
            }

            foreach (var error in Forecast.Validate(nameof(Forecast)))
            {
                foreach (var message in error.Value)
                {
                    errors.AddError(error.Key, message);
                }
            }
        });
    }

    public RiskContextSnapshot ToRiskContext()
    {
        return new RiskContextSnapshot
        {
            Symbol = Symbol,
            Sector = Sector,
            LiquidityScore = Signals.LiquidityScore,
            SpreadPercent = Signals.SpreadPercent,
            ObservedAtUtc = ObservedAtUtc
        };
    }
}

public sealed class MarketSignalSnapshot
{
    public decimal FiftyDayMovingAverage { get; set; }
    public decimal TwoHundredDayMovingAverage { get; set; }
    public decimal RsiValue { get; set; }
    public decimal RsiTrendScore { get; set; }
    public decimal VolumeTrendScore { get; set; }
    public decimal RelativeStrengthPercentile { get; set; }
    public decimal EarningsGrowthPercent { get; set; }
    public decimal RevenueGrowthPercent { get; set; }
    public decimal FreeCashFlowMarginPercent { get; set; }
    public decimal DebtToEquityRatio { get; set; }
    public decimal ReturnOnEquityPercent { get; set; }
    public decimal PegRatio { get; set; }
    public decimal EarningsSurprisePercent { get; set; }
    public decimal GuidanceChangeScore { get; set; }
    public decimal AnalystRevisionScore { get; set; }
    public decimal SentimentScore { get; set; }
    public decimal MacroScore { get; set; }
    public decimal QualityScore { get; set; }
    public decimal PriceDislocationPercent { get; set; }
    public decimal LiquidityScore { get; set; }
    public decimal SpreadPercent { get; set; }
    public decimal AtrPercent { get; set; }

    public Dictionary<string, string[]> Validate(string prefix)
    {
        return ApiValidation.Build(errors =>
        {
            if (FiftyDayMovingAverage <= 0m)
            {
                errors.AddError($"{prefix}.{nameof(FiftyDayMovingAverage)}", "FiftyDayMovingAverage must be greater than zero.");
            }

            if (TwoHundredDayMovingAverage <= 0m)
            {
                errors.AddError($"{prefix}.{nameof(TwoHundredDayMovingAverage)}", "TwoHundredDayMovingAverage must be greater than zero.");
            }

            ValidateRange(errors, $"{prefix}.{nameof(RsiValue)}", RsiValue, 0m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(RsiTrendScore)}", RsiTrendScore, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(VolumeTrendScore)}", VolumeTrendScore, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(RelativeStrengthPercentile)}", RelativeStrengthPercentile, 0m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(GuidanceChangeScore)}", GuidanceChangeScore, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(AnalystRevisionScore)}", AnalystRevisionScore, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(SentimentScore)}", SentimentScore, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(MacroScore)}", MacroScore, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(QualityScore)}", QualityScore, 0m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(LiquidityScore)}", LiquidityScore, 0m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(SpreadPercent)}", SpreadPercent, 0m, 25m);
            ValidateRange(errors, $"{prefix}.{nameof(AtrPercent)}", AtrPercent, 0m, 30m);
            ValidateRange(errors, $"{prefix}.{nameof(DebtToEquityRatio)}", DebtToEquityRatio, 0m, 20m);
            ValidateRange(errors, $"{prefix}.{nameof(PegRatio)}", PegRatio, 0m, 10m);
        });
    }

    private static void ValidateRange(Dictionary<string, List<string>> errors, string key, decimal value, decimal min, decimal max)
    {
        if (value < min || value > max)
        {
            errors.AddError(key, $"{key[(key.LastIndexOf('.') + 1)..]} must be between {min} and {max}.");
        }
    }
}

public sealed class ForecastModelOutput
{
    public string ModelName { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal ForecastPercent { get; set; }
    public decimal Confidence { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

public sealed class ForecastSnapshot
{
    public decimal OneDayReturnPercent { get; set; }
    public decimal FiveDayReturnPercent { get; set; }
    public decimal ThirtyDayReturnPercent { get; set; }
    public decimal NinetyDayReturnPercent { get; set; }
    public decimal ModelConfidenceScore { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<ForecastModelOutput> ModelOutputs { get; set; } = [];

    public Dictionary<string, string[]> Validate(string prefix)
    {
        return ApiValidation.Build(errors =>
        {
            ValidateRange(errors, $"{prefix}.{nameof(ModelConfidenceScore)}", ModelConfidenceScore, 0m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(OneDayReturnPercent)}", OneDayReturnPercent, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(FiveDayReturnPercent)}", FiveDayReturnPercent, -100m, 100m);
            ValidateRange(errors, $"{prefix}.{nameof(ThirtyDayReturnPercent)}", ThirtyDayReturnPercent, -100m, 200m);
            ValidateRange(errors, $"{prefix}.{nameof(NinetyDayReturnPercent)}", NinetyDayReturnPercent, -100m, 300m);
        });
    }

    private static void ValidateRange(Dictionary<string, List<string>> errors, string key, decimal value, decimal min, decimal max)
    {
        if (value < min || value > max)
        {
            errors.AddError(key, $"{key[(key.LastIndexOf('.') + 1)..]} must be between {min} and {max}.");
        }
    }
}

public sealed class StrategyComponentScore
{
    public string Strategy { get; set; } = string.Empty;
    public decimal WeightPercent { get; set; }
    public decimal Score { get; set; }
    public bool Triggered { get; set; }
    public List<string> Reasons { get; set; } = [];
}

public sealed class FactorScoreBreakdown
{
    public decimal TechnicalScore { get; set; }
    public decimal FundamentalScore { get; set; }
    public decimal MomentumScore { get; set; }
    public decimal SentimentScore { get; set; }
    public decimal MacroScore { get; set; }
}

public sealed class StrategyEvaluationResponse
{
    public TradeDecision Decision { get; set; } = new();
    public RiskAssessmentResult RiskAssessment { get; set; } = new();
    public Guid AuditLogId { get; set; }
    public DateTimeOffset EvaluatedAtUtc { get; set; }
}
