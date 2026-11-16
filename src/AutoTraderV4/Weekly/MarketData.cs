namespace AutoTraderV4.Weekly;

/// <summary>One completed trading session for a symbol.</summary>
public sealed record DailyBar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

/// <summary>A symbol's daily bars, ordered oldest first with one bar per date.</summary>
public sealed class PriceSeries
{
    private static readonly IReadOnlyList<DailyBar> Empty = [];
    private readonly DailyBar[] _bars;

    public PriceSeries(string symbol, IEnumerable<DailyBar> bars)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(bars);
        Symbol = symbol.Trim().ToUpperInvariant();
        _bars = bars
            .GroupBy(bar => bar.Date)
            .Select(group => group.Last())
            .OrderBy(bar => bar.Date)
            .ToArray();
    }

    public string Symbol { get; }

    public IReadOnlyList<DailyBar> Bars => _bars;

    /// <summary>Bars dated on or before <paramref name="asOfDate"/>. Later bars are not reachable from the result.</summary>
    public IReadOnlyList<DailyBar> UpTo(DateOnly asOfDate)
    {
        var count = CountOnOrBefore(asOfDate);
        return count == 0 ? Empty : new ArraySegment<DailyBar>(_bars, 0, count);
    }

    public DailyBar? BarOn(DateOnly date)
    {
        var count = CountOnOrBefore(date);
        return count > 0 && _bars[count - 1].Date == date ? _bars[count - 1] : null;
    }

    public DailyBar? LastOnOrBefore(DateOnly date)
    {
        var count = CountOnOrBefore(date);
        return count > 0 ? _bars[count - 1] : null;
    }

    private int CountOnOrBefore(DateOnly date)
    {
        var low = 0;
        var high = _bars.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_bars[middle].Date <= date)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}

/// <summary>What a strategy needs before it can produce a score.</summary>
public sealed record StrategyDataRequirement(int MinimumBars, bool NeedsBenchmark = false);

/// <summary>
/// The only data a strategy can see: bars dated on or before <see cref="AsOfDate"/>.
/// It is built by <see cref="MarketDataSet.View"/>, which is what prevents look-ahead.
/// </summary>
public sealed class StrategyData
{
    public StrategyData(DateOnly asOfDate, IReadOnlyList<DailyBar> bars, IReadOnlyList<DailyBar> benchmarkBars)
    {
        AsOfDate = asOfDate;
        Bars = bars ?? throw new ArgumentNullException(nameof(bars));
        BenchmarkBars = benchmarkBars ?? throw new ArgumentNullException(nameof(benchmarkBars));
    }

    public DateOnly AsOfDate { get; }

    /// <summary>The scored symbol's bars, oldest first.</summary>
    public IReadOnlyList<DailyBar> Bars { get; }

    /// <summary>The market benchmark's bars, oldest first.</summary>
    public IReadOnlyList<DailyBar> BenchmarkBars { get; }
}

public sealed class MarketDataSet
{
    private static readonly IReadOnlyList<DailyBar> NoBars = [];
    private readonly Dictionary<string, PriceSeries> _series;

    public MarketDataSet(IEnumerable<PriceSeries> series, string benchmarkSymbol)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkSymbol);
        BenchmarkSymbol = benchmarkSymbol.Trim().ToUpperInvariant();
        _series = series.ToDictionary(item => item.Symbol, StringComparer.OrdinalIgnoreCase);
    }

    public string BenchmarkSymbol { get; }

    public IReadOnlyCollection<string> Symbols => _series.Keys;

    public PriceSeries? Benchmark => Get(BenchmarkSymbol);

    public PriceSeries? Get(string symbol)
    {
        return _series.TryGetValue(symbol, out var series) ? series : null;
    }

    public StrategyData View(string symbol, DateOnly asOfDate)
    {
        return new StrategyData(
            asOfDate,
            Get(symbol)?.UpTo(asOfDate) ?? NoBars,
            Benchmark?.UpTo(asOfDate) ?? NoBars);
    }
}
