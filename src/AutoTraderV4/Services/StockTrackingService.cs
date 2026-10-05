using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoTraderV4.Services;

public sealed class StockTrackingCycleResult
{
    public MarketDataReadiness MarketDataReadiness { get; init; } = new();
    public IReadOnlyList<string> UpdatedTickers { get; init; } = [];
    public int TrackedStockCount { get; init; }
    public int NewlyTrackedStockCount { get; init; }
    public int StockTicksCollected { get; init; }
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
    private const int MaximumReportedNewStocks = 20;
    private const string StockDiscoveryErrorMessage =
        "Stock discovery failed. Verify Trading 212 API credentials and metadata access.";

    private readonly ApplicationDbContext _context;
    private readonly IPortfolioRepository _repository;
    private readonly ITrading212Client _trading212Client;
    private readonly PortfolioStateSyncService _portfolioStateSyncService;
    private readonly MarketDataService _marketDataService;
    private readonly PortfolioReviewService _portfolioReviewService;
    private readonly BuyOpportunityService _buyOpportunityService;
    private readonly ILogger<StockTrackingService> _logger;
    private readonly StockTrackingCycleGate _cycleGate;

    public StockTrackingService(
        ApplicationDbContext context,
        IPortfolioRepository repository,
        ITrading212Client trading212Client,
        PortfolioStateSyncService portfolioStateSyncService,
        MarketDataService marketDataService,
        PortfolioReviewService portfolioReviewService,
        BuyOpportunityService buyOpportunityService,
        ILogger<StockTrackingService> logger,
        StockTrackingCycleGate cycleGate)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
                TrackedStockCount = stockDiscovery.TrackedStockCount,
                NewlyTrackedStockCount = stockDiscovery.NewlyTrackedStockCount,
                NewStockDiscoveryError = stockDiscovery.Error
            };
        }

        var updatedTickers = new List<string>();
        var trackedTickers = await _context.TrackedStocks
            .AsNoTracking()
            .OrderBy(stock => stock.Ticker)
            .Select(stock => stock.Ticker)
            .ToListAsync(cancellationToken);
        var positionsByTicker = positions
            .GroupBy(position => NormalizeMarketTicker(position.Ticker), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var tickersToIngest = positions
            .Select(position => position.Ticker)
            .Concat(trackedTickers)
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
            .Select(NormalizeMarketTicker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var stockTicksCollected = 0;

        foreach (var ticker in tickersToIngest)
        {
            var ingestion = await _marketDataService.IngestAsync(ticker, cancellationToken);
            if (ingestion.PersistedSnapshot is not null && ingestion.Data is { IsSynthetic: false })
            {
                stockTicksCollected++;
            }

            positionsByTicker.TryGetValue(ticker, out var position);
            if (!ingestion.Success
                || ingestion.FreshnessStatus != DataFreshnessStatus.Fresh
                || ingestion.Data is null)
            {
                if (position is not null)
                {
                    _logger.LogWarning(
                        "Keeping the last known price for {Ticker}; fresh market data is unavailable. Provider failures: {Failures}",
                        position.Ticker,
                        string.Join("; ", ingestion.Failures));
                }
                else
                {
                    _logger.LogWarning(
                        "No fresh market tick is available for tracked stock {Ticker}. Provider failures: {Failures}",
                        ticker,
                        string.Join("; ", ingestion.Failures));
                }

                continue;
            }

            var marketData = ingestion.Data;
            if (marketData.IsSynthetic)
            {
                _logger.LogDebug(
                    "Keeping the last known price for {Ticker}; {Provider} returned synthetic market data.",
                    ticker,
                    marketData.ProviderName);
                continue;
            }

            if (marketData.Price <= 0m)
            {
                _logger.LogWarning(
                    "Keeping the last known price for {Ticker}; the provider returned a non-positive price.",
                    ticker);
                continue;
            }

            if (position is null)
            {
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
            TrackedStockCount = stockDiscovery.TrackedStockCount,
            NewlyTrackedStockCount = stockDiscovery.NewlyTrackedStockCount,
            StockTicksCollected = stockTicksCollected,
            SellRecommendations = sellRecommendations,
            BuyOpportunities = buyOpportunities,
            NewStocks = stockDiscovery.Stocks,
            NewStockDiscoveryError = stockDiscovery.Error
        };
    }

    private async Task<(
        IReadOnlyList<Trading212TradableInstrument> Stocks,
        string? Error,
        int TrackedStockCount,
        int NewlyTrackedStockCount)> DiscoverNewStocksAsync(
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
            return (
                [],
                StockDiscoveryErrorMessage,
                await _context.TrackedStocks.CountAsync(cancellationToken),
                0);
        }

        var stockInstruments = instruments
            .Where(instrument => string.Equals(instrument.Type, "STOCK", StringComparison.OrdinalIgnoreCase))
            .Where(instrument => !string.IsNullOrWhiteSpace(instrument.Ticker))
            .Select(NormalizeInstrument)
            .GroupBy(instrument => instrument.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(instrument => instrument.AddedOn ?? DateTimeOffset.MinValue)
                .ThenBy(instrument => instrument.Name, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(instrument => instrument.AddedOn ?? DateTimeOffset.MinValue)
            .ThenBy(instrument => instrument.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingStocks = await _context.TrackedStocks.ToListAsync(cancellationToken);
        var existingByTicker = existingStocks.ToDictionary(
            stock => stock.Ticker,
            StringComparer.OrdinalIgnoreCase);
        var newlyTrackedTickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var observedAtUtc = DateTimeOffset.UtcNow;

        foreach (var instrument in stockInstruments)
        {
            if (existingByTicker.TryGetValue(instrument.Ticker, out var existing))
            {
                UpdateTrackedStock(existing, instrument, observedAtUtc);
                continue;
            }

            _context.TrackedStocks.Add(new TrackedStock
            {
                Ticker = instrument.Ticker,
                Name = GetInstrumentName(instrument),
                ShortName = GetInstrumentShortName(instrument),
                Isin = instrument.Isin.Trim(),
                CurrencyCode = instrument.CurrencyCode.Trim().ToUpperInvariant(),
                InstrumentType = instrument.Type,
                AddedOn = instrument.AddedOn,
                FirstSeenAtUtc = observedAtUtc,
                LastSeenAtUtc = observedAtUtc
            });
            newlyTrackedTickers.Add(instrument.Ticker);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var alreadyOrderedTickers = new HashSet<string>(
            positions.Select(position => NormalizeTicker(position.Ticker)),
            StringComparer.OrdinalIgnoreCase);
        alreadyOrderedTickers.UnionWith(
            (await _repository.GetPreviouslyOrderedTickersAsync(cancellationToken))
                .Select(NormalizeTicker));

        var newStocks = stockInstruments
            .Where(instrument => newlyTrackedTickers.Contains(instrument.Ticker))
            .Where(instrument => !alreadyOrderedTickers.Contains(NormalizeTicker(instrument.Ticker)))
            .OrderByDescending(instrument => instrument.AddedOn ?? DateTimeOffset.MinValue)
            .ThenBy(instrument => instrument.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumReportedNewStocks)
            .ToArray();

        return (
            newStocks,
            null,
            await _context.TrackedStocks.CountAsync(cancellationToken),
            newlyTrackedTickers.Count);
    }

    private static Trading212TradableInstrument NormalizeInstrument(Trading212TradableInstrument instrument)
    {
        return new Trading212TradableInstrument
        {
            AddedOn = instrument.AddedOn,
            CurrencyCode = (instrument.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant(),
            Isin = (instrument.Isin ?? string.Empty).Trim(),
            Name = (instrument.Name ?? string.Empty).Trim(),
            ShortName = (instrument.ShortName ?? string.Empty).Trim(),
            Ticker = NormalizeMarketTicker(instrument.Ticker),
            Type = (instrument.Type ?? string.Empty).Trim().ToUpperInvariant()
        };
    }

    private static void UpdateTrackedStock(
        TrackedStock stock,
        Trading212TradableInstrument instrument,
        DateTimeOffset observedAtUtc)
    {
        stock.Name = string.IsNullOrWhiteSpace(instrument.Name) ? stock.Name : instrument.Name;
        stock.ShortName = string.IsNullOrWhiteSpace(instrument.ShortName) ? stock.ShortName : instrument.ShortName;
        stock.Isin = string.IsNullOrWhiteSpace(instrument.Isin) ? stock.Isin : instrument.Isin;
        stock.CurrencyCode = string.IsNullOrWhiteSpace(instrument.CurrencyCode)
            ? stock.CurrencyCode
            : instrument.CurrencyCode;
        stock.InstrumentType = instrument.Type;
        stock.AddedOn = instrument.AddedOn ?? stock.AddedOn;
        stock.LastSeenAtUtc = observedAtUtc;
    }

    private static string GetInstrumentName(Trading212TradableInstrument instrument)
    {
        if (!string.IsNullOrWhiteSpace(instrument.Name))
        {
            return instrument.Name;
        }

        return string.IsNullOrWhiteSpace(instrument.ShortName)
            ? instrument.Ticker
            : instrument.ShortName;
    }

    private static string GetInstrumentShortName(Trading212TradableInstrument instrument)
    {
        return string.IsNullOrWhiteSpace(instrument.ShortName)
            ? GetInstrumentName(instrument)
            : instrument.ShortName;
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

    private static string NormalizeMarketTicker(string? ticker)
    {
        return (ticker ?? string.Empty).Trim().ToUpperInvariant();
    }
}
