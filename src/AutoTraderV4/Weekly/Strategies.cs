namespace AutoTraderV4.Weekly;

/// <summary>
/// A weekly strategy that evaluates one stock and returns its own index:
/// 0 = strong sell signal, 50 = neutral, 100 = strong buy signal.
/// </summary>
public interface IStrategy
{
    string Name { get; }

    StrategyDataRequirement RequiredData { get; }

    /// <summary>
    /// Scores <paramref name="symbol"/> using only data dated on or before <paramref name="asOfDate"/>.
    /// Returns null when there is not enough data to measure the signal.
    /// </summary>
    decimal? Score(string symbol, DateOnly asOfDate, StrategyData data);
}

public static class StrategyMath
{
    /// <summary>Maps a signed value onto 0-100, with 0 at 50 and ±<paramref name="fullScale"/> at the extremes.</summary>
    public static decimal ToIndex(decimal value, decimal fullScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fullScale);
        return 50m + (Math.Clamp(value / fullScale, -1m, 1m) * 50m);
    }

    public static decimal? AverageClose(IReadOnlyList<DailyBar> bars, int period)
    {
        if (period <= 0 || bars.Count < period)
        {
            return null;
        }

        var sum = 0m;
        for (var index = bars.Count - period; index < bars.Count; index++)
        {
            sum += bars[index].Close;
        }

        return sum / period;
    }

    public static decimal PercentDifference(decimal value, decimal reference)
    {
        return reference == 0m ? 0m : ((value - reference) / reference) * 100m;
    }
}

public sealed class TrendFollowingOptions
{
    public int ShortPeriod { get; set; } = 50;
    public int LongPeriod { get; set; } = 200;

    /// <summary>The short average sitting this many percent above the long one counts as a full-strength trend.</summary>
    public decimal AverageGapFullScalePercent { get; set; } = 10m;

    /// <summary>The price sitting this many percent above the short average counts as full strength.</summary>
    public decimal PriceGapFullScalePercent { get; set; } = 10m;

    public void Validate()
    {
        if (ShortPeriod <= 0 || LongPeriod <= ShortPeriod)
        {
            throw new ArgumentException("TrendFollowing: LongPeriod must be greater than ShortPeriod, and both positive.");
        }

        if (AverageGapFullScalePercent <= 0m || PriceGapFullScalePercent <= 0m)
        {
            throw new ArgumentException("TrendFollowing: full-scale percentages must be positive.");
        }
    }
}

/// <summary>
/// Uptrend strength: half from the short moving average against the long one (above 50 once it has
/// crossed above), half from the price against the short average (falls when the trend breaks).
/// </summary>
public sealed class TrendFollowingStrategy : IStrategy
{
    public const string StrategyName = "TrendFollowing";
    private readonly TrendFollowingOptions _options;

    public TrendFollowingStrategy(TrendFollowingOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public string Name => StrategyName;

    public StrategyDataRequirement RequiredData => new(_options.LongPeriod);

    public decimal? Score(string symbol, DateOnly asOfDate, StrategyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var shortAverage = StrategyMath.AverageClose(data.Bars, _options.ShortPeriod);
        var longAverage = StrategyMath.AverageClose(data.Bars, _options.LongPeriod);
        if (shortAverage is null || longAverage is null)
        {
            return null;
        }

        var averageGap = StrategyMath.PercentDifference(shortAverage.Value, longAverage.Value);
        var priceGap = StrategyMath.PercentDifference(data.Bars[^1].Close, shortAverage.Value);
        return (StrategyMath.ToIndex(averageGap, _options.AverageGapFullScalePercent)
            + StrategyMath.ToIndex(priceGap, _options.PriceGapFullScalePercent)) / 2m;
    }
}

public sealed class ShortTermReversalOptions
{
    /// <summary>Trading days in the look-back; five is one week.</summary>
    public int LookbackDays { get; set; } = 5;

    /// <summary>A fall of this many percent over the look-back scores 100; the same rise scores 0.</summary>
    public decimal FullScalePercent { get; set; } = 15m;

    public void Validate()
    {
        if (LookbackDays <= 0)
        {
            throw new ArgumentException("ShortTermReversal: LookbackDays must be positive.");
        }

        if (FullScalePercent <= 0m)
        {
            throw new ArgumentException("ShortTermReversal: FullScalePercent must be positive.");
        }
    }
}

/// <summary>Last week's losers score high and last week's winners score low.</summary>
public sealed class ShortTermReversalStrategy : IStrategy
{
    public const string StrategyName = "ShortTermReversal";
    private readonly ShortTermReversalOptions _options;

    public ShortTermReversalStrategy(ShortTermReversalOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public string Name => StrategyName;

    public StrategyDataRequirement RequiredData => new(_options.LookbackDays + 1);

    public decimal? Score(string symbol, DateOnly asOfDate, StrategyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Bars.Count < _options.LookbackDays + 1)
        {
            return null;
        }

        var previousClose = data.Bars[data.Bars.Count - 1 - _options.LookbackDays].Close;
        if (previousClose <= 0m)
        {
            return null;
        }

        var periodReturn = StrategyMath.PercentDifference(data.Bars[^1].Close, previousClose);
        return StrategyMath.ToIndex(-periodReturn, _options.FullScalePercent);
    }
}

public sealed class MarketRegimeOptions
{
    public int Period { get; set; } = 200;

    /// <summary>The benchmark sitting this many percent above its average scores 100; the same below scores 0.</summary>
    public decimal FullScalePercent { get; set; } = 10m;

    public void Validate()
    {
        if (Period <= 0)
        {
            throw new ArgumentException("MarketRegime: Period must be positive.");
        }

        if (FullScalePercent <= 0m)
        {
            throw new ArgumentException("MarketRegime: FullScalePercent must be positive.");
        }
    }
}

/// <summary>
/// Market health from the benchmark against its long moving average. The index is the same for every
/// stock; above 50 means the benchmark is above its average. Used by the backtester as a filter on buys.
/// </summary>
public sealed class MarketRegimeStrategy : IStrategy
{
    public const string StrategyName = "MarketRegime";
    private readonly MarketRegimeOptions _options;

    public MarketRegimeStrategy(MarketRegimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public string Name => StrategyName;

    public StrategyDataRequirement RequiredData => new(_options.Period, NeedsBenchmark: true);

    public decimal? Score(string symbol, DateOnly asOfDate, StrategyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var average = StrategyMath.AverageClose(data.BenchmarkBars, _options.Period);
        if (average is null)
        {
            return null;
        }

        var gap = StrategyMath.PercentDifference(data.BenchmarkBars[^1].Close, average.Value);
        return StrategyMath.ToIndex(gap, _options.FullScalePercent);
    }
}
