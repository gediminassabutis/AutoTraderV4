using System.Text.Json;
using AutoTraderV4.Services;
using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4.Tests;

public sealed class StocktwitsMcpSentimentProviderTests
{
    [Fact]
    public async Task IngestAsync_PersistsStocktwitsSentimentAndStockStats_AndAvoidsDuplicateRecords()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var toolClient = new StaticStocktwitsMcpToolClient();
        var provider = new StocktwitsMcpSentimentProvider(
            toolClient,
            new StocktwitsMcpOptions { Enabled = true, Command = "node" });
        var service = new SentimentService(context, [provider], TimeProvider.System);

        var result = await service.IngestAsync("NVDA_US_EQ");

        Assert.True(result.Success);
        Assert.Equal(DataFreshnessStatus.Fresh, result.FreshnessStatus);
        Assert.Equal("stocktwits-mcp", result.ActiveProvider);

        var record = await context.SentimentRecords.SingleAsync();
        Assert.Equal("NVDA_US_EQ", record.Ticker);
        Assert.Equal("NVDA", record.Symbol);
        Assert.Equal("stocktwits", record.Source);
        Assert.Equal("stocktwits-mcp", record.ProviderName);
        Assert.Equal(60m, record.Score);
        Assert.Equal(60m, record.Magnitude);
        Assert.Equal(60m, record.Confidence);

        using var metadata = JsonDocument.Parse(record.MetadataJson);
        var root = metadata.RootElement;
        Assert.Equal(12, root.GetProperty("messages_analyzed").GetInt32());
        Assert.Equal(8, root.GetProperty("bullish").GetInt32());
        Assert.Equal(2, root.GetProperty("bearish").GetInt32());
        Assert.Equal(2, root.GetProperty("neutral").GetInt32());
        Assert.Equal(80m, root.GetProperty("sentiment_score").GetDecimal());
        Assert.Equal("Bullish", root.GetProperty("sentiment_label").GetString());
        Assert.Equal(180.25m, root.GetProperty("stock_stats").GetProperty("price").GetDecimal());
        Assert.Equal(1250000, root.GetProperty("stock_stats").GetProperty("volume").GetInt32());

        var cachedResult = await service.IngestAsync("NVDA_US_EQ");

