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
                Signal = 0.5m,
                Side = "Buy",
                OrderType = "Market",
                CreatedUtc = now.AddSeconds(-2)
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
                Price = symbol == "KO" ? 110m : 100m,
                LiquidityScore = 90m,
                SpreadPercent = 0.2m,
                IsSynthetic = false,
                TimestampUtc = DateTimeOffset.UtcNow,
                Source = "test-provider",
                ProviderName = "verified-test-provider",
                Metadata = new Dictionary<string, object> { ["sector"] = symbol == "KO" ? "Consumer Staples" : "Technology" }
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
        Assert.Equal(2, await context.MarketSnapshots.CountAsync());
        Assert.Equal(100m, (await context.MarketSnapshots.SingleAsync(snapshot => snapshot.Ticker == "NVDA")).Price);
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

    private sealed class StaticMarketDataProvider(Func<string, MarketDataContract> createContract) : IMarketDataProvider
    {
        public Task<MarketDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MarketDataContract?>(createContract(symbol));
        }
    }
}
