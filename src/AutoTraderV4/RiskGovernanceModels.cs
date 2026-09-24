namespace AutoTraderV4;

public sealed class RiskContextSnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public decimal LiquidityScore { get; set; }
    public decimal SpreadPercent { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, string[]> Validate()
    {
        return ApiValidation.Build(errors =>
        {
            if (string.IsNullOrWhiteSpace(Symbol))
            {
                errors.AddError(nameof(Symbol), "Symbol is required.");
            }

            if (string.IsNullOrWhiteSpace(Sector))
            {
                errors.AddError(nameof(Sector), "Sector is required.");
            }

            if (LiquidityScore < 0m || LiquidityScore > 100m)
            {
                errors.AddError(nameof(LiquidityScore), "LiquidityScore must be between 0 and 100.");
            }

            if (SpreadPercent < 0m || SpreadPercent > 25m)
            {
                errors.AddError(nameof(SpreadPercent), "SpreadPercent must be between 0 and 25.");
            }

            if (ObservedAtUtc == default)
            {
                errors.AddError(nameof(ObservedAtUtc), "ObservedAtUtc is required.");
            }
        });
    }
}

public sealed class RiskEvaluationRequest
{
    public TradeDecision CandidateTrade { get; set; } = new();
    public RiskContextSnapshot Context { get; set; } = new();

    public Dictionary<string, string[]> Validate()
    {
        return ApiValidation.Build(errors =>
        {
            try
            {
                CandidateTrade.Validate();
            }
            catch (ArgumentOutOfRangeException exception)
            {
                errors.AddError(nameof(CandidateTrade), exception.Message);
            }
            catch (ArgumentException exception)
            {
                errors.AddError(nameof(CandidateTrade), exception.Message);
            }

            foreach (var error in Context.Validate())
            {
                foreach (var message in error.Value)
                {
                    errors.AddError($"{nameof(Context)}.{error.Key}", message);
                }
            }
        });
    }
}

public sealed class RiskAssessmentResult
{
    public bool Approved { get; set; }
    public bool DefensiveModeActivated { get; set; }
    public decimal CurrentExposurePercent { get; set; }
    public decimal ProjectedExposurePercent { get; set; }
    public decimal ProjectedCashPercent { get; set; }
    public decimal ProjectedSectorExposurePercent { get; set; }
    public decimal DailyLossPercent { get; set; }
    public decimal DrawdownPercent { get; set; }
    public List<string> Violations { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
}
