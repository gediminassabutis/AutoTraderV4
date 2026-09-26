namespace AutoTraderV4;

public sealed class UpsertPortfolioStateRequest
{
    public decimal TotalValue { get; set; }
    public decimal Cash { get; set; }
    public decimal DailyProfitLoss { get; set; }
    public decimal WeeklyProfitLoss { get; set; }
    public decimal MonthlyProfitLoss { get; set; }
    public decimal PeakPortfolioValue { get; set; }
    public bool DefensiveModeActive { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, string[]> Validate()
    {
        return ApiValidation.Build(errors =>
        {
            if (TotalValue <= 0m)
            {
                errors.AddError(nameof(TotalValue), "TotalValue must be greater than zero.");
            }

            if (Cash < 0m)
            {
                errors.AddError(nameof(Cash), "Cash cannot be negative.");
            }

            if (PeakPortfolioValue <= 0m)
            {
                errors.AddError(nameof(PeakPortfolioValue), "PeakPortfolioValue must be greater than zero.");
            }

            if (Cash > TotalValue)
            {
                errors.AddError(nameof(Cash), "Cash cannot exceed TotalValue.");
            }

            if (PeakPortfolioValue < TotalValue)
            {
                errors.AddError(nameof(PeakPortfolioValue), "PeakPortfolioValue must be greater than or equal to TotalValue.");
            }

            if (UpdatedAtUtc == default)
            {
                errors.AddError(nameof(UpdatedAtUtc), "UpdatedAtUtc is required.");
            }
        });
    }

    public PortfolioStateRecord ToEntity()
    {
        return new PortfolioStateRecord
        {
            TotalValue = TotalValue,
            Cash = Cash,
            DailyProfitLoss = DailyProfitLoss,
            WeeklyProfitLoss = WeeklyProfitLoss,
            MonthlyProfitLoss = MonthlyProfitLoss,
            PeakPortfolioValue = PeakPortfolioValue,
            DefensiveModeActive = DefensiveModeActive,
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}

public sealed class UpsertPortfolioPositionRequest
{
    public string Ticker { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal ConfidenceScore { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, string[]> Validate()
    {
        return ApiValidation.Build(errors =>
        {
            if (string.IsNullOrWhiteSpace(Ticker))
            {
                errors.AddError(nameof(Ticker), "Ticker is required.");
            }
            else if (Ticker.Trim().Length > 64)
            {
                errors.AddError(nameof(Ticker), "Ticker must be 64 characters or fewer.");
            }

            if (string.IsNullOrWhiteSpace(Sector))
            {
                errors.AddError(nameof(Sector), "Sector is required.");
            }
            else if (Sector.Trim().Length > 64)
            {
                errors.AddError(nameof(Sector), "Sector must be 64 characters or fewer.");
            }

            if (Quantity <= 0m)
            {
                errors.AddError(nameof(Quantity), "Quantity must be greater than zero.");
            }

            if (AveragePrice <= 0m)
            {
                errors.AddError(nameof(AveragePrice), "AveragePrice must be greater than zero.");
            }

            if (CurrentPrice <= 0m)
            {
                errors.AddError(nameof(CurrentPrice), "CurrentPrice must be greater than zero.");
            }

            if (string.IsNullOrWhiteSpace(Currency))
            {
                errors.AddError(nameof(Currency), "Currency is required.");
            }
            else if (Currency.Trim().Length > 10)
            {
                errors.AddError(nameof(Currency), "Currency must be 10 characters or fewer.");
            }

            if (StopLoss <= 0m)
            {
                errors.AddError(nameof(StopLoss), "StopLoss must be greater than zero.");
            }

            if (TakeProfit <= 0m)
            {
                errors.AddError(nameof(TakeProfit), "TakeProfit must be greater than zero.");
            }

            if (ConfidenceScore < 0m || ConfidenceScore > 100m)
            {
                errors.AddError(nameof(ConfidenceScore), "ConfidenceScore must be between 0 and 100.");
            }

            if (UpdatedAtUtc == default)
            {
                errors.AddError(nameof(UpdatedAtUtc), "UpdatedAtUtc is required.");
            }
        });
    }

    public PortfolioPosition ToEntity()
    {
        return new PortfolioPosition
        {
            Ticker = Ticker,
            Sector = Sector,
            Quantity = Quantity,
            AveragePrice = AveragePrice,
            CurrentPrice = CurrentPrice,
            Currency = Currency,
            StopLoss = StopLoss,
            TakeProfit = TakeProfit,
            ConfidenceScore = ConfidenceScore,
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}

public sealed class PortfolioPositionResponse
{
    public string Ticker { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal MarketValue { get; set; }
    public decimal UnrealizedProfitLoss { get; set; }
    public decimal ExposurePercent { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal ConfidenceScore { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class PortfolioSummaryResponse
{
    public decimal TotalPortfolioValue { get; set; }
    public decimal DailyProfitLoss { get; set; }
    public decimal WeeklyProfitLoss { get; set; }
    public decimal MonthlyProfitLoss { get; set; }
    public decimal AvailableCash { get; set; }
    public decimal TotalExposurePercent { get; set; }
    public bool DefensiveModeActive { get; set; }
}

public sealed class PortfolioSnapshotResponse
{
    public PortfolioSummaryResponse Summary { get; set; } = new();
    public RiskAssessmentResult Risk { get; set; } = new();
    public List<PortfolioPositionResponse> Positions { get; set; } = [];
}

public sealed class TradeAuditLogResponse
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal FinalScore { get; set; }
    public decimal ConfidenceScore { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal Quantity { get; set; }
    public string Side { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public string SignalScoresJson { get; set; } = string.Empty;
    public string ForecastJson { get; set; } = string.Empty;
    public string RiskAssessmentJson { get; set; } = string.Empty;
    public string RecommendationJson { get; set; } = string.Empty;
}
