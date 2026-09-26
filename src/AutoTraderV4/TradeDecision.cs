using System.Text.Json;

namespace AutoTraderV4;

public enum OrderSide
{
    Buy,
    Sell
}

public enum OrderExecutionStatus
{
    Created,
    Validated,
    Approved,
    Submitted,
    Accepted,
    Rejected,
    Filled,
    Cancelled,
    Closed
}

public sealed class TradeDecision
{
    public string Ticker { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public Trading212OrderType OrderType { get; set; }
    public decimal ConfidenceScore { get; set; } = 75m;
    public int Confidence
    {
        get => (int)Math.Round(ConfidenceScore, MidpointRounding.AwayFromZero);
        set => ConfidenceScore = value;
    }
    public string Rating { get; set; } = "Hold";
    public RecommendationRating RecommendationRating { get; set; } = RecommendationRating.Hold;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal RiskReward { get; set; }
    public List<string> TopFactors { get; set; } = [];
    public string TriggeringStrategy { get; set; } = "weighted-strategy";
    public string Strategy
    {
        get => TriggeringStrategy;
        set => TriggeringStrategy = value;
    }
    public string RiskAssessment { get; set; } = string.Empty;
    public string ForecastOutput { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string RecommendationSummary
    {
        get => Recommendation;
        set => Recommendation = value;
    }
    public decimal FinalScore { get; set; }
    public decimal CombinedStrategyScore { get; set; }
    public Dictionary<string, decimal> SignalScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public FactorScoreBreakdown FactorBreakdown { get; set; } = new();
    public List<StrategyComponentScore> StrategyBreakdown { get; set; } = [];
    public ForecastSnapshot Forecast { get; set; } = new();
    public bool EligibleForExecution { get; set; }
    public OrderExecutionStatus ExecutionStatus { get; set; } = OrderExecutionStatus.Created;
    public string OrderStatus
    {
        get => ExecutionStatus.ToString();
        set => ExecutionStatus = Enum.TryParse<OrderExecutionStatus>(value, true, out var status) ? status : OrderExecutionStatus.Created;
    }
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset TimestampUtc
    {
        get => GeneratedAtUtc == default ? CreatedUtc : GeneratedAtUtc;
        set
        {
            GeneratedAtUtc = value;
            CreatedUtc = value;
        }
    }
    public DateTimeOffset LoggedAtUtc
    {
        get => TimestampUtc;
        set => TimestampUtc = value;
    }

    public void ApplyAuditMetadata(
        string strategy,
        Dictionary<string, decimal>? signalScores,
        string forecastOutput,
        string riskAssessment,
        string recommendation)
    {
        TimestampUtc = DateTimeOffset.UtcNow;
        Strategy = strategy;
        if (signalScores is not null && signalScores.Count > 0)
        {
            SignalScores = signalScores;
        }

        ForecastOutput = string.IsNullOrWhiteSpace(forecastOutput)
            ? BuildForecastOutput()
            : forecastOutput;

        RiskAssessment = string.IsNullOrWhiteSpace(riskAssessment) ? "Within trading constraints" : riskAssessment;
        Recommendation = string.IsNullOrWhiteSpace(recommendation) ? RecommendationSummary : recommendation;
        RecommendationSummary = Recommendation;
    }

    public string BuildForecastOutput()
    {
        if (!string.IsNullOrWhiteSpace(ForecastOutput))
        {
            return ForecastOutput;
        }

        if (Forecast == null)
        {
            return "{}";
        }

        var payload = new
        {
            oneDayReturnPercent = Forecast.OneDayReturnPercent,
            fiveDayReturnPercent = Forecast.FiveDayReturnPercent,
            thirtyDayReturnPercent = Forecast.ThirtyDayReturnPercent,
            ninetyDayReturnPercent = Forecast.NinetyDayReturnPercent,
            modelConfidenceScore = Forecast.ModelConfidenceScore
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public OrderExecutionRecord ToOrderExecutionRecord()
    {
        return new OrderExecutionRecord
        {
            Id = Guid.NewGuid(),
            Ticker = Ticker,
            Side = Side.ToString(),
            Status = ExecutionStatus.ToString(),
            StrategyName = TriggeringStrategy,
            Quantity = Quantity,
            EntryPrice = EntryPrice,
            StopLoss = StopLoss,
            TakeProfit = TakeProfit,
            FinalScore = FinalScore,
            ConfidenceScore = ConfidenceScore,
            SignalScoresJson = SignalScores.Count > 0
                ? JsonSerializer.Serialize(SignalScores)
                : "{}",
            ForecastJson = string.IsNullOrWhiteSpace(ForecastOutput)
                ? BuildForecastOutput()
                : ForecastOutput,
            RiskAssessmentJson = string.IsNullOrWhiteSpace(RiskAssessment)
                ? JsonSerializer.Serialize("Within trading constraints")
                : JsonSerializer.Serialize(RiskAssessment),
            RecommendationJson = string.IsNullOrWhiteSpace(Recommendation)
                ? JsonSerializer.Serialize(RecommendationSummary)
                : JsonSerializer.Serialize(Recommendation),
            CreatedUtc = TimestampUtc == default ? DateTimeOffset.UtcNow : TimestampUtc,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

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

        if (EntryPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(EntryPrice), "EntryPrice cannot be negative.");
        }

        if (StopLoss < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(StopLoss), "StopLoss cannot be negative.");
        }

        if (TakeProfit < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(TakeProfit), "TakeProfit cannot be negative.");
        }

        if (ConfidenceScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(ConfidenceScore), "ConfidenceScore must be between 0 and 100.");
        }

        if (Confidence is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(Confidence), "Confidence must be between 0 and 100.");
        }

        if (EntryPrice > 0m && StopLoss > 0m && TakeProfit > 0m)
        {
            if (Side == OrderSide.Buy && !(StopLoss < EntryPrice && TakeProfit > EntryPrice))
            {
                throw new ArgumentException("Buy decisions must have StopLoss below EntryPrice and TakeProfit above EntryPrice.", nameof(StopLoss));
            }

            if (Side == OrderSide.Sell && !(StopLoss > EntryPrice && TakeProfit < EntryPrice))
            {
                throw new ArgumentException("Sell decisions must have StopLoss above EntryPrice and TakeProfit below EntryPrice.", nameof(StopLoss));
            }
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
