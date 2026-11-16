using System.Net;
using System.Text;
using AutoTraderV4.Services;
using AutoTraderV4.Weekly;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4.Tests;

public sealed class WeeklyBarStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 15, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly BarStore _store;

    public WeeklyBarStoreTests()
    {
        _connection.Open();
        _store = new BarStore(new SharedConnectionFactory(_connection));
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AppendAsync_OnlyAddsBarsNewerThanWhatIsStored()
    {
        var first = await _store.AppendAsync("aapl", [Bar(2, 10m), Bar(3, 11m)]);
        var second = await _store.AppendAsync("AAPL", [Bar(3, 99m), Bar(4, 12m), Bar(4, 12.5m)]);
        await _store.AppendAsync("SPY", [Bar(4, 500m)]);

        var data = await _store.LoadAsync("SPY");
        var lastDates = await _store.GetLastDatesAsync();

        Assert.Equal(2, first);
        Assert.Equal(1, second);
        Assert.Equal([10m, 11m, 12.5m], data.Get("AAPL")!.Bars.Select(bar => bar.Close));
        Assert.Equal(new DateOnly(2026, 3, 4), lastDates["aapl"]);
        Assert.Equal(new DateOnly(2026, 3, 4), lastDates["SPY"]);
        Assert.Equal(500m, data.Benchmark!.Bars[0].Close);
    }

    [Fact]
    public async Task SyncAsync_DownloadsFullHistoryOnce_ThenOnlyNewSessions_AndSkipsToday()
    {
        var source = new FakeBarSource(new Dictionary<string, DailyBar[]>
        {
            ["AAPL"] = [Bar(2, 10m), Bar(3, 11m), Bar(4, 12m)],
            ["SPY"] = [Bar(2, 500m), Bar(3, 501m), Bar(4, 502m)]
        });
        var options = new WeeklyOptions { HistoryYears = 1 };
        options.Universe.Symbols = ["aapl", "ZZZZ", " "];
        var sync = new BarSyncService(_store, source, options, new FixedTimeProvider(Now));

        var first = await sync.SyncAsync();
        var second = await sync.SyncAsync();

        // Today is 2026-03-04, so only bars up to 03-03 are stored.
        Assert.Equal(3, first.SymbolsRequested);
        Assert.Equal(2, first.SymbolsUpdated);
        Assert.Equal(4, first.BarsAdded);
        Assert.Equal(new DateOnly(2026, 3, 3), first.UpToDate);
        Assert.Equal(["ZZZZ"], first.SymbolsWithoutData);
        Assert.Equal(0, second.BarsAdded);
        Assert.Equal((new DateOnly(2025, 3, 4), new DateOnly(2026, 3, 3)), (source.Requests[0].Start, source.Requests[0].End));
        // The second run only asks again for the symbol that has never returned data.
        var repeat = Assert.Single(source.Requests.Skip(1));
        Assert.Equal(["ZZZZ"], repeat.Symbols);

        var refreshed = await sync.SyncAsync(fullRefresh: true);
        Assert.Equal(4, refreshed.BarsAdded);
        Assert.Contains(refreshed.Notes, note => note.Contains("removed 4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Service_ScoresAndBacktestsFromTheStore()
    {
        var days = Enumerable.Range(0, 70)
            .Select(offset => new DateOnly(2025, 12, 1).AddDays(offset))
            .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToArray();
        await _store.AppendAsync("UP", days.Select((date, index) => Flat(date, 50m + index)));
        await _store.AppendAsync("DOWN", days.Select((date, index) => Flat(date, 150m - index)));
        await _store.AppendAsync("SPY", days.Select(date => Flat(date, 400m)));
        var options = new WeeklyOptions();
        options.Universe.MinimumAverageDollarVolume = 0m;
        options.Strategies.TrendFollowing = new TrendFollowingOptions { ShortPeriod = 5, LongPeriod = 20 };
        options.Strategies.MarketRegime = new MarketRegimeOptions { Period = 20 };
        var service = new WeeklyService(_store, options, new StrategyFactory(options));

        var scores = await service.GetScoresAsync(null);
        var report = await service.RunBacktestAsync(new WeeklyBacktestRequest { Strategy = "TrendFollowing", TopN = 1 });

        Assert.Equal(days[^1], scores.AsOfDate);
        Assert.Equal(StrategyFactory.Names, scores.Strategies);
        var up = scores.Symbols.Single(item => item.Symbol == "UP").Scores;
        var down = scores.Symbols.Single(item => item.Symbol == "DOWN").Scores;
        Assert.True(up["TrendFollowing"] > 50m);
        Assert.True(down["TrendFollowing"] < 50m);
        Assert.True(up["ShortTermReversal"] < 50m);
        Assert.Equal(50m, up["MarketRegime"]);
        Assert.DoesNotContain(scores.Symbols, item => item.Symbol == "SPY");
        Assert.Equal("TrendFollowing", report.Strategy);
        Assert.True(report.TotalReturnPercent > 0m);
        Assert.Equal("UP", Assert.Single(report.OpenPositions).Symbol);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RunBacktestAsync(new WeeklyBacktestRequest { Strategy = "Nope" }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RunBacktestAsync(new WeeklyBacktestRequest
        {
            Strategy = "TrendFollowing",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2025, 1, 1)
        }));
    }

    [Fact]
    public async Task Alpaca_DownloadsSeveralSymbolsPerRequest_AndFollowsPagination()
    {
        var requests = new List<string>();
        var options = new AlpacaOptions { ApiKeyId = "key-id", ApiSecretKey = "secret-key", MinimumRequestIntervalMilliseconds = 0 };
        var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(Uri.UnescapeDataString(request.RequestUri!.PathAndQuery));
            return Json(requests.Count == 1
                ? """
                  { "next_page_token": "p2", "bars": {
                      "AAPL": [ { "t": "2026-03-02T05:00:00Z", "o": 1.5, "h": 2.5, "l": 1.0, "c": 2.0, "v": 1000 } ] } }
                  """
                : """
                  { "next_page_token": null, "bars": {
                      "AAPL": [ { "t": "2026-03-03T05:00:00Z", "o": 2.0, "h": 3.0, "l": 1.5, "c": 2.5, "v": 2000 } ],
                      "BRK.B": [ { "t": "2026-03-03T05:00:00Z", "o": 400, "h": 410, "l": 395, "c": 405, "v": 300 },
                                 { "t": "2026-03-03T05:00:00Z", "o": 0, "h": 1, "l": 1, "c": 1, "v": 1 } ] } }
                  """);
        }));
        AlpacaMarketDataProvider.ConfigureHttpClient(client, options);
        var provider = new AlpacaMarketDataProvider(() => client, options, new FixedTimeProvider(Now));

        var bars = await provider.GetDailyBarsAsync(
            ["AAPL", "BRK_B_US_EQ", "VODl_EQ"],
            new DateOnly(2026, 3, 2),
            new DateOnly(2026, 3, 3));

        Assert.Equal(
            [new DailyBar(new DateOnly(2026, 3, 2), 1.5m, 2.5m, 1.0m, 2.0m, 1000m), new DailyBar(new DateOnly(2026, 3, 3), 2.0m, 3.0m, 1.5m, 2.5m, 2000m)],
            bars["AAPL"]);
        Assert.Equal(405m, Assert.Single(bars["BRK.B"]).Close);
        Assert.Equal(2, requests.Count);
        Assert.Equal(
            "/v2/stocks/bars?symbols=AAPL,BRK.B&timeframe=1Day&start=2026-03-02&end=2026-03-03&limit=10000&adjustment=all&sort=asc&feed=iex",
            requests[0]);
        Assert.EndsWith("&page_token=p2", requests[1]);
        Assert.Empty(await provider.GetDailyBarsAsync(["VODl_EQ"], new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)));
    }

    private static DailyBar Bar(int dayOfMarch, decimal close)
    {
        return Flat(new DateOnly(2026, 3, dayOfMarch), close);
    }

    private static DailyBar Flat(DateOnly date, decimal price) => new(date, price, price, price, price, 1_000_000m);

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SharedConnectionFactory(SqliteConnection connection) : IDbContextFactory<BarStoreDbContext>
    {
        public BarStoreDbContext CreateDbContext()
        {
            return new BarStoreDbContext(new DbContextOptionsBuilder<BarStoreDbContext>().UseSqlite(connection).Options);
        }
    }

    private sealed class FakeBarSource(Dictionary<string, DailyBar[]> bars) : IDailyBarSource
    {
        public List<(string[] Symbols, DateOnly Start, DateOnly End)> Requests { get; } = [];

        public Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>> GetDailyBarsAsync(
            IReadOnlyCollection<string> symbols,
            DateOnly start,
            DateOnly end,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((symbols.ToArray(), start, end));
            IReadOnlyDictionary<string, IReadOnlyList<DailyBar>> result = symbols
                .Where(bars.ContainsKey)
                .ToDictionary(
                    symbol => symbol,
                    symbol => (IReadOnlyList<DailyBar>)bars[symbol].Where(bar => bar.Date >= start).ToArray());
            return Task.FromResult(result);
        }
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
