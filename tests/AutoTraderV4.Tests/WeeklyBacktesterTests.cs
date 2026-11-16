using AutoTraderV4.Weekly;

namespace AutoTraderV4.Tests;

public class WeeklyBacktesterTests
{
    // 2024-01-01 is a Monday. Four trading weeks, Monday to Friday.
    private static readonly DateOnly Monday1 = new(2024, 1, 1);
    private static readonly DateOnly Friday1 = new(2024, 1, 5);
    private static readonly DateOnly Monday2 = new(2024, 1, 8);
    private static readonly DateOnly Friday2 = new(2024, 1, 12);
    private static readonly DateOnly Monday3 = new(2024, 1, 15);
    private static readonly DateOnly Friday3 = new(2024, 1, 19);
    private static readonly DateOnly Monday4 = new(2024, 1, 22);
    private static readonly DateOnly Friday4 = new(2024, 1, 26);

    private static readonly UniverseOptions OpenUniverse = new()
    {
        MinimumPrice = 0m,
        MinimumAverageDollarVolume = 0m,
        LiquidityLookbackDays = 1
    };

    private static BacktestOptions Options => new()
    {
        TopN = 1,
        InitialCapital = 1_000m,
        SlippageBps = 0m,
        CommissionPerOrder = 0m
    };

