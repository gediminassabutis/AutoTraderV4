namespace AutoTraderV4;

public sealed class PortfolioService
{
    private readonly IPortfolioRepository _repository;
    private readonly RiskGovernanceService _riskGovernanceService;

    public PortfolioService(IPortfolioRepository repository, RiskGovernanceService riskGovernanceService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _riskGovernanceService = riskGovernanceService ?? throw new ArgumentNullException(nameof(riskGovernanceService));
    }

    public async Task<PortfolioSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        var state = await _repository.GetPortfolioStateAsync(cancellationToken);

        var totalPortfolioValue = state?.TotalValue > 0m
            ? state.TotalValue
            : positions.Sum(x => ResolveMarketValue(x)) + (state?.Cash ?? 0m);
        var totalExposureValue = positions.Sum(ResolveMarketValue);
        var exposurePercent = totalPortfolioValue <= 0m ? 0m : Math.Round(totalExposureValue / totalPortfolioValue * 100m, 2, MidpointRounding.AwayFromZero);

        var responsePositions = positions
            .OrderBy(x => x.Ticker)
            .Select(x =>
            {
                var marketValue = ResolveMarketValue(x);
                return new PortfolioPositionResponse
                {
                    Ticker = x.Ticker,
                    Sector = x.Sector,
                    Quantity = x.Quantity,
                    AveragePrice = x.AveragePrice,
                    CurrentPrice = x.CurrentPrice,
                    Currency = x.Currency,
                    MarketValue = marketValue,
                    UnrealizedProfitLoss = Math.Round((x.CurrentPrice - x.AveragePrice) * x.Quantity, 2, MidpointRounding.AwayFromZero),
                    ExposurePercent = totalPortfolioValue <= 0m ? 0m : Math.Round(marketValue / totalPortfolioValue * 100m, 2, MidpointRounding.AwayFromZero),
                    StopLoss = x.StopLoss,
                    TakeProfit = x.TakeProfit,
                    ConfidenceScore = x.ConfidenceScore,
                    UpdatedAtUtc = x.UpdatedAtUtc
                };
            })
            .ToList();

        return new PortfolioSnapshotResponse
        {
            Summary = new PortfolioSummaryResponse
            {
                TotalPortfolioValue = totalPortfolioValue,
                DailyProfitLoss = state?.DailyProfitLoss ?? 0m,
                WeeklyProfitLoss = state?.WeeklyProfitLoss ?? 0m,
                MonthlyProfitLoss = state?.MonthlyProfitLoss ?? 0m,
                AvailableCash = state?.Cash ?? 0m,
                TotalExposurePercent = exposurePercent,
                DefensiveModeActive = state?.DefensiveModeActive ?? false
            },
            Risk = _riskGovernanceService.EvaluatePortfolioHealth(state, positions),
            Positions = responsePositions
        };
    }

    private static decimal ResolveMarketValue(PortfolioPosition position)
    {
        var effectivePrice = position.CurrentPrice > 0m ? position.CurrentPrice : position.AveragePrice;
        return Math.Abs(position.Quantity) * effectivePrice;
    }
}
