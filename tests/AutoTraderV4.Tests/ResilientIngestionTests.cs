using AutoTraderV4.Services;
using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4.Tests;

public class ResilientIngestionTests
{
    [Fact]
    public void DataFreshnessRules_Evaluate_ReturnsExpectedStates()
    {
        var reference = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var freshTimestamp = reference.AddSeconds(-30);
        var staleTimestamp = reference.AddMinutes(-10);
        var futureTimestamp = reference.AddMinutes(2);

        Assert.Equal(DataFreshnessStatus.Fresh, DataFreshnessRules.Evaluate(freshTimestamp, TimeSpan.FromMinutes(5), reference));
        Assert.Equal(DataFreshnessStatus.Stale, DataFreshnessRules.Evaluate(staleTimestamp, TimeSpan.FromMinutes(5), reference));
        Assert.Equal(DataFreshnessStatus.Missing, DataFreshnessRules.Evaluate(null, TimeSpan.FromMinutes(5), reference));
        Assert.Equal(DataFreshnessStatus.Invalid, DataFreshnessRules.Evaluate(futureTimestamp, TimeSpan.FromMinutes(5), reference));
    }

    [Fact]
    public void MarketDataContract_Validate_RejectsMissingTickerAndNegativePrice()
    {
        var missingTicker = new MarketDataContract
        {
            Price = 100m,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        var invalidPrice = new MarketDataContract
        {
            Symbol = "AAPL",
            Price = -1m,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        var missingTickerException = Assert.Throws<ArgumentException>(() => missingTicker.Validate());
        Assert.Equal("Symbol", missingTickerException.ParamName);

        var negativePriceException = Assert.Throws<ArgumentOutOfRangeException>(() => invalidPrice.Validate());
        Assert.Equal("Price", negativePriceException.ParamName);
    }

    [Fact]
    public void SentimentDataContract_Validate_RejectsOutOfRangeValues()
    {
        var invalidScore = new SentimentDataContract
        {
            Symbol = "MSFT",
            Score = 101m,
            Magnitude = 50m,
            Confidence = 80m,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        var invalidConfidence = new SentimentDataContract
        {
            Symbol = "MSFT",
            Score = 25m,
            Magnitude = 50m,
            Confidence = 101m,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        var scoreException = Assert.Throws<ArgumentOutOfRangeException>(() => invalidScore.Validate());
        Assert.Equal("Score", scoreException.ParamName);

        var confidenceException = Assert.Throws<ArgumentOutOfRangeException>(() => invalidConfidence.Validate());
        Assert.Equal("Confidence", confidenceException.ParamName);
    }

    [Fact]
    public async Task MarketDataService_IngestAsync_UsesFallbackProvider_WhenPrimaryDataIsStale()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var service = new MarketDataService(
            context,
            new IMarketDataProvider[]
            {
                new StaticMarketDataProvider(new MarketDataContract
                {
                    Symbol = "AAPL",
                    Price = 180m,
                    TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    Source = "primary",
                    ProviderName = "primary-market-data",
                    FreshnessWindow = TimeSpan.FromMinutes(5)
                }),
                new StaticMarketDataProvider(new MarketDataContract
                {
                    Symbol = "AAPL",
                    Price = 182m,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Source = "fallback",
                    ProviderName = "fallback-market-data",
                    FreshnessWindow = TimeSpan.FromMinutes(5)
                })
            },
            TimeProvider.System);

        var result = await service.IngestAsync("AAPL");

        Assert.True(result.Success);
        Assert.Equal(DataFreshnessStatus.Fresh, result.FreshnessStatus);
        Assert.Equal("fallback-market-data", result.ActiveProvider);
        Assert.Equal(1, await context.MarketSnapshots.CountAsync());
        Assert.Equal("fallback", (await context.MarketSnapshots.SingleAsync()).Source);
        Assert.Equal("fallback-market-data", (await context.MarketSnapshots.SingleAsync()).ProviderName);
        Assert.Equal("Fresh", (await context.MarketSnapshots.SingleAsync()).FreshnessStatus);
    }

    [Fact]
    public async Task MarketDataService_IngestAsync_RejectsWrongSymbolProviderResponse_BeforePersisting()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var service = new MarketDataService(
            context,
            new IMarketDataProvider[]
            {
                new StaticMarketDataProvider(new MarketDataContract
                {
                    Symbol = "MSFT",
                    Price = 500m,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Source = "primary",
                    ProviderName = "wrong-symbol-provider",
                    FreshnessWindow = TimeSpan.FromMinutes(5)
                }),
                new StaticMarketDataProvider(new MarketDataContract
                {
                    Symbol = "AAPL",
                    Price = 182m,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Source = "fallback",
                    ProviderName = "fallback-market-data",
                    FreshnessWindow = TimeSpan.FromMinutes(5)
                })
            },
            TimeProvider.System);

        var result = await service.IngestAsync("AAPL");

        Assert.True(result.Success);
        Assert.Equal("fallback-market-data", result.ActiveProvider);
        var persisted = await context.MarketSnapshots.SingleAsync();
        Assert.Equal("AAPL", persisted.Ticker);
        Assert.Equal("fallback-market-data", persisted.ProviderName);
    }

    [Fact]
    public async Task MarketDataService_IngestAsync_PersistsStaleState_WhenAllProvidersAreStale()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var service = new MarketDataService(
            context,
            new IMarketDataProvider[]
            {
                new StaticMarketDataProvider(new MarketDataContract
                {
                    Symbol = "MSFT",
                    Price = 400m,
                    TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    Source = "primary",
                    ProviderName = "primary-market-data",
                    FreshnessWindow = TimeSpan.FromMinutes(5)
                }),
                new StaticMarketDataProvider(new MarketDataContract
                {
                    Symbol = "MSFT",
                    Price = 401m,
                    TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-8),
                    Source = "fallback",
                    ProviderName = "fallback-market-data",
                    FreshnessWindow = TimeSpan.FromMinutes(5)
                })
            },
            TimeProvider.System);

        var result = await service.IngestAsync("MSFT");

        Assert.False(result.Success);
        Assert.Equal(DataFreshnessStatus.Stale, result.FreshnessStatus);
        var persisted = await context.MarketSnapshots.SingleAsync();
        Assert.Equal("Stale", persisted.FreshnessStatus);
        Assert.Equal("MSFT", persisted.Ticker);
    }

