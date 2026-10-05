using Microsoft.Extensions.Logging;

namespace AutoTraderV4.Services;

public sealed class StockTrackingCycleResult
{
    public MarketDataReadiness MarketDataReadiness { get; init; } = new();
    public IReadOnlyList<string> UpdatedTickers { get; init; } = [];
    public IReadOnlyList<PortfolioReviewDecision> SellRecommendations { get; init; } = [];
    public IReadOnlyList<BuyOpportunityDecision> BuyOpportunities { get; init; } = [];
}

public sealed class StockTrackingService
{
    private readonly IPortfolioRepository _repository;
    private readonly PortfolioStateSyncService _portfolioStateSyncService;
    private readonly MarketDataService _marketDataService;
    private readonly PortfolioReviewService _portfolioReviewService;
    private readonly BuyOpportunityService _buyOpportunityService;
    private readonly ILogger<StockTrackingService> _logger;

    public StockTrackingService(
        IPortfolioRepository repository,
        PortfolioStateSyncService portfolioStateSyncService,
        MarketDataService marketDataService,
        PortfolioReviewService portfolioReviewService,
        BuyOpportunityService buyOpportunityService,
        ILogger<StockTrackingService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _portfolioStateSyncService = portfolioStateSyncService ?? throw new ArgumentNullException(nameof(portfolioStateSyncService));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _portfolioReviewService = portfolioReviewService ?? throw new ArgumentNullException(nameof(portfolioReviewService));
        _buyOpportunityService = buyOpportunityService ?? throw new ArgumentNullException(nameof(buyOpportunityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StockTrackingCycleResult> RunCycleAsync(CancellationToken cancellationToken = default)
    {
        await _portfolioStateSyncService.EnsureFreshAsync(cancellationToken);
        var marketDataReadiness = _marketDataService.GetReadiness();
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        if (!marketDataReadiness.LiveProviderConfigured)
        {
            _logger.LogWarning(
                "Stock price tracking is non-actionable. {MarketDataMessage}",
                marketDataReadiness.Message);

            return new StockTrackingCycleResult
            {
                MarketDataReadiness = marketDataReadiness,
                SellRecommendations = await _portfolioReviewService.ReviewOpenPositionsAsync(cancellationToken)
            };
        }

        var updatedTickers = new List<string>();

        foreach (var position in positions)
        {
            var ingestion = await _marketDataService.IngestAsync(position.Ticker, cancellationToken);
            if (!ingestion.Success
                || ingestion.FreshnessStatus != DataFreshnessStatus.Fresh
                || ingestion.Data is null)
            {
                _logger.LogWarning(
                    "Keeping the last known price for {Ticker}; fresh market data is unavailable. Provider failures: {Failures}",
                    position.Ticker,
                    string.Join("; ", ingestion.Failures));
                continue;
            }

            var marketData = ingestion.Data;
            if (marketData.IsSynthetic)
            {
                _logger.LogDebug(
                    "Keeping the last known price for {Ticker}; {Provider} returned synthetic market data.",
                    position.Ticker,
                    marketData.ProviderName);
                continue;
            }

            if (marketData.Price <= 0m)
            {
                _logger.LogWarning(
                    "Keeping the last known price for {Ticker}; the provider returned a non-positive price.",
                    position.Ticker);
                continue;
            }

            position.CurrentPrice = marketData.Price;
            position.UpdatedAtUtc = marketData.TimestampUtc;
            await _repository.UpsertPositionAsync(position, cancellationToken);
            updatedTickers.Add(position.Ticker);
        }

        var sellRecommendations = await _portfolioReviewService.ReviewOpenPositionsAsync(cancellationToken);
        var buyOpportunities = await _buyOpportunityService.ScanForBuysAsync(cancellationToken);

        return new StockTrackingCycleResult
        {
            MarketDataReadiness = marketDataReadiness,
            UpdatedTickers = updatedTickers,
            SellRecommendations = sellRecommendations,
            BuyOpportunities = buyOpportunities
        };
    }
}