        Assert.True(cachedResult.Success);
        Assert.Equal(record.Id, cachedResult.PersistedRecord?.Id);
        Assert.Equal(2, toolClient.Calls.Count);
        Assert.Equal(1, await context.SentimentRecords.CountAsync());
        Assert.All(toolClient.Calls, call => Assert.Equal("NVDA", call.Symbol));
        Assert.Equal(
            ["get_symbol_sentiment", "get_stock_price"],
            toolClient.Calls.Select(call => call.ToolName));
    }

    [Fact]
    public async Task SentimentService_WhenStocktwitsEnabled_DoesNotFallBackToSyntheticSentiment()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var stocktwitsOptions = new StocktwitsMcpOptions { Enabled = true, Command = "node" };
        var service = new SentimentService(
            context,
            [new FailingStocktwitsMcpToolClientProvider(), new DemoSentimentProvider()],
            TimeProvider.System,
            stocktwitsOptions);

        var result = await service.IngestAsync("NVDA");

        Assert.False(result.Success);
        Assert.Equal(DataFreshnessStatus.Missing, result.FreshnessStatus);
        Assert.Contains(result.Failures, failure => failure.Contains("Stocktwits unavailable", StringComparison.Ordinal));
        Assert.Empty(await context.SentimentRecords.ToListAsync());
    }

    [Fact]
    public async Task IngestAsync_WhenDisabled_DoesNotValidateOptionalLauncherSettings()
    {
        var options = new StocktwitsMcpOptions
        {
            Enabled = false,
            Command = string.Empty,
            NpmScriptShell = "missing-bash.exe"
        };
        var toolClient = new StaticStocktwitsMcpToolClient();
        var provider = new StocktwitsMcpSentimentProvider(toolClient, options);

        var result = await provider.GetAsync("NVDA");

        Assert.Null(result);
        Assert.Empty(toolClient.Calls);
    }

    [Fact]
    public async Task SentimentService_ConcurrentIngestion_DeduplicatesAcrossServiceScopes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);

        var stocktwitsOptions = new StocktwitsMcpOptions { Enabled = true };
        var toolClient = new StaticStocktwitsMcpToolClient();
        var provider = new StocktwitsMcpSentimentProvider(toolClient, stocktwitsOptions);
        var writeGate = new SentimentRecordWriteGate();
        var firstService = new SentimentService(
            firstContext,
            [provider],
            TimeProvider.System,
            stocktwitsOptions,
            writeGate);
        var secondService = new SentimentService(
            secondContext,
            [provider],
            TimeProvider.System,
            stocktwitsOptions,
            writeGate);

        var results = await Task.WhenAll(
            firstService.IngestAsync("NVDA_US_EQ"),
            secondService.IngestAsync("NVDA_US_EQ"));

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(1, await firstContext.SentimentRecords.CountAsync());
        Assert.Equal(2, toolClient.Calls.Count);
    }

    [Fact]
    public async Task IngestAsync_WhenProviderFails_BacksOffBeforeRetryingRemoteCalls()
    {
        var toolClient = new FailingStocktwitsMcpToolClient();
        var provider = new StocktwitsMcpSentimentProvider(
            toolClient,
            new StocktwitsMcpOptions { Enabled = true, Command = "node" });

        var firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync("NVDA"));
        var retryFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync("NVDA"));

        Assert.Equal("Stocktwits unavailable.", firstFailure.Message);
        Assert.Contains("backed off", retryFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, toolClient.Calls);
    }

    [Fact]
    public void StocktwitsMcpRequestRateLimiter_EnforcesTheSharedHourlyBudget()
    {
        var limiter = new StocktwitsMcpRequestRateLimiter(TimeProvider.System);

        for (var request = 0; request < StocktwitsMcpRequestRateLimiter.MaximumRequestsPerHour; request++)
        {
            Assert.True(limiter.TryAcquire(out var retryAfter));
            Assert.Equal(TimeSpan.Zero, retryAfter);
        }

        Assert.False(limiter.TryAcquire(out var blockedRetryAfter));
        Assert.True(blockedRetryAfter > TimeSpan.Zero);
        Assert.True(blockedRetryAfter <= TimeSpan.FromHours(1));
    }

    [Fact]
    public void StocktwitsMcpOptions_RequiresBashForNpxOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var defaultLauncherWithoutBash = new StocktwitsMcpOptions
        {
            Enabled = true,
            Command = "npx",
            NpmScriptShell = null
        };

        var exception = Assert.Throws<ArgumentException>(defaultLauncherWithoutBash.Validate);
        Assert.Contains("Git Bash", exception.Message, StringComparison.Ordinal);

        var prebuiltServerLauncher = new StocktwitsMcpOptions
        {
            Enabled = true,
            Command = "node",
            NpmScriptShell = null
        };
        prebuiltServerLauncher.Validate();
    }

    private sealed class StaticStocktwitsMcpToolClient : IStocktwitsMcpToolClient
    {
        public List<(string ToolName, string Symbol)> Calls { get; } = [];

        public Task<JsonElement> CallAsync(
            string toolName,
            string symbol,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((toolName, symbol));

            var response = toolName switch
            {
                "get_symbol_sentiment" =>
                    """{"symbol":"NVDA","messages_analyzed":12,"bullish":8,"bearish":2,"neutral":2,"sentiment_score":80,"sentiment_label":"Bullish"}""",
                "get_stock_price" =>
                    """{"symbol":"NVDA","price":180.25,"change":2.15,"change_percent":1.21,"volume":1250000,"market_cap":4440000000,"pe_ratio":36.5,"sector":"Technology","industry":"Semiconductors"}""",
                _ => throw new InvalidOperationException($"Unexpected Stocktwits MCP tool '{toolName}'.")
            };

            using var document = JsonDocument.Parse(response);
            return Task.FromResult(document.RootElement.Clone());
        }
    }

    private sealed class FailingStocktwitsMcpToolClientProvider : IStocktwitsSentimentProvider
    {
        public Task<SentimentDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Stocktwits unavailable.");
        }
    }

    private sealed class FailingStocktwitsMcpToolClient : IStocktwitsMcpToolClient
    {
        public int Calls { get; private set; }

        public Task<JsonElement> CallAsync(
            string toolName,
            string symbol,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            throw new InvalidOperationException("Stocktwits unavailable.");
        }
    }
}
