namespace AutoTraderV4.Services;

[Obsolete("Use AutoTraderV4.PortfolioRiskService for new code.")]
public sealed class PortfolioRiskService
{
    private readonly global::AutoTraderV4.PortfolioRiskService _inner;

    public PortfolioRiskService() : this(new global::AutoTraderV4.PortfolioRiskPolicy())
    {
    }

    public PortfolioRiskService(global::AutoTraderV4.PortfolioRiskPolicy policy)
    {
        _inner = new global::AutoTraderV4.PortfolioRiskService(policy ?? new global::AutoTraderV4.PortfolioRiskPolicy());
    }

    public global::AutoTraderV4.RiskCheckResult Evaluate(PortfolioDashboard dashboard, TradeDecision? decision = null)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        return _inner.Evaluate(dashboard, decision);
    }

    public global::AutoTraderV4.PortfolioRiskAssessment EvaluateTrade(global::AutoTraderV4.TradeRiskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _inner.EvaluateTrade(request);
    }

    public global::AutoTraderV4.PortfolioRiskAssessment EvaluateTrade(
        decimal portfolioValue,
        decimal availableCash,
        decimal proposedPositionValue,
        decimal existingExposureValue = 0m,
        decimal sectorExposureValue = 0m,
        decimal dailyPortfolioLoss = 0m,
        decimal portfolioDrawdownPct = 0m)
    {
        return _inner.EvaluateTrade(
            portfolioValue,
            availableCash,
            proposedPositionValue,
            existingExposureValue,
            sectorExposureValue,
            dailyPortfolioLoss,
            portfolioDrawdownPct);
    }

    public global::AutoTraderV4.TradeReductionPlan BuildTradeReductionPlan(
        decimal portfolioValue,
        decimal availableCash,
        decimal proposedPositionValue,
        decimal existingExposureValue = 0m,
        decimal sectorExposureValue = 0m,
        decimal dailyPortfolioLoss = 0m,
        decimal portfolioDrawdownPct = 0m)
    {
        return _inner.BuildTradeReductionPlan(
            portfolioValue,
            availableCash,
            proposedPositionValue,
            existingExposureValue,
            sectorExposureValue,
            dailyPortfolioLoss,
            portfolioDrawdownPct);
    }
}
