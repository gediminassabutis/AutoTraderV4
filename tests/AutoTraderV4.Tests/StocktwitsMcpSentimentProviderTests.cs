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
            new StocktwitsMcpOptions { Enabled = true });
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
        var stocktwitsOptions = new StocktwitsMcpOptions { Enabled = true };
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
}