    [Fact]
    public async Task SentimentService_IngestAsync_UsesFallbackProvider_WhenPrimaryDataIsStale()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var service = new SentimentService(
            context,
            new ISentimentProvider[]
            {
                new StaticSentimentProvider(new SentimentDataContract
                {
                    Symbol = "NVDA",
                    Score = 55m,
                    Magnitude = 60m,
                    Confidence = 75m,
                    TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                    Source = "primary",
                    ProviderName = "primary-sentiment",
                    FreshnessWindow = TimeSpan.FromMinutes(15)
                }),
                new StaticSentimentProvider(new SentimentDataContract
                {
                    Symbol = "NVDA",
                    Score = 70m,
                    Magnitude = 68m,
                    Confidence = 85m,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Source = "fallback",
                    ProviderName = "fallback-sentiment",
                    FreshnessWindow = TimeSpan.FromMinutes(15)
                })
            },
            TimeProvider.System);

        var result = await service.IngestAsync("NVDA");

        Assert.True(result.Success);
        Assert.Equal(DataFreshnessStatus.Fresh, result.FreshnessStatus);
        Assert.Equal("fallback-sentiment", result.ActiveProvider);
        Assert.Equal(1, await context.SentimentRecords.CountAsync());
        var record = await context.SentimentRecords.SingleAsync();
        Assert.Equal("fallback", record.Source);
        Assert.Equal("fallback-sentiment", record.ProviderName);
        Assert.Equal("Fresh", record.FreshnessStatus);
    }

    [Fact]
    public async Task SentimentService_IngestAsync_RejectsWrongSymbolProviderResponse_BeforePersisting()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);

        var service = new SentimentService(
            context,
            new ISentimentProvider[]
            {
                new StaticSentimentProvider(new SentimentDataContract
                {
                    Symbol = "MSFT",
                    Score = 80m,
                    Magnitude = 70m,
                    Confidence = 90m,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Source = "primary",
                    ProviderName = "wrong-symbol-provider",
                    FreshnessWindow = TimeSpan.FromMinutes(15)
                }),
                new StaticSentimentProvider(new SentimentDataContract
                {
                    Symbol = "NVDA",
                    Score = 72m,
                    Magnitude = 68m,
                    Confidence = 85m,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Source = "fallback",
                    ProviderName = "fallback-sentiment",
                    FreshnessWindow = TimeSpan.FromMinutes(15)
                })
            },
            TimeProvider.System);

        var result = await service.IngestAsync("NVDA");

        Assert.True(result.Success);
        Assert.Equal("fallback-sentiment", result.ActiveProvider);
        var record = await context.SentimentRecords.SingleAsync();
        Assert.Equal("NVDA", record.Ticker);
        Assert.Equal("fallback-sentiment", record.ProviderName);
    }

    private sealed class StaticMarketDataProvider : IMarketDataProvider
    {
        private readonly MarketDataContract _contract;

        public StaticMarketDataProvider(MarketDataContract contract)
        {
            _contract = contract;
        }

        public Task<MarketDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MarketDataContract?>(_contract);
        }
    }

    private sealed class StaticSentimentProvider : ISentimentProvider
    {
        private readonly SentimentDataContract _contract;

        public StaticSentimentProvider(SentimentDataContract contract)
        {
            _contract = contract;
        }

        public Task<SentimentDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SentimentDataContract?>(_contract);
        }
    }
}
