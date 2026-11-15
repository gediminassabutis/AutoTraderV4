using AutoTraderV4.Indicators;

namespace AutoTraderV4.Tests;

public class TechnicalIndicatorTests
{
    // Wilder RSI reference series (the widely published StockCharts worked example).
    private static readonly decimal[] RsiReferenceCloses =
    [
        44.34m, 44.09m, 44.15m, 43.61m, 44.33m, 44.83m, 45.10m, 45.42m, 45.84m, 46.08m,
        45.89m, 46.03m, 45.61m, 46.28m, 46.28m, 46.00m, 46.03m, 46.41m, 46.22m, 45.64m
    ];

    [Fact]
    public void Sma_AveragesTheMostRecentPeriod_AndReturnsNullWithoutEnoughHistory()
    {
        Assert.Equal(4m, TechnicalIndicators.Sma([1m, 2m, 3m, 4m, 5m], 3));
        Assert.Null(TechnicalIndicators.Sma([1m, 2m], 3));
    }

    [Fact]
    public void Ema_SeedsWithSmaThenSmooths()
    {
        // Seed = (1+2+3)/3 = 2; multiplier = 0.5; then 3, then 4.
        Assert.Equal(new[] { 2m, 3m, 4m }, TechnicalIndicators.EmaSeries([1m, 2m, 3m, 4m, 5m], 3));
        Assert.Null(TechnicalIndicators.Ema([1m, 2m], 3));
    }

    [Theory]
    [InlineData(15, 70.46)]
    [InlineData(16, 66.25)]
    [InlineData(17, 66.48)]
    [InlineData(18, 69.35)]
    [InlineData(19, 66.29)]
    [InlineData(20, 57.92)]
    public void Rsi_MatchesWilderReferenceValues(int closeCount, double expected)
    {
        var rsi = TechnicalIndicators.Rsi(RsiReferenceCloses[..closeCount]);

        Assert.NotNull(rsi);
        Assert.Equal((decimal)expected, Math.Round(rsi.Value, 2));
    }

    [Fact]
    public void Rsi_HandlesOneSidedAndInsufficientSeries()
    {
        var rising = Enumerable.Range(1, 20).Select(value => (decimal)value).ToArray();
        var flat = Enumerable.Repeat(10m, 20).ToArray();

        Assert.Equal(100m, TechnicalIndicators.Rsi(rising));
        Assert.Equal(0m, TechnicalIndicators.Rsi(rising.Reverse().ToArray()));
        Assert.Equal(50m, TechnicalIndicators.Rsi(flat));
        Assert.Null(TechnicalIndicators.Rsi(rising[..14]));
    }

    [Fact]
    public void Macd_MatchesIndependentlyComputedValues()
    {
        var closes = Enumerable.Range(0, 60)
            .Select(index => 100m + (index * index % 17) + (index * 0.5m))
            .ToArray();

        var macd = TechnicalIndicators.Macd(closes);

        Assert.NotNull(macd);
        Assert.Equal(4.102523m, Math.Round(macd.Macd, 6));
        Assert.Equal(3.417771m, Math.Round(macd.Signal, 6));
        Assert.Equal(0.684751m, Math.Round(macd.Histogram, 6));
        Assert.Null(TechnicalIndicators.Macd(closes[..33]));
    }

    [Fact]
    public void Atr_UsesTrueRangeIncludingGaps()
    {
        var bars = new[]
        {
            Bar(10m, 10m, 10m),
            Bar(12m, 11m, 11m), // true range 2 (high - previous close)
            Bar(11m, 8m, 9m),   // true range 3 (high - low)
            Bar(15m, 14m, 15m)  // true range 6 (gap: high - previous close)
        };

        // Seed over the first two true ranges = 2.5; Wilder smoothing with the third = (2.5 + 6) / 2.
        Assert.Equal(4.25m, TechnicalIndicators.Atr(bars, 2));
        Assert.Null(TechnicalIndicators.Atr(bars, 4));
    }

    [Fact]
    public void BollingerBands_UsePopulationStandardDeviation()
    {
        // Mean 5, population standard deviation 2.
        var bands = TechnicalIndicators.BollingerBands([2m, 4m, 4m, 4m, 5m, 5m, 7m, 9m], 8);

        Assert.NotNull(bands);
        Assert.Equal(5m, bands.Middle);
        Assert.Equal(9m, bands.Upper);
        Assert.Equal(1m, bands.Lower);
        Assert.Equal(1m, bands.PercentB);
    }

    [Fact]
    public void Vwap_WeightsTypicalPriceByVolume()
    {
        var bars = new[]
        {
            new PriceBar(DateTimeOffset.UnixEpoch, 10m, 12m, 9m, 9m, 100m),  // typical 10
            new PriceBar(DateTimeOffset.UnixEpoch, 20m, 22m, 18m, 20m, 300m) // typical 20
        };

        Assert.Equal(17.5m, TechnicalIndicators.Vwap(bars, 2));
    }

    [Fact]
    public void RateOfChangePercent_ComparesAgainstThePeriodStart()
    {
        Assert.Equal(10m, TechnicalIndicators.RateOfChangePercent([100m, 105m, 110m], 2));
        Assert.Null(TechnicalIndicators.RateOfChangePercent([100m, 105m], 2));
    }

