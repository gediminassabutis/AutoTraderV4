namespace AutoTraderV4.Weekly;

public sealed record StrategyVariant(string Label, IStrategy Strategy);

public sealed record WalkForwardFold(
    DateOnly TrainStart,
    DateOnly TrainEnd,
    DateOnly TestStart,
    DateOnly TestEnd,
    string ChosenVariant,
    decimal TrainReturnPercent,
    decimal TestReturnPercent,
    decimal TestBenchmarkReturnPercent,
    decimal TestMaxDrawdownPercent,
    int TestClosedTrades);

public sealed class WalkForwardReport
{
    public string Strategy { get; init; } = string.Empty;
    public int TrainWeeks { get; init; }
    public int TestWeeks { get; init; }
    public IReadOnlyList<WalkForwardFold> Folds { get; init; } = [];

    /// <summary>The out-of-sample test periods compounded together.</summary>
    public decimal CompoundedTestReturnPercent { get; init; }
    public decimal CompoundedBenchmarkReturnPercent { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Walk-forward testing: pick the best parameter variant on one period, then measure it on the
/// following period it has never seen. Only the test periods count towards the result.
/// </summary>
public sealed class WalkForwardRunner
{
    private readonly MarketDataSet _data;
    private readonly Backtester _backtester;

    public WalkForwardRunner(MarketDataSet data, UniverseOptions universe)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _backtester = new Backtester(data, universe);
    }

    public WalkForwardReport Run(
        IReadOnlyList<StrategyVariant> variants,
        BacktestOptions options,
        int trainWeeks,
        int testWeeks,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        IStrategy? regimeStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(options);
        if (variants.Count == 0)
        {
            throw new ArgumentException("At least one strategy variant is required.", nameof(variants));
        }

        // Two weeks is the least a backtest can trade over: one to decide, one to fill.
        ArgumentOutOfRangeException.ThrowIfLessThan(trainWeeks, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(testWeeks, 2);

        var benchmark = _data.Benchmark
            ?? throw new InvalidOperationException($"No bars are stored for the benchmark {_data.BenchmarkSymbol}.");
        var weeks = Backtester.GroupIntoWeeks(benchmark.Bars
            .Select(bar => bar.Date)
            .Where(date => (startDate is null || date >= startDate) && (endDate is null || date <= endDate)));

        var folds = new List<WalkForwardFold>();
        var compounded = 1m;
        var compoundedBenchmark = 1m;
        for (var start = 0; start + trainWeeks + testWeeks <= weeks.Count; start += testWeeks)
        {
            var trainStart = weeks[start][0];
            var trainEnd = weeks[start + trainWeeks - 1][^1];
            var testStart = weeks[start + trainWeeks][0];
            var testEnd = weeks[start + trainWeeks + testWeeks - 1][^1];

            StrategyVariant? best = null;
            var bestReturn = decimal.MinValue;
            foreach (var variant in variants)
            {
                var train = _backtester.Run(variant.Strategy, options, trainStart, trainEnd, regimeStrategy);
                if (train.TotalReturnPercent > bestReturn)
                {
                    best = variant;
                    bestReturn = train.TotalReturnPercent;
                }
            }

            var test = _backtester.Run(best!.Strategy, options, testStart, testEnd, regimeStrategy);
            folds.Add(new WalkForwardFold(
                trainStart,
                trainEnd,
                testStart,
                testEnd,
                best.Label,
                bestReturn,
                test.TotalReturnPercent,
                test.BenchmarkTotalReturnPercent,
                test.MaxDrawdownPercent,
                test.ClosedTrades));
            compounded *= 1m + (test.TotalReturnPercent / 100m);
            compoundedBenchmark *= 1m + (test.BenchmarkTotalReturnPercent / 100m);
        }

        var warnings = new List<string>
        {
            "Survivorship bias: the universe is today's symbol list, so companies that were delisted or acquired during the test period are missing. Real results would be worse.",
            "Each test period starts from cash, so positions are not carried between folds."
        };
        if (folds.Count == 0)
        {
            warnings.Add($"Not enough history for one fold: {trainWeeks + testWeeks} weeks are needed and {weeks.Count} are available.");
        }
        else if (folds.Count < 4)
        {
            warnings.Add($"Only {folds.Count} out-of-sample periods. Too few to judge whether the strategy holds up.");
        }

        var compoundedReturn = (compounded - 1m) * 100m;
        if (folds.Count > 0 && folds.Average(fold => fold.TrainReturnPercent) > 0m && compoundedReturn <= 0m)
        {
            warnings.Add("The strategy made money on the periods it was fitted to but not on the periods that followed. That is the signature of overfitting.");
        }

        return new WalkForwardReport
        {
            Strategy = variants[0].Strategy.Name,
            TrainWeeks = trainWeeks,
            TestWeeks = testWeeks,
            Folds = folds,
            CompoundedTestReturnPercent = Math.Round(compoundedReturn, 4, MidpointRounding.AwayFromZero),
            CompoundedBenchmarkReturnPercent = Math.Round((compoundedBenchmark - 1m) * 100m, 4, MidpointRounding.AwayFromZero),
            Warnings = warnings
        };
    }
}
