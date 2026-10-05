using AutoTraderV4.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoTraderV4.Tests;

public sealed class StockTrackingServiceTests
{
    [Fact]
    public async Task RunCycleAsync_RefreshesHeldTickAndFindsUnheldBuySignal()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;

        context.Positions.Add(new PortfolioPosition
        {
            Id = Guid.NewGuid(),
            Ticker = "KO",
            Sector = "Consumer Staples",
            Quantity = 5m,
            AveragePrice = 95m,
            CurrentPrice = 100m,
            Currency = "USD",
            ConfidenceScore = 80m,
            UpdatedAtUtc = now.AddMinutes(-10)
        });
        context.PortfolioStates.Add(new PortfolioStateRecord
        {
            Id = Guid.NewGuid(),
            TotalValue = 100000m,
            Cash = 90000m,
            PeakPortfolioValue = 100000m,
            UpdatedAtUtc = now
        });
        context.StrategySignals.AddRange(
            new StrategySignalRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "NVDA",
                Signal = 1.2m,
                Side = "Buy",
                OrderType = "Market",
                CreatedUtc = now
            },
            new StrategySignalRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "TSLA",
                Signal = -1.8m,
                Side = "Sell",
                OrderType = "Market",
                CreatedUtc = now.AddSeconds(-1)
            },
            new StrategySignalRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "AMD",
                Signal = 1.2m,
                Side = "Buy",
                OrderType = "Market",
                CreatedUtc = now.AddSeconds(-2)
            },
            new StrategySignalRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "ZZZZ",
                Signal = 2m,
                Side = "Buy",
                OrderType = "Market",
                CreatedUtc = now.AddSeconds(-3)
            });
        await context.SaveChangesAsync();

        var repository = new PortfolioRepository(context);
        var portfolioSync = new PortfolioStateSyncService(
            repository,
            new DemoTrading212Client(),
            TimeProvider.System,
            useDemoData: true);
        var marketData = new MarketDataService(
            context,
            [new StaticMarketDataProvider(symbol => new MarketDataContract
            {
                Symbol = symbol,
                Ticker = symbol,
                Price = symbol switch
                {
                    "KO" => 110m,
                    "AMD" => 500m,
                    _ => 100m
                },
                LiquidityScore = 90m,
                SpreadPercent = 0.2m,
                IsSynthetic = false,
                TimestampUtc = DateTimeOffset.UtcNow,
                Source = "test-provider",
                ProviderName = "verified-test-provider",
                Metadata = symbol == "KO"
                    ? new Dictionary<string, object> { ["sector"] = "Consumer Staples" }
                    : new Dictionary<string, object>()
            })],
            TimeProvider.System);
        var buyService = new BuyOpportunityService(
            context,
            repository,
            new RiskGovernanceService(repository, TimeProvider.System, portfolioSync, useDemoData: true),
            portfolioSync,
            marketData,
            NullLogger<BuyOpportunityService>.Instance);
        var service = new StockTrackingService(
            repository,
            portfolioSync,
            marketData,
            new PortfolioReviewService(repository),
            buyService,
            NullLogger<StockTrackingService>.Instance);

        var result = await service.RunCycleAsync();

        Assert.Equal(new[] { "KO" }, result.UpdatedTickers);
        Assert.Empty(result.SellRecommendations);
        var updatedPosition = await context.Positions.SingleAsync();
        Assert.Equal(110m, updatedPosition.CurrentPrice);
        Assert.Equal("NVDA", Assert.Single(result.BuyOpportunities).Ticker);
        Assert.Equal(86m, result.BuyOpportunities[0].ConfidenceScore);
        Assert.Equal(4, await context.MarketSnapshots.CountAsync());
        Assert.Equal(100m, (await context.MarketSnapshots.SingleAsync(snapshot => snapshot.Ticker == "NVDA")).Price);
        Assert.Equal(500m, (await context.MarketSnapshots.SingleAsync(snapshot => snapshot.Ticker == "AMD")).Price);
        Assert.Empty(await context.OrderExecutionRecords.ToListAsync());
    }

    [Fact]
    public async Task RunCycleAsync_DoesNotReplaceHeldPriceWithSyntheticOrStaleData()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;

        context.Positions.Add(new PortfolioPosition
        {
            Id = Guid.NewGuid(),
            Ticker = "KO",
            Sector = "Consumer Staples",
            Quantity = 5m,
            AveragePrice = 95m,
            CurrentPrice = 100m,
            Currency = "USD",
            UpdatedAtUtc = now.AddMinutes(-10)
        });
        context.Positions.Add(new PortfolioPosition
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Sector = "Technology",
            Quantity = 2m,
            AveragePrice = 200m,
            CurrentPrice = 210m,
            Currency = "USD",
            UpdatedAtUtc = now.AddMinutes(-10)
        });
        await context.SaveChangesAsync();

        var repository = new PortfolioRepository(context);
        var portfolioSync = new PortfolioStateSyncService(
            repository,
            new DemoTrading212Client(),
            TimeProvider.System,
            useDemoData: true);
        var marketData = new MarketDataService(
            context,
            [new StaticMarketDataProvider(symbol =>
            {
                var isSynthetic = symbol == "KO";
                return new MarketDataContract
                {
                    Symbol = symbol,
                    Ticker = symbol,
                    Price = 999m,
                    IsSynthetic = isSynthetic,
                    TimestampUtc = isSynthetic ? DateTimeOffset.UtcNow : now.AddMinutes(-10),
                    FreshnessWindow = TimeSpan.FromMinutes(5),
                    Source = isSynthetic ? "demo" : "test-provider",
                    ProviderName = isSynthetic ? "synthetic-test-provider" : "stale-test-provider"
                };
            })],
            TimeProvider.System);
        var buyService = new BuyOpportunityService(
            context,
            repository,
            new RiskGovernanceService(repository, TimeProvider.System, portfolioSync, useDemoData: true),
            portfolioSync,
            marketData,
            NullLogger<BuyOpportunityService>.Instance);
        var service = new StockTrackingService(
            repository,
            portfolioSync,
            marketData,
            new PortfolioReviewService(repository),
            buyService,
            NullLogger<StockTrackingService>.Instance);

        var result = await service.RunCycleAsync();

        Assert.Empty(result.UpdatedTickers);
        Assert.Empty(result.BuyOpportunities);
        Assert.Equal(100m, (await context.Positions.SingleAsync(position => position.Ticker == "KO")).CurrentPrice);
        Assert.Equal(210m, (await context.Positions.SingleAsync(position => position.Ticker == "MSFT")).CurrentPrice);
        Assert.Equal(
            "Stale",
            (await context.MarketSnapshots.SingleAsync(snapshot => snapshot.Ticker == "MSFT")).FreshnessStatus);
    }

    [Fact]
    public async Task RunCycleAsync_DisablesTicksAndBuysWithoutLiveProvider()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.Positions.Add(new PortfolioPosition
        {
            Id = Guid.NewGuid(),
            Ticker = "NVDA",
            Sector = "Technology",
            Quantity = 2m,
            AveragePrice = 100m,
            CurrentPrice = 105m,
            Currency = "USD",
            ConfidenceScore = 80m,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var repository = new PortfolioRepository(context);
        var portfolioSync = new PortfolioStateSyncService(
            repository,
            new DemoTrading212Client(),
            TimeProvider.System,
            useDemoData: true);
        var provider = new StaticMarketDataProvider(
            symbol => new MarketDataContract
            {
                Symbol = symbol,
                Price = 999m,
                LiquidityScore = 90m,
                SpreadPercent = 0.2m,
                IsSynthetic = false,
                TimestampUtc = DateTimeOffset.UtcNow,
                Source = "unexpected-provider",
                ProviderName = "unconfigured-test-provider"
            },
            supportsLiveData: false);
        var marketData = new MarketDataService(context, [provider], TimeProvider.System);
        var buyService = new BuyOpportunityService(
            context,
            repository,
            new RiskGovernanceService(repository, TimeProvider.System, portfolioSync, useDemoData: true),
            portfolioSync,
            marketData,
            NullLogger<BuyOpportunityService>.Instance);
        var service = new StockTrackingService(
            repository,
            portfolioSync,
            marketData,
            new PortfolioReviewService(repository),
            buyService,
            NullLogger<StockTrackingService>.Instance);

        var result = await service.RunCycleAsync();

        Assert.False(result.MarketDataReadiness.LiveProviderConfigured);
        Assert.Equal("non-actionable", result.MarketDataReadiness.Mode);
        Assert.Empty(result.UpdatedTickers);
        Assert.Empty(result.BuyOpportunities);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(105m, (await context.Positions.SingleAsync()).CurrentPrice);
        Assert.Empty(await context.MarketSnapshots.ToListAsync());
    }

    [Fact]
    public async Task ScanForBuysAsync_AccumulatesSectorExposureAcrossCandidates()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        var now = DateTimeOffset.UtcNow;

        context.Positions.Add(new PortfolioPosition
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Sector = "Technology",
            Quantity = 150m,
            AveragePrice = 100m,
            CurrentPrice = 100m,
            Currency = "USD",
            ConfidenceScore = 80m,
            UpdatedAtUtc = now
        });
        context.PortfolioStates.Add(new PortfolioStateRecord
        {
            Id = Guid.NewGuid(),
            TotalValue = 100000m,
            Cash = 85000m,
            PeakPortfolioValue = 100000m,
            UpdatedAtUtc = now
        });
        context.StrategySignals.AddRange(
            new StrategySignalRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "NVDA",
                Signal = 2m,
                Side = "Buy",
                OrderType = "Market",
                CreatedUtc = now
            },
            new StrategySignalRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "AMD",
                Signal = 1.5m,
                Side = "Buy",
                OrderType = "Market",
                CreatedUtc = now.AddSeconds(-1)
            });
        await context.SaveChangesAsync();

        var repository = new PortfolioRepository(context);
        var portfolioSync = new PortfolioStateSyncService(
            repository,
            new DemoTrading212Client(),
            TimeProvider.System,
            useDemoData: true);
        var marketData = new MarketDataService(
            context,
            [new StaticMarketDataProvider(symbol => new MarketDataContract
            {
                Symbol = symbol,
                Ticker = symbol,
                Price = 100m,
                LiquidityScore = 90m,
                SpreadPercent = 0.2m,
                IsSynthetic = false,
                TimestampUtc = DateTimeOffset.UtcNow,
                Source = "test-provider",
                ProviderName = "verified-test-provider"
            })],
            TimeProvider.System);
        var buyService = new BuyOpportunityService(
            context,
            repository,
            new RiskGovernanceService(repository, TimeProvider.System, portfolioSync, useDemoData: true),
            portfolioSync,
            marketData,
            NullLogger<BuyOpportunityService>.Instance);

        var decisions = await buyService.ScanForBuysAsync();

        var decision = Assert.Single(decisions);
        Assert.Equal("NVDA", decision.Ticker);
        Assert.Equal(100m, decision.FinalScore);
        Assert.Equal(5000m, decision.EstimatedCost);
        Assert.Empty(await context.OrderExecutionRecords.ToListAsync());
    }

    private sealed class StaticMarketDataProvider(
        Func<string, MarketDataContract> createContract,
        bool supportsLiveData = true) : IMarketDataProvider
    {
        public bool SupportsLiveData => supportsLiveData;
        public int Calls { get; private set; }

        public Task<MarketDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<MarketDataContract?>(createContract(symbol));
        }
    }
}
