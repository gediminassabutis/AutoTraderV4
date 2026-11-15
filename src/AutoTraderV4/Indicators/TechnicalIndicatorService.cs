using System.Collections.Concurrent;
using AutoTraderV4.Services;

namespace AutoTraderV4.Indicators;

/// <summary>Supplies completed daily bars, oldest first, for a symbol.</summary>
public interface IPriceHistoryProvider
{
    string ProviderName { get; }

    /// <summary>Returns null when the provider does not cover the symbol.</summary>
    Task<IReadOnlyList<PriceBar>?> GetDailyBarsAsync(
        string symbol,
        int minimumBars,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Indicators computed from daily bars. A null value means there was not enough history to measure it.
/// </summary>
public sealed record TechnicalIndicatorSnapshot
{
    public string Symbol { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string BenchmarkSymbol { get; init; } = string.Empty;
    public int BarCount { get; init; }
    public DateTimeOffset LastBarAtUtc { get; init; }
    public DateTimeOffset CalculatedAtUtc { get; init; }
    public decimal LastClose { get; init; }
    public decimal? Sma20 { get; init; }
    public decimal? Sma50 { get; init; }
    public decimal? Sma200 { get; init; }
    public decimal? Ema12 { get; init; }
    public decimal? Ema26 { get; init; }
    public decimal? Rsi14 { get; init; }
    public decimal? Rsi14FiveBarsAgo { get; init; }
    public decimal? Macd { get; init; }
    public decimal? MacdSignal { get; init; }
    public decimal? MacdHistogram { get; init; }
    public decimal? Atr14 { get; init; }
    public decimal? AtrPercent { get; init; }
    public decimal? BollingerUpper { get; init; }
    public decimal? BollingerMiddle { get; init; }
    public decimal? BollingerLower { get; init; }
    public decimal? BollingerPercentB { get; init; }
    public decimal? Vwap20 { get; init; }
    public decimal? Momentum21Percent { get; init; }
    public decimal? Momentum63Percent { get; init; }
    public decimal? Momentum126Percent { get; init; }
    public decimal? AverageVolume20 { get; init; }
    public decimal? AverageVolume50 { get; init; }
    public decimal? AverageDollarVolume20 { get; init; }

    /// <summary>The symbol's 63-bar return minus the benchmark's, in percentage points.</summary>
    public decimal? RelativeStrengthVsBenchmarkPercent { get; init; }

    public bool? PriceAboveSma50 => Sma50 is null ? null : LastClose > Sma50;

    public bool? Sma50AboveSma200 => Sma50 is null || Sma200 is null ? null : Sma50 > Sma200;

    /// <summary>Five-bar change in RSI scaled to -100..100 (a 10-point RSI move is the full range).</summary>
    public decimal? RsiTrendScore => Rsi14 is null || Rsi14FiveBarsAgo is null
        ? null
        : Math.Clamp((Rsi14.Value - Rsi14FiveBarsAgo.Value) * 10m, -100m, 100m);

    /// <summary>20-bar average volume against the 50-bar average, scaled to -100..100 (±50% is the full range).</summary>
    public decimal? VolumeTrendScore => AverageVolume20 is null || AverageVolume50 is null || AverageVolume50 == 0m
        ? null
        : Math.Clamp(((AverageVolume20.Value / AverageVolume50.Value) - 1m) * 200m, -100m, 100m);

    /// <summary>How far the last close sits below the 20-bar average, in percent. Zero when at or above it.</summary>
    public decimal? PriceDislocationPercent => Sma20 is null || Sma20 == 0m
        ? null
        : Math.Max(0m, ((Sma20.Value - LastClose) / Sma20.Value) * 100m);

    /// <summary>
    /// Copies the measured technical inputs onto a strategy-engine snapshot. Returns the names of the
    /// engine inputs that could not be measured; those fields are left untouched.
    /// Fundamental, sentiment, macro, liquidity, spread and relative-strength-percentile inputs are not
    /// technical indicators and are never set here.
    /// </summary>
    public IReadOnlyList<string> ApplyTo(MarketSignalSnapshot signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var missing = new List<string>();

        Assign(Sma50, value => signals.FiftyDayMovingAverage = value, nameof(signals.FiftyDayMovingAverage));
        Assign(Sma200, value => signals.TwoHundredDayMovingAverage = value, nameof(signals.TwoHundredDayMovingAverage));
        Assign(Rsi14, value => signals.RsiValue = value, nameof(signals.RsiValue));
        Assign(RsiTrendScore, value => signals.RsiTrendScore = value, nameof(signals.RsiTrendScore));
        Assign(VolumeTrendScore, value => signals.VolumeTrendScore = value, nameof(signals.VolumeTrendScore));
        Assign(PriceDislocationPercent, value => signals.PriceDislocationPercent = value, nameof(signals.PriceDislocationPercent));
        Assign(
            AtrPercent is null ? null : Math.Min(AtrPercent.Value, 30m),
            value => signals.AtrPercent = value,
            nameof(signals.AtrPercent));

        return missing;

        void Assign(decimal? value, Action<decimal> assign, string name)
        {
            if (value is null)
            {
                missing.Add(name);
                return;
            }

            assign(Math.Round(value.Value, 4, MidpointRounding.AwayFromZero));
        }
    }
}

public sealed class TechnicalIndicatorService
{
    public const string DefaultBenchmarkSymbol = "SPY";

    /// <summary>Enough daily bars for a 200-day average plus the RSI look-back.</summary>
    public const int RequiredBars = 220;

    private const int RelativeStrengthPeriod = 63;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private readonly IReadOnlyList<IPriceHistoryProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CachedBars> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TechnicalIndicatorService(IEnumerable<IPriceHistoryProvider> providers, TimeProvider timeProvider)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToList();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public bool HistoryProviderConfigured => _providers.Count > 0;

    /// <summary>
    /// Returns null when no configured provider covers the symbol or it has no bars.
    /// Provider failures (authentication, rate limits, network) propagate to the caller.
    /// </summary>
    public async Task<TechnicalIndicatorSnapshot?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();

        var history = await GetBarsAsync(normalizedSymbol, cancellationToken);
        if (history is null || history.Bars.Count == 0)
        {
            return null;
        }

        decimal? benchmarkReturn = null;
        if (!IsBenchmark(normalizedSymbol))
        {
            var benchmark = await GetBarsAsync(DefaultBenchmarkSymbol, cancellationToken);
            if (benchmark is not null)
            {
                benchmarkReturn = TechnicalIndicators.RateOfChangePercent(
                    benchmark.Bars.Select(bar => bar.Close).ToArray(),
                    RelativeStrengthPeriod);
            }
        }

        return Calculate(
            normalizedSymbol,
            history.ProviderName,
            history.Bars,
            benchmarkReturn,
            _timeProvider.GetUtcNow());
    }

    public static TechnicalIndicatorSnapshot Calculate(
        string symbol,
        string providerName,
        IReadOnlyList<PriceBar> bars,
        decimal? benchmarkReturnPercent,
        DateTimeOffset calculatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
        {
            throw new ArgumentException("At least one bar is required.", nameof(bars));
        }

        var closes = bars.Select(bar => bar.Close).ToArray();
        var volumes = bars.Select(bar => bar.Volume).ToArray();
        var dollarVolumes = bars.Select(bar => bar.Close * bar.Volume).ToArray();
        var lastClose = closes[^1];
        var macd = TechnicalIndicators.Macd(closes);
        var bollinger = TechnicalIndicators.BollingerBands(closes);
        var atr = TechnicalIndicators.Atr(bars);
        var momentum63 = TechnicalIndicators.RateOfChangePercent(closes, RelativeStrengthPeriod);

        return new TechnicalIndicatorSnapshot
        {
            Symbol = symbol,
            ProviderName = providerName,
            BenchmarkSymbol = DefaultBenchmarkSymbol,
            BarCount = bars.Count,
            LastBarAtUtc = bars[^1].TimestampUtc,
            CalculatedAtUtc = calculatedAtUtc,
            LastClose = lastClose,
            Sma20 = TechnicalIndicators.Sma(closes, 20),
            Sma50 = TechnicalIndicators.Sma(closes, 50),
            Sma200 = TechnicalIndicators.Sma(closes, 200),
            Ema12 = TechnicalIndicators.Ema(closes, 12),
            Ema26 = TechnicalIndicators.Ema(closes, 26),
            Rsi14 = TechnicalIndicators.Rsi(closes),
            Rsi14FiveBarsAgo = closes.Length > 5 ? TechnicalIndicators.Rsi(closes[..^5]) : null,
            Macd = macd?.Macd,
            MacdSignal = macd?.Signal,
            MacdHistogram = macd?.Histogram,
            Atr14 = atr,
            AtrPercent = atr is null || lastClose == 0m ? null : (atr.Value / lastClose) * 100m,
            BollingerUpper = bollinger?.Upper,
            BollingerMiddle = bollinger?.Middle,
            BollingerLower = bollinger?.Lower,
            BollingerPercentB = bollinger?.PercentB,
            Vwap20 = TechnicalIndicators.Vwap(bars, 20),
            Momentum21Percent = TechnicalIndicators.RateOfChangePercent(closes, 21),
            Momentum63Percent = momentum63,
            Momentum126Percent = TechnicalIndicators.RateOfChangePercent(closes, 126),
            AverageVolume20 = TechnicalIndicators.Sma(volumes, 20),
            AverageVolume50 = TechnicalIndicators.Sma(volumes, 50),
            AverageDollarVolume20 = TechnicalIndicators.Sma(dollarVolumes, 20),
            RelativeStrengthVsBenchmarkPercent = momentum63 is null || benchmarkReturnPercent is null
                ? null
                : momentum63.Value - benchmarkReturnPercent.Value
        };
    }

    private static bool IsBenchmark(string symbol)
    {
        return string.Equals(
            AlpacaSymbol.FromTicker(symbol),
            DefaultBenchmarkSymbol,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CachedBars?> GetBarsAsync(string symbol, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_cache.TryGetValue(symbol, out var cached) && now - cached.FetchedAtUtc < CacheDuration)
        {
            return cached;
        }

        foreach (var provider in _providers)
        {
            var bars = await provider.GetDailyBarsAsync(symbol, RequiredBars, cancellationToken);
            if (bars is null || bars.Count == 0)
            {
                continue;
            }

            var entry = new CachedBars(provider.ProviderName, bars, now);
            _cache[symbol] = entry;
            return entry;
        }

        return null;
    }

    private sealed record CachedBars(string ProviderName, IReadOnlyList<PriceBar> Bars, DateTimeOffset FetchedAtUtc);
}
