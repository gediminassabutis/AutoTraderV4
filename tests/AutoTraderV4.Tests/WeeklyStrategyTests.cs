using System.Text.Json;
using AutoTraderV4.Weekly;

namespace AutoTraderV4.Tests;

public class WeeklyStrategyTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);

    [Fact]
    public void TrendFollowing_ScoresAboveFifty_WhenShortAverageIsAboveLongAverage()
    {
        var strategy = new TrendFollowingStrategy(new TrendFollowingOptions { ShortPeriod = 2, LongPeriod = 4 });

        // Short average 11, long average 10.5: gap 4.7619% of a 10% scale -> 73.81; price equals short average -> 50.
        var score = strategy.Score("AAA", Start.AddDays(3), Data([10m, 10m, 11m, 11m]));

        Assert.NotNull(score);
        Assert.Equal(61.90m, Math.Round(score.Value, 2));
    }

    [Fact]
    public void TrendFollowing_IsNeutralWhenFlat_LowInDowntrend_AndNullWithoutHistory()
    {
        var strategy = new TrendFollowingStrategy(new TrendFollowingOptions { ShortPeriod = 2, LongPeriod = 4 });

        Assert.Equal(50m, strategy.Score("AAA", Start, Data([10m, 10m, 10m, 10m])));
        Assert.True(strategy.Score("AAA", Start, Data([12m, 11m, 10m, 9m])) < 50m);
        Assert.Equal(100m, strategy.Score("AAA", Start, Data([10m, 10m, 20m, 40m])));
        Assert.Null(strategy.Score("AAA", Start, Data([10m, 10m, 10m])));
        Assert.Equal(4, strategy.RequiredData.MinimumBars);
    }

    [Theory]
    [InlineData(85, 100)]
    [InlineData(92.5, 75)]
    [InlineData(100, 50)]
    [InlineData(115, 0)]
    [InlineData(60, 100)]
    public void ShortTermReversal_ScoresLastWeeksLosersHigh(double lastClose, double expected)
    {
        var strategy = new ShortTermReversalStrategy(new ShortTermReversalOptions());

        var score = strategy.Score("AAA", Start, Data([100m, 99m, 98m, 97m, 96m, (decimal)lastClose]));

        Assert.Equal((decimal)expected, score);
    }

    [Fact]
    public void ShortTermReversal_ReturnsNull_WithoutAFullLookback()
    {
        var strategy = new ShortTermReversalStrategy(new ShortTermReversalOptions());

        Assert.Null(strategy.Score("AAA", Start, Data([100m, 99m, 98m, 97m, 96m])));
    }

    [Fact]
    public void MarketRegime_ScoresFromTheBenchmark_NotTheStock()
    {
        var strategy = new MarketRegimeStrategy(new MarketRegimeOptions { Period = 3 });

        // Benchmark average 105, last close 115: 9.5238% above on a 10% scale.
        var healthy = strategy.Score("AAA", Start, Data([1m, 1m, 1m], benchmark: [100m, 100m, 115m]));
        var weak = strategy.Score("AAA", Start, Data([9m, 9m, 9m], benchmark: [100m, 100m, 85m]));

        Assert.NotNull(healthy);
        Assert.Equal(97.62m, Math.Round(healthy.Value, 2));
        Assert.True(weak < 50m);
        Assert.Null(strategy.Score("AAA", Start, Data([1m, 1m, 1m], benchmark: [100m, 100m])));
        Assert.True(strategy.RequiredData.NeedsBenchmark);
    }

    [Fact]
    public void Factory_AppliesParameterOverrides_OnTopOfConfiguredDefaults()
    {
        var factory = new StrategyFactory(new WeeklyOptions());
        using var overrides = JsonDocument.Parse("""{ "shortPeriod": 20, "longPeriod": 100 }""");

        var configured = factory.Create("trendfollowing");
        var overridden = factory.Create("TrendFollowing", overrides.RootElement);

        Assert.Equal(200, configured.RequiredData.MinimumBars);
        Assert.Equal(100, overridden.RequiredData.MinimumBars);
        Assert.Equal(StrategyFactory.Names, factory.CreateAll().Select(strategy => strategy.Name));
    }

    [Theory]
    [InlineData("Nope", "{}")]
    [InlineData("TrendFollowing", """{ "unknown": 1 }""")]
    [InlineData("TrendFollowing", """{ "shortPeriod": 300 }""")]
    [InlineData("TrendFollowing", """{ "shortPeriod": "many" }""")]
    [InlineData("ShortTermReversal", "[1]")]
    public void Factory_RejectsUnknownStrategiesAndInvalidParameters(string name, string json)
    {
        var factory = new StrategyFactory(new WeeklyOptions());
        using var parameters = JsonDocument.Parse(json);

        Assert.Throws<ArgumentException>(() => factory.Create(name, parameters.RootElement));
    }

    [Fact]
    public void PriceSeries_ViewsNeverExposeBarsAfterTheAsOfDate()
    {
        var series = new PriceSeries("aaa", Bars([1m, 2m, 3m, 4m]).Reverse());
        var data = new MarketDataSet([series, new PriceSeries("SPY", Bars([5m, 6m, 7m, 8m]))], "spy");

        var view = data.View("AAA", Start.AddDays(1));

        Assert.Equal([1m, 2m], view.Bars.Select(bar => bar.Close));
        Assert.Equal([5m, 6m], view.BenchmarkBars.Select(bar => bar.Close));
        Assert.Empty(data.View("AAA", Start.AddDays(-1)).Bars);
        Assert.Empty(data.View("MISSING", Start.AddDays(1)).Bars);
        Assert.Null(series.BarOn(Start.AddDays(10)));
        Assert.Equal(4m, series.LastOnOrBefore(Start.AddDays(10))!.Close);
    }

    private static StrategyData Data(decimal[] closes, decimal[]? benchmark = null)
    {
        return new StrategyData(Start.AddDays(closes.Length - 1), Bars(closes), Bars(benchmark ?? []));
    }

    private static DailyBar[] Bars(decimal[] closes)
    {
        return closes.Select((close, index) => new DailyBar(Start.AddDays(index), close, close, close, close, 1_000m)).ToArray();
    }
}
