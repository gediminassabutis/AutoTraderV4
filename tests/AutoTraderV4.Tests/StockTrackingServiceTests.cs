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
        context.OrderExecutionRecords.AddRange(
            new OrderExecutionRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "MSFT_US_EQ",
                Side = "Buy",
                Status = nameof(OrderExecutionStatus.Filled),
                CreatedUtc = now.AddMinutes(-1),
                UpdatedUtc = now.AddMinutes(-1)
            },
            new OrderExecutionRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "ZZZZ",
                Side = "Buy",
                Status = nameof(OrderExecutionStatus.Validated),
                CreatedUtc = now.AddMinutes(-1),
                UpdatedUtc = now.AddMinutes(-1)
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
        var tradingClient = new StaticTrading212Client(
        [
            new Trading212TradableInstrument
            {
                Ticker = "KO_US_EQ",
                Name = "Coca-Cola",
                Type = "STOCK",
                AddedOn = now.AddMinutes(-1)
            },
            new Trading212TradableInstrument
            {
                Ticker = "MSFT_US_EQ",
                Name = "Microsoft",
                Type = "STOCK",
                AddedOn = now
            },
            new Trading212TradableInstrument
            {
                Ticker = "NVDA_US_EQ",
                Name = "NVIDIA",
                Type = "STOCK",
                AddedOn = now.AddSeconds(-1)
            },
            new Trading212TradableInstrument
            {
                Ticker = "TSLA_US_EQ",
                Name = "Tesla",
                Type = "STOCK",
                AddedOn = now.AddSeconds(-2)
            },
            new Trading212TradableInstrument
            {
                Ticker = "AMD_US_EQ",
                Name = "AMD",
                Type = "STOCK",
                AddedOn = now.AddSeconds(-3)
            },
            new Trading212TradableInstrument
            {
                Ticker = "ZZZZ_US_EQ",
                Name = "Unfilled example",
                Type = "STOCK",
                AddedOn = now.AddSeconds(-4)
            },
            new Trading212TradableInstrument
            {
                Ticker = "VTI_US_EQ",
                Name = "Vanguard Total Stock Market ETF",
                Type = "ETF",
                AddedOn = now.AddSeconds(-5)
            }
        ]);
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
            tradingClient,
            portfolioSync,
            marketData,
            new PortfolioReviewService(repository),
            buyService,
            NullLogger<StockTrackingService>.Instance,
            new StockTrackingCycleGate());

        var result = await service.RunCycleAsync();

        Assert.Equal(new[] { "KO" }, result.UpdatedTickers);
        Assert.Empty(result.SellRecommendations);
        var updatedPosition = await context.Positions.SingleAsync();
        Assert.Equal(110m, updatedPosition.CurrentPrice);
        Assert.Equal("NVDA", Assert.Single(result.BuyOpportunities).Ticker);
        Assert.Equal(86m, result.BuyOpportunities[0].ConfidenceScore);
        Assert.Equal(
            new[] { "NVDA_US_EQ", "TSLA_US_EQ", "AMD_US_EQ", "ZZZZ_US_EQ" },
            result.NewStocks.Select(stock => stock.Ticker));
        Assert.Null(result.NewStockDiscoveryError);
        Assert.Equal(4, await context.MarketSnapshots.CountAsync());
        Assert.Equal(100m, (await context.MarketSnapshots.SingleAsync(snapshot => snapshot.Ticker == "NVDA")).Price);
        Assert.Equal(500m, (await context.MarketSnapshots.SingleAsync(snapshot => snapshot.Ticker == "AMD")).Price);
        Assert.Equal(2, await context.OrderExecutionRecords.CountAsync());
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
            new DemoTrading212Client(),
            portfolioSync,
            marketData,
            new PortfolioReviewService(repository),
            buyService,
            NullLogger<StockTrackingService>.Instance,
            new StockTrackingCycleGate());

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
            new StaticTrading212Client(
            [
                new Trading212TradableInstrument
                {
                    Ticker = "ABC_US_EQ",
                    Name = "Example Stock",
                    Type = "STOCK",
                    AddedOn = DateTimeOffset.UtcNow
                }
            ]),
            portfolioSync,
            marketData,
            new PortfolioReviewService(repository),
            buyService,
            NullLogger<StockTrackingService>.Instance,
            new StockTrackingCycleGate());

        var result = await service.RunCycleAsync();

        Assert.False(result.MarketDataReadiness.LiveProviderConfigured);
        Assert.Equal("non-actionable", result.MarketDataReadiness.Mode);
        Assert.Empty(result.UpdatedTickers);
        Assert.Empty(result.BuyOpportunities);
        Assert.Equal("ABC_US_EQ", Assert.Single(result.NewStocks).Ticker);
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

    private sealed class StaticTrading212Client(
        IReadOnlyList<Trading212TradableInstrument> instruments) : ITrading212Client
    {
        public Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Trading212Position>> GetPositionsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Trading212TradableInstrument>> GetAvailableInstrumentsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(instruments);
        }

        public Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