    [Fact]
    public void Run_BuysAtNextMondayOpen_SellsWhatDropsOut_AndReportsHandCalculatedResults()
    {
        var backtester = new Backtester(BuildData(), OpenUniverse);

        var report = backtester.Run(RotationStrategy(), Options);

        // AAA: 100 shares bought at 10, sold at 12 (+200). BBB: 60 shares bought at 20, sold at 18 (-120).
        Assert.Equal(1_080m, report.FinalEquity);
        Assert.Equal(8m, report.TotalReturnPercent);
        Assert.Equal(2, report.ClosedTrades);
        Assert.Equal(4, report.Orders);
        Assert.Equal(50m, report.WinRatePercent);
        Assert.Empty(report.OpenPositions);
        Assert.Equal(
            [
                new BacktestTrade("AAA", Monday2, 10m, Monday3, 12m, 100m, 200m, 20m),
                new BacktestTrade("BBB", Monday3, 20m, Monday4, 18m, 60m, -120m, -10m)
            ],
            report.Trades);
        // Weekly closes: 1000, 100 x 11, 60 x 19, cash 1080.
        Assert.Equal([1_000m, 1_100m, 1_140m, 1_080m], report.EquityCurve.Select(point => point.Equity));
        Assert.Equal([Friday1, Friday2, Friday3, Friday4], report.EquityCurve.Select(point => point.Date));
        // Peak 1140 to 1080.
        Assert.Equal(5.2632m, report.MaxDrawdownPercent);
        // Benchmark bought at 100 on the first fill date and worth 110 at the end.
        Assert.Equal(10m, report.BenchmarkTotalReturnPercent);
        Assert.Equal(-2m, report.ExcessReturnPercent);
        Assert.Equal(4, report.Weeks);
        Assert.Contains(report.Warnings, warning => warning.Contains("Survivorship", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, warning => warning.Contains("Short test period", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, warning => warning.Contains("closed trades", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_SubtractsSlippageAndCommissionOnBothSides()
    {
        var backtester = new Backtester(BuildData(), OpenUniverse);
        var options = Options;
        options.SlippageBps = 100m;
        options.CommissionPerOrder = 1m;

        var report = backtester.Run(RotationStrategy(), options);

        // Buy 98.910891 AAA at 10.10 (999 budget after 1 commission); sell at 11.88 less 1 commission.
        var first = report.Trades[0];
        Assert.Equal(10.1m, first.EntryPrice);
        Assert.Equal(11.88m, first.ExitPrice);
        Assert.Equal(98.910891m, first.Quantity);
        Assert.InRange(first.ProfitLoss, 174.05m, 174.07m);
        Assert.True(report.FinalEquity < 1_080m);
    }

    [Fact]
    public void Run_KeepsAHoldingThatStaysInTheTopRanks_WithoutTrading()
    {
        var backtester = new Backtester(BuildData(), OpenUniverse);
        var alwaysAaa = new StubStrategy((symbol, _) => symbol == "AAA" ? 90m : 10m);

        var report = backtester.Run(alwaysAaa, Options);

        Assert.Equal(1, report.Orders);
        Assert.Equal(0, report.ClosedTrades);
        Assert.Null(report.WinRatePercent);
        var open = Assert.Single(report.OpenPositions);
        Assert.Equal("AAA", open.Symbol);
        Assert.Equal(Monday2, open.EntryDate);
    }

    [Fact]
    public void Run_OpensNoPositions_WhileTheRegimeFilterIsBelowItsThreshold()
    {
        var backtester = new Backtester(BuildData(), OpenUniverse);
        var options = Options;
        options.UseRegimeFilter = true;

        var blocked = backtester.Run(RotationStrategy(), options, regimeStrategy: new StubStrategy((_, _) => 20m));
        var allowed = backtester.Run(RotationStrategy(), options, regimeStrategy: new StubStrategy((_, _) => 80m));

        Assert.Equal(0, blocked.Orders);
        Assert.Equal(1_000m, blocked.FinalEquity);
        Assert.Equal(3, blocked.WeeksBlockedByRegimeFilter);
        Assert.Equal(1_080m, allowed.FinalEquity);
        Assert.Throws<ArgumentException>(() => backtester.Run(RotationStrategy(), options));
    }

    [Fact]
    public void Run_NeverShowsAStrategyDataFromAfterTheSignalDate()
    {
        var data = BuildData();
        var latestSeen = new List<(DateOnly AsOf, DateOnly Latest)>();
        var spy = new StubStrategy((_, _) => 60m, (asOf, view) =>
        {
            latestSeen.Add((asOf, view.Bars[^1].Date));
            if (view.BenchmarkBars.Count > 0)
            {
                latestSeen.Add((asOf, view.BenchmarkBars[^1].Date));
            }
        });

        new Backtester(data, OpenUniverse).Run(spy, Options);

        Assert.NotEmpty(latestSeen);
        Assert.All(latestSeen, seen => Assert.True(seen.Latest <= seen.AsOf));
        Assert.Equal([Friday1, Friday2, Friday3], latestSeen.Select(seen => seen.AsOf).Distinct());
    }

    [Fact]
    public void Rank_IsUnchangedWhenFutureBarsAreAdded()
    {
        var strategy = new ShortTermReversalStrategy(new ShortTermReversalOptions());
        var full = BuildData();
        var truncated = new MarketDataSet(
            full.Symbols.Select(symbol => new PriceSeries(symbol, full.Get(symbol)!.Bars.Where(bar => bar.Date <= Friday2))),
            "SPY");

        var withFuture = new Backtester(full, OpenUniverse).Rank(strategy, Friday2);
        var withoutFuture = new Backtester(truncated, OpenUniverse).Rank(strategy, Friday2);

        Assert.NotEmpty(withFuture);
        Assert.Equal(withoutFuture, withFuture);
    }

    [Fact]
    public void Rank_AppliesUniverseFilters_AndExcludesTheBenchmark()
    {
        var backtester = new Backtester(BuildData(), new UniverseOptions
        {
            MinimumPrice = 15m,
            MinimumAverageDollarVolume = 0m,
            LiquidityLookbackDays = 1
        });
        var illiquid = new Backtester(BuildData(), new UniverseOptions
        {
            MinimumPrice = 0m,
            MinimumAverageDollarVolume = 1_000_000_000m,
            LiquidityLookbackDays = 5
        });

        // AAA closes at 11 on that Friday, below the 15 minimum; BBB closes at 20.
        Assert.Equal(["BBB"], backtester.Rank(new StubStrategy((_, _) => 60m), Friday2).Select(item => item.Symbol));
        Assert.Empty(illiquid.Rank(new StubStrategy((_, _) => 60m), Friday2));
    }

    [Fact]
    public void Run_ReportsNoResult_WhenThePeriodHasFewerThanTwoWeeks()
    {
        var report = new Backtester(BuildData(), OpenUniverse).Run(RotationStrategy(), Options, Monday1, Friday1);

        Assert.Equal(1_000m, report.FinalEquity);
        Assert.Contains(report.Warnings, warning => warning.Contains("Not enough trading weeks", StringComparison.Ordinal));
    }

    [Fact]
    public void GroupIntoWeeks_AndMaxDrawdown_BehaveAsDocumented()
    {
        var weeks = Backtester.GroupIntoWeeks([Friday1, Monday1, Monday2, new DateOnly(2024, 1, 10)]);

        Assert.Equal(2, weeks.Count);
        Assert.Equal([Monday1, Friday1], weeks[0]);
        Assert.Equal(25m, Backtester.MaxDrawdownPercent([100m, 120m, 90m, 130m, 110m]));
        Assert.Equal(0m, Backtester.MaxDrawdownPercent([100m, 110m, 120m]));
    }

    [Fact]
    public void WalkForward_ChoosesTheBestVariantOnTraining_AndCompoundsOnlyTestPeriods()
    {
        // Twelve weeks: UP rises 1 a day, DOWN falls 0.5 a day, the benchmark is flat.
        var days = Enumerable.Range(0, 12 * 7)
            .Select(offset => Monday1.AddDays(offset))
            .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToArray();
        var data = new MarketDataSet(
            [
                new PriceSeries("UP", days.Select((date, index) => Flat(date, 100m + index))),
                new PriceSeries("DOWN", days.Select((date, index) => Flat(date, 100m - (index * 0.5m)))),
                new PriceSeries("SPY", days.Select(date => Flat(date, 100m)))
            ],
            "SPY");
        var variants = new[]
        {
            new StrategyVariant("picks DOWN", new StubStrategy((symbol, _) => symbol == "DOWN" ? 90m : 10m)),
            new StrategyVariant("picks UP", new StubStrategy((symbol, _) => symbol == "UP" ? 90m : 10m))
        };

        var report = new WalkForwardRunner(data, OpenUniverse).Run(variants, Options, trainWeeks: 4, testWeeks: 2);

        Assert.Equal(4, report.Folds.Count);
        Assert.All(report.Folds, fold =>
        {
            Assert.Equal("picks UP", fold.ChosenVariant);
            Assert.True(fold.TrainEnd < fold.TestStart);
            Assert.True(fold.TestReturnPercent > 0m);
        });
        // Test periods follow one another without gaps or overlap (Friday to the next Monday).
        Assert.Equal(report.Folds[0].TestEnd.AddDays(3), report.Folds[1].TestStart);
        Assert.True(report.CompoundedTestReturnPercent > report.Folds[0].TestReturnPercent);
        Assert.Equal(0m, Math.Round(report.CompoundedBenchmarkReturnPercent, 2));

        var tooShort = new WalkForwardRunner(data, OpenUniverse).Run(variants, Options, trainWeeks: 20, testWeeks: 4);
        Assert.Empty(tooShort.Folds);
        Assert.Contains(tooShort.Warnings, warning => warning.Contains("Not enough history", StringComparison.Ordinal));
    }

    /// <summary>Week 1 ranks AAA first, week 2 ranks BBB first, week 3 ranks nothing above 50.</summary>
    private static StubStrategy RotationStrategy()
    {
        return new StubStrategy((symbol, asOf) => (symbol, asOf) switch
        {
            ("AAA", _) when asOf == Friday1 => 80m,
            ("BBB", _) when asOf == Friday1 => 60m,
            ("AAA", _) when asOf == Friday2 => 40m,
            ("BBB", _) when asOf == Friday2 => 90m,
            _ => 30m
        });
    }

    private static MarketDataSet BuildData()
    {
        var days = Enumerable.Range(0, 26)
            .Select(offset => Monday1.AddDays(offset))
            .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToArray();

        var aaa = days.Select(date => date switch
        {
            _ when date == Monday2 => new DailyBar(date, 10m, 10.5m, 9.5m, 10m, 1_000m),
            _ when date == Friday2 => new DailyBar(date, 11m, 11m, 11m, 11m, 1_000m),
            _ when date == Monday3 => new DailyBar(date, 12m, 12m, 12m, 12m, 1_000m),
            _ => Flat(date, 10m)
        });
        var bbb = days.Select(date => date switch
        {
            _ when date == Friday3 => new DailyBar(date, 19m, 19m, 19m, 19m, 1_000m),
            _ when date == Monday4 => new DailyBar(date, 18m, 18m, 18m, 18m, 1_000m),
            _ => Flat(date, 20m)
        });
        var spy = days.Select(date => date == Friday4 ? Flat(date, 110m) : Flat(date, 100m));

        return new MarketDataSet(
            [new PriceSeries("AAA", aaa), new PriceSeries("BBB", bbb), new PriceSeries("SPY", spy)],
            "SPY");
    }

    private static DailyBar Flat(DateOnly date, decimal price) => new(date, price, price, price, price, 1_000m);

    private sealed class StubStrategy(
        Func<string, DateOnly, decimal?> score,
        Action<DateOnly, StrategyData>? observe = null) : IStrategy
    {
        public string Name => "Stub";

        public StrategyDataRequirement RequiredData => new(1);

        public decimal? Score(string symbol, DateOnly asOfDate, StrategyData data)
        {
            observe?.Invoke(asOfDate, data);
            return score(symbol, asOfDate);
        }
    }
}
