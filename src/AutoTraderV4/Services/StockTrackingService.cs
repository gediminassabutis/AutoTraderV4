using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AutoTraderV4.Services;

public sealed class StockTrackingCycleResult
{
    public MarketDataReadiness MarketDataReadiness { get; init; } = new();
    public IReadOnlyList<string> UpdatedTickers { get; init; } = [];
    public IReadOnlyList<PortfolioReviewDecision> SellRecommendations { get; init; } = [];
    public IReadOnlyList<BuyOpportunityDecision> BuyOpportunities { get; init; } = [];
    public IReadOnlyList<Trading212TradableInstrument> NewStocks { get; init; } = [];
    public string? NewStockDiscoveryError { get; init; }
}

public sealed class StockTrackingCycleGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<StockTrackingCycleResult> RunAsync(
        Func<CancellationToken, Task<StockTrackingCycleResult>> runCycleAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runCycleAsync);
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await runCycleAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

public sealed class StockTrackingService
{
    private const int MaximumDiscoveredStocks = 20;
    private const string StockDiscoveryErrorMessage =
        "Stock discovery failed. Verify Trading 212 API credentials and metadata access.";

    private readonly IPortfolioRepository _repository;
    private readonly ITrading212Client _trading212Client;
    private readonly PortfolioStateSyncService _portfolioStateSyncService;
    private readonly MarketDataService _marketDataService;
    private readonly PortfolioReviewService _portfolioReviewService;
    private readonly BuyOpportunityService _buyOpportunityService;
    private readonly ILogger<StockTrackingService> _logger;
    private readonly StockTrackingCycleGate _cycleGate;

    public StockTrackingService(
        IPortfolioRepository repository,
        ITrading212Client trading212Client,
        PortfolioStateSyncService portfolioStateSyncService,
        MarketDataService marketDataService,
        PortfolioReviewService portfolioReviewService,
        BuyOpportunityService buyOpportunityService,
        ILogger<StockTrackingService> logger,
        StockTrackingCycleGate cycleGate)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _trading212Client = trading212Client ?? throw new ArgumentNullException(nameof(trading212Client));
        _portfolioStateSyncService = portfolioStateSyncService ?? throw new ArgumentNullException(nameof(portfolioStateSyncService));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _portfolioReviewService = portfolioReviewService ?? throw new ArgumentNullException(nameof(portfolioReviewService));
        _buyOpportunityService = buyOpportunityService ?? throw new ArgumentNullException(nameof(buyOpportunityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cycleGate = cycleGate ?? throw new ArgumentNullException(nameof(cycleGate));
    }

    public Task<StockTrackingCycleResult> RunCycleAsync(CancellationToken cancellationToken = default)
    {
        return _cycleGate.RunAsync(RunCycleCoreAsync, cancellationToken);
    }

    private async Task<StockTrackingCycleResult> RunCycleCoreAsync(CancellationToken cancellationToken)
    {
        await _portfolioStateSyncService.EnsureFreshAsync(cancellationToken);
        var marketDataReadiness = _marketDataService.GetReadiness();
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        var stockDiscovery = await DiscoverNewStocksAsync(positions, cancellationToken);
        if (!marketDataReadiness.LiveProviderConfigured)
        {
            _logger.LogWarning(
                "Stock price tracking is non-actionable. {MarketDataMessage}",
                marketDataReadiness.Message);

            return new StockTrackingCycleResult
            {
                MarketDataReadiness = marketDataReadiness,
                SellRecommendations = await _portfolioReviewService.ReviewOpenPositionsAsync(cancellationToken),
                NewStocks = stockDiscovery.Stocks,
                NewStockDiscoveryError = stockDiscovery.Error
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
            BuyOpportunities = buyOpportunities,
            NewStocks = stockDiscovery.Stocks,
            NewStockDiscoveryError = stockDiscovery.Error
        };
    }

    private async Task<(IReadOnlyList<Trading212TradableInstrument> Stocks, string? Error)> DiscoverNewStocksAsync(
        IReadOnlyCollection<PortfolioPosition> positions,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Trading212TradableInstrument> instruments;
        try
        {
            instruments = await _trading212Client.GetAvailableInstrumentsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or JsonException
                or InvalidOperationException
                or IOException
                or OperationCanceledException)
        {
            _logger.LogWarning(exception, "Stock discovery failed. Verify Trading 212 metadata access.");
            return ([], StockDiscoveryErrorMessage);
        }

        var alreadyOrderedTickers = new HashSet<string>(
            positions.Select(position => NormalizeTicker(position.Ticker)),
            StringComparer.OrdinalIgnoreCase);
        alreadyOrderedTickers.UnionWith(
            (await _repository.GetPreviouslyOrderedTickersAsync(cancellationToken))
                .Select(NormalizeTicker));

        var stocks = instruments
            .Where(instrument => string.Equals(instrument.Type, "STOCK", StringComparison.OrdinalIgnoreCase))
            .Where(instrument => !string.IsNullOrWhiteSpace(instrument.Ticker))
            .Where(instrument => !alreadyOrderedTickers.Contains(NormalizeTicker(instrument.Ticker)))
            .GroupBy(instrument => instrument.Ticker.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(instrument => instrument.AddedOn ?? DateTimeOffset.MinValue)
                .ThenBy(instrument => instrument.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(instrument => instrument.AddedOn ?? DateTimeOffset.MinValue)
            .ThenBy(instrument => instrument.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumDiscoveredStocks)
            .ToArray();

        return (stocks, null);
    }

    private static string NormalizeTicker(string? ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return string.Empty;
        }

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var marketSuffixSeparator = normalizedTicker.IndexOf('_');
        return marketSuffixSeparator > 0
            ? normalizedTicker[..marketSuffixSeparator]
            : normalizedTicker;
    }
}