    [Fact]
    public void Calculate_ProducesEngineInputs_ForAnUptrendWithFullHistory()
    {
        var bars = Enumerable.Range(0, 250)
            .Select(index =>
            {
                var close = 100m + (index * 0.5m) + (index % 5 == 0 ? -1.5m : 0m);
                return new PriceBar(
                    DateTimeOffset.UnixEpoch.AddDays(index),
                    close - 0.5m,
                    close + 1m,
                    close - 1m,
                    close,
                    index >= 230 ? 1_500_000m : 1_000_000m);
            })
            .ToArray();

        var snapshot = TechnicalIndicatorService.Calculate("AAPL", "test", bars, 5m, DateTimeOffset.UnixEpoch);
        var signals = new MarketSignalSnapshot();
        var missing = snapshot.ApplyTo(signals);

        Assert.Empty(missing);
        Assert.Equal(250, snapshot.BarCount);
        Assert.True(snapshot.PriceAboveSma50);
        Assert.True(snapshot.Sma50AboveSma200);
        Assert.True(signals.FiftyDayMovingAverage > signals.TwoHundredDayMovingAverage);
        Assert.InRange(signals.RsiValue, 50m, 100m);
        Assert.True(signals.VolumeTrendScore > 0m);
        Assert.InRange(signals.AtrPercent, 0.01m, 30m);
        Assert.Equal(0m, signals.PriceDislocationPercent);
        Assert.NotNull(snapshot.Momentum63Percent);
        Assert.Equal(snapshot.Momentum63Percent - 5m, snapshot.RelativeStrengthVsBenchmarkPercent);
        Assert.DoesNotContain(signals.Validate("Signals").Keys, key =>
            key.EndsWith(nameof(signals.RsiValue), StringComparison.Ordinal)
            || key.EndsWith(nameof(signals.AtrPercent), StringComparison.Ordinal)
            || key.EndsWith(nameof(signals.RsiTrendScore), StringComparison.Ordinal)
            || key.EndsWith(nameof(signals.VolumeTrendScore), StringComparison.Ordinal));
    }

    [Fact]
    public void Calculate_ReportsMissingInputs_InsteadOfDefaults_WhenHistoryIsShort()
    {
        var bars = Enumerable.Range(0, 30)
            .Select(index => Bar(101m + index, 99m + index, 100m + index))
            .ToArray();

        var snapshot = TechnicalIndicatorService.Calculate("AAPL", "test", bars, null, DateTimeOffset.UnixEpoch);
        var signals = new MarketSignalSnapshot();
        var missing = snapshot.ApplyTo(signals);

        Assert.Null(snapshot.Sma50);
        Assert.Null(snapshot.Sma200);
        Assert.Null(snapshot.Macd);
        Assert.Null(snapshot.RelativeStrengthVsBenchmarkPercent);
        Assert.NotNull(snapshot.Rsi14);
        Assert.Contains(nameof(signals.FiftyDayMovingAverage), missing);
        Assert.Contains(nameof(signals.TwoHundredDayMovingAverage), missing);
        Assert.Contains(nameof(signals.VolumeTrendScore), missing);
        Assert.Equal(0m, signals.FiftyDayMovingAverage);
    }

    [Fact]
    public async Task Service_CachesHistory_AndComputesRelativeStrengthAgainstTheBenchmark()
    {
        var provider = new FakeHistoryProvider(symbol => Enumerable.Range(0, 100)
            .Select(index =>
            {
                var close = symbol == "SPY" ? 100m + (index * 0.1m) : 100m + index;
                return Bar(close + 1m, close - 1m, close);
            })
            .ToArray());
        var service = new TechnicalIndicatorService([provider], TimeProvider.System);

        var first = await service.GetAsync("aapl_us_eq");
        var second = await service.GetAsync("AAPL_US_EQ");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("AAPL_US_EQ", first.Symbol);
        Assert.True(first.RelativeStrengthVsBenchmarkPercent > 0m);
        Assert.Equal(["AAPL_US_EQ", "SPY"], provider.Requests);
    }

    [Fact]
    public async Task Service_ReturnsNull_WhenNoProviderCoversTheSymbol()
    {
        var service = new TechnicalIndicatorService([new FakeHistoryProvider(_ => null)], TimeProvider.System);

        Assert.Null(await service.GetAsync("VODl_EQ"));
        Assert.False(new TechnicalIndicatorService([], TimeProvider.System).HistoryProviderConfigured);
    }

    private static PriceBar Bar(decimal high, decimal low, decimal close)
    {
        return new PriceBar(DateTimeOffset.UnixEpoch, close, high, low, close, 1_000m);
    }

    private sealed class FakeHistoryProvider(Func<string, IReadOnlyList<PriceBar>?> bars) : IPriceHistoryProvider
    {
        public List<string> Requests { get; } = [];

        public string ProviderName => "fake";

        public Task<IReadOnlyList<PriceBar>?> GetDailyBarsAsync(
            string symbol,
            int minimumBars,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(symbol);
            return Task.FromResult(bars(symbol));
        }
    }
}
