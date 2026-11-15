using System.Net;
using System.Text;
using AutoTraderV4.Indicators;
using AutoTraderV4.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTraderV4.Tests;

public class AlpacaMarketDataProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 15, 0, 0, TimeSpan.Zero);

    private const string SnapshotJson = """
        {
          "symbol": "AAPL",
          "latestTrade": { "t": "2026-03-02T14:59:30.123456789Z", "x": "V", "p": 200.5, "s": 100 },
          "latestQuote": { "t": "2026-03-02T14:59:31Z", "ap": 200.6, "as": 2, "bp": 200.4, "bs": 3 },
          "minuteBar": { "t": "2026-03-02T14:59:00Z", "o": 200.1, "h": 200.7, "l": 200.0, "c": 200.5, "v": 12000, "vw": 200.4 },
          "dailyBar": { "t": "2026-03-02T05:00:00Z", "o": 198.0, "h": 201.0, "l": 197.5, "c": 200.5, "v": 400000, "vw": 199.5 },
          "prevDailyBar": { "t": "2026-02-27T05:00:00Z", "o": 196.0, "h": 199.0, "l": 195.0, "c": 198.0, "v": 500000, "vw": 200.0 }
        }
        """;

    [Theory]
    [InlineData("AAPL_US_EQ", "AAPL")]
    [InlineData("aapl", "AAPL")]
    [InlineData("BRK_B_US_EQ", "BRK.B")]
    [InlineData("BRK.B", "BRK.B")]
    [InlineData("VODl_EQ", null)]
    [InlineData("BMW_DE_EQ", null)]
    [InlineData("^VIX", null)]
    [InlineData("", null)]
    public void AlpacaSymbol_MapsUsTickers_AndRejectsUnsupportedInstruments(string ticker, string? expected)
    {
        Assert.Equal(expected, AlpacaSymbol.FromTicker(ticker));
    }

    [Fact]
    public async Task GetAsync_ParsesSnapshotIntoLiveNonSyntheticQuote()
    {
        HttpRequestMessage? captured = null;
        var provider = CreateProvider(request =>
        {
            captured = request;
            return Json(SnapshotJson);
        });

        var quote = await provider.GetAsync("AAPL_US_EQ");

        Assert.NotNull(quote);
        Assert.NotNull(captured);
        Assert.Equal("https://data.alpaca.markets/v2/stocks/AAPL/snapshot?feed=iex", captured.RequestUri!.ToString());
        Assert.Equal("key-id", captured.Headers.GetValues("APCA-API-KEY-ID").Single());
        Assert.Equal("secret-key", captured.Headers.GetValues("APCA-API-SECRET-KEY").Single());
        Assert.Equal("AAPL_US_EQ", quote.Ticker);
        Assert.Equal(200.5m, quote.Price);
        Assert.False(quote.IsSynthetic);
        Assert.Equal("alpaca", quote.ProviderName);
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 14, 59, 30, TimeSpan.Zero), quote.TimestampUtc.AddTicks(-(quote.TimestampUtc.Ticks % TimeSpan.TicksPerSecond)));
        Assert.Equal(198.0m, quote.Open);
        Assert.Equal(400000m, quote.Volume);
        // (200.6 - 200.4) / 200.5 * 100
        Assert.Equal(0.0998m, quote.SpreadPercent);
        // Previous session traded $100M: 20 * log10(1e8) - 80 = 80.
        Assert.Equal(80m, quote.LiquidityScore);
        Assert.Equal(DataFreshnessStatus.Fresh, DataFreshnessRules.Evaluate(quote.TimestampUtc, quote.FreshnessWindow, Now));
        quote.Validate();
        Assert.True(provider.SupportsLiveData);
    }

    [Fact]
    public async Task GetAsync_LeavesSpreadAndLiquidityUnmeasured_WhenQuoteAndBarsAreMissing()
    {
        var provider = CreateProvider(_ => Json("""
            { "symbol": "AAPL", "latestTrade": { "t": "2026-03-02T14:59:30Z", "p": 200.5 }, "latestQuote": { "ap": 0, "bp": 0 } }
            """));

        var quote = await provider.GetAsync("AAPL");

        Assert.NotNull(quote);
        Assert.Null(quote.SpreadPercent);
        Assert.Null(quote.LiquidityScore);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWithoutCallingAlpaca_ForUnsupportedTickers()
    {
        var calls = 0;
        var provider = CreateProvider(_ =>
        {
            calls++;
            return Json(SnapshotJson);
        });

        Assert.False(provider.Supports("VODl_EQ"));
        Assert.True(provider.Supports("AAPL_US_EQ"));
        Assert.Null(await provider.GetAsync("VODl_EQ"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenSymbolIsUnknownOrHasNoTrade()
    {
        Assert.Null(await CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).GetAsync("ZZZZ"));
        Assert.Null(await CreateProvider(_ => Json("""{ "symbol": "ZZZZ", "latestTrade": null }""")).GetAsync("ZZZZ"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "ApiKeyId")]
    [InlineData(HttpStatusCode.Forbidden, "ApiKeyId")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task GetAsync_ThrowsDescriptiveError_OnProviderFailures(HttpStatusCode statusCode, string expectedText)
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(statusCode));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync("AAPL"));

        Assert.Contains(expectedText, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDailyBarsAsync_ParsesBarsOldestFirst_AndFollowsPagination()
    {
        var requests = new List<string>();
        var provider = CreateProvider(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return requests.Count == 1
                ? Json("""
                    { "symbol": "AAPL", "next_page_token": "page two",
                      "bars": [ { "t": "2026-02-26T05:00:00Z", "o": 1.5, "h": 2.5, "l": 1.0, "c": 2.0, "v": 1000, "vw": 1.9 } ] }
                    """)
                : Json("""
                    { "symbol": "AAPL", "next_page_token": null,
                      "bars": [ { "t": "2026-02-27T05:00:00Z", "o": 2.0, "h": 3.0, "l": 1.5, "c": 2.5, "v": 2000 },
                                { "t": "2026-03-02T05:00:00Z", "o": 2.5, "h": 3.0, "l": 2.0, "c": 0 } ] }
                    """);
        });

        var bars = await provider.GetDailyBarsAsync("AAPL_US_EQ", 220);

        Assert.NotNull(bars);
        Assert.Equal(2, bars.Count);
        Assert.Equal(new PriceBar(new DateTimeOffset(2026, 2, 26, 5, 0, 0, TimeSpan.Zero), 1.5m, 2.5m, 1.0m, 2.0m, 1000m, 1.9m), bars[0]);
        Assert.Equal(2.5m, bars[1].Close);
        Assert.Null(bars[1].Vwap);
        Assert.Equal(2, requests.Count);
        // 220 bars -> 349 calendar days back from 2026-03-02.
        Assert.StartsWith("/v2/stocks/AAPL/bars?timeframe=1Day&start=2025-03-18&limit=1000&adjustment=all&sort=asc&feed=iex", requests[0]);
        Assert.EndsWith("&page_token=page%20two", requests[1]);
    }

    [Fact]
    public async Task GetDailyBarsAsync_ReturnsNull_ForUnsupportedOrUnknownSymbols()
    {
        Assert.Null(await CreateProvider(_ => Json("{}")).GetDailyBarsAsync("VODl_EQ", 220));
        Assert.Null(await CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).GetDailyBarsAsync("ZZZZ", 220));
    }

    [Fact]
    public void Constructor_RequiresCredentials()
    {
        Assert.Throws<ArgumentException>(() => new AlpacaMarketDataProvider(
            () => new HttpClient(),
            new AlpacaOptions(),
            TimeProvider.System));
    }

    [Fact]
    public void Program_RegistersAlpacaAheadOfSyntheticProviders_WhenCredentialsAreConfigured()
    {
        using var provider = BuildServices(new Dictionary<string, string?>
        {
            ["Alpaca:ApiKeyId"] = "key-id",
            ["Alpaca:ApiSecretKey"] = "secret-key"
        });

        var marketDataProviders = provider.GetServices<IMarketDataProvider>().ToArray();

        Assert.IsType<AlpacaMarketDataProvider>(marketDataProviders[0]);
        Assert.Contains(marketDataProviders, item => item is DemoMarketDataProvider);
        Assert.IsType<AlpacaMarketDataProvider>(Assert.Single(provider.GetServices<IPriceHistoryProvider>()));
        Assert.True(provider.GetRequiredService<TechnicalIndicatorService>().HistoryProviderConfigured);
    }

    [Theory]
    [InlineData("", "", "true")]
    [InlineData("key-id", "secret-key", "false")]
    public void Program_LeavesAlpacaOff_WithoutCredentialsOrWhenDisabled(string keyId, string secretKey, string enabled)
    {
        using var provider = BuildServices(new Dictionary<string, string?>
        {
            ["Alpaca:ApiKeyId"] = keyId,
            ["Alpaca:ApiSecretKey"] = secretKey,
            ["Alpaca:Enabled"] = enabled
        });

        Assert.DoesNotContain(provider.GetServices<IMarketDataProvider>(), item => item.SupportsLiveData);
        Assert.False(provider.GetRequiredService<TechnicalIndicatorService>().HistoryProviderConfigured);
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings)
        {
            ["Trading212:UseDemoData"] = "true",
            ["ConnectionStrings:DefaultConnection"] = string.Empty
        });

        Program.ConfigureServices(builder);
        return builder.Services.BuildServiceProvider();
    }

    private static AlpacaMarketDataProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var options = new AlpacaOptions { ApiKeyId = "key-id", ApiSecretKey = "secret-key" };
        var client = new HttpClient(new StubHandler(respond));
        AlpacaMarketDataProvider.ConfigureHttpClient(client, options);
        return new AlpacaMarketDataProvider(() => client, options, new FixedTimeProvider(Now));
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
