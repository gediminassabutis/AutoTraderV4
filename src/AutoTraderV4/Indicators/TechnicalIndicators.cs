namespace AutoTraderV4.Indicators;

/// <summary>One completed price bar. Bars are always ordered oldest first.</summary>
public sealed record PriceBar(
    DateTimeOffset TimestampUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal? Vwap = null);

public sealed record MacdResult(decimal Macd, decimal Signal, decimal Histogram);

public sealed record BollingerBandsResult(decimal Upper, decimal Middle, decimal Lower, decimal PercentB);

/// <summary>
/// Pure technical-indicator calculations. Every method takes values ordered oldest first and
/// returns null when there is not enough history, so callers can never mistake a default for a measurement.
/// </summary>
public static class TechnicalIndicators
{
    public static decimal? Sma(IReadOnlyList<decimal> values, int period)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        if (values.Count < period)
        {
            return null;
        }

        var sum = 0m;
        for (var index = values.Count - period; index < values.Count; index++)
        {
            sum += values[index];
        }

        return sum / period;
    }

    /// <summary>
    /// Exponential moving average series seeded with the SMA of the first <paramref name="period"/> values.
    /// The first element corresponds to input index <c>period - 1</c>.
    /// </summary>
    public static IReadOnlyList<decimal> EmaSeries(IReadOnlyList<decimal> values, int period)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        if (values.Count < period)
        {
            return [];
        }

        var series = new List<decimal>(values.Count - period + 1);
        var seed = 0m;
        for (var index = 0; index < period; index++)
        {
            seed += values[index];
        }

        var ema = seed / period;
        series.Add(ema);
        var multiplier = 2m / (period + 1);
        for (var index = period; index < values.Count; index++)
        {
            ema = ((values[index] - ema) * multiplier) + ema;
            series.Add(ema);
        }

        return series;
    }

    public static decimal? Ema(IReadOnlyList<decimal> values, int period)
    {
        var series = EmaSeries(values, period);
        return series.Count == 0 ? null : series[^1];
    }

    /// <summary>Relative Strength Index using Wilder's smoothing. Needs <c>period + 1</c> values.</summary>
    public static decimal? Rsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        if (closes.Count < period + 1)
        {
            return null;
        }

        var averageGain = 0m;
        var averageLoss = 0m;
        for (var index = 1; index <= period; index++)
        {
            var change = closes[index] - closes[index - 1];
            if (change >= 0m)
            {
                averageGain += change;
            }
            else
            {
                averageLoss -= change;
            }
        }

        averageGain /= period;
        averageLoss /= period;

        for (var index = period + 1; index < closes.Count; index++)
        {
            var change = closes[index] - closes[index - 1];
            var gain = change > 0m ? change : 0m;
            var loss = change < 0m ? -change : 0m;
            averageGain = ((averageGain * (period - 1)) + gain) / period;
            averageLoss = ((averageLoss * (period - 1)) + loss) / period;
        }

        if (averageLoss == 0m)
        {
            return averageGain == 0m ? 50m : 100m;
        }

        var relativeStrength = averageGain / averageLoss;
        return 100m - (100m / (1m + relativeStrength));
    }

    public static MacdResult? Macd(
        IReadOnlyList<decimal> closes,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fastPeriod);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(signalPeriod);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(slowPeriod, fastPeriod);
        if (closes.Count < slowPeriod + signalPeriod - 1)
        {
            return null;
        }

        var fast = EmaSeries(closes, fastPeriod);
        var slow = EmaSeries(closes, slowPeriod);
        var offset = slowPeriod - fastPeriod;
        var macdSeries = new List<decimal>(slow.Count);
        for (var index = 0; index < slow.Count; index++)
        {
            macdSeries.Add(fast[index + offset] - slow[index]);
        }

        var signal = Ema(macdSeries, signalPeriod);
        if (signal is null)
        {
            return null;
        }

        var macd = macdSeries[^1];
        return new MacdResult(macd, signal.Value, macd - signal.Value);
    }

    /// <summary>Average True Range using Wilder's smoothing. Needs <c>period + 1</c> bars.</summary>
    public static decimal? Atr(IReadOnlyList<PriceBar> bars, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        if (bars.Count < period + 1)
        {
            return null;
        }

        var atr = 0m;
        for (var index = 1; index <= period; index++)
        {
            atr += TrueRange(bars[index], bars[index - 1].Close);
        }

        atr /= period;
        for (var index = period + 1; index < bars.Count; index++)
        {
            atr = ((atr * (period - 1)) + TrueRange(bars[index], bars[index - 1].Close)) / period;
        }

        return atr;
    }

    public static BollingerBandsResult? BollingerBands(
        IReadOnlyList<decimal> closes,
        int period = 20,
        decimal standardDeviations = 2m)
    {
        var middle = Sma(closes, period);
        if (middle is null)
        {
            return null;
        }

        var sumOfSquares = 0m;
        for (var index = closes.Count - period; index < closes.Count; index++)
        {
            var deviation = closes[index] - middle.Value;
            sumOfSquares += deviation * deviation;
        }

        var standardDeviation = (decimal)Math.Sqrt((double)(sumOfSquares / period));
        var upper = middle.Value + (standardDeviations * standardDeviation);
        var lower = middle.Value - (standardDeviations * standardDeviation);
        var percentB = upper == lower ? 0.5m : (closes[^1] - lower) / (upper - lower);
        return new BollingerBandsResult(upper, middle.Value, lower, percentB);
    }

    /// <summary>Volume-weighted average of the typical price over the last <paramref name="period"/> bars.</summary>
    public static decimal? Vwap(IReadOnlyList<PriceBar> bars, int period)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        if (bars.Count < period)
        {
            return null;
        }

        var priceVolume = 0m;
        var volume = 0m;
        for (var index = bars.Count - period; index < bars.Count; index++)
        {
            var bar = bars[index];
            var typicalPrice = bar.Vwap ?? ((bar.High + bar.Low + bar.Close) / 3m);
            priceVolume += typicalPrice * bar.Volume;
            volume += bar.Volume;
        }

        return volume == 0m ? null : priceVolume / volume;
    }

    /// <summary>Percentage change over the last <paramref name="period"/> bars.</summary>
    public static decimal? RateOfChangePercent(IReadOnlyList<decimal> values, int period)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        if (values.Count < period + 1)
        {
            return null;
        }

        var previous = values[values.Count - 1 - period];
        return previous == 0m ? null : ((values[^1] - previous) / previous) * 100m;
    }

    private static decimal TrueRange(PriceBar bar, decimal previousClose)
    {
        return Math.Max(
            bar.High - bar.Low,
            Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
    }
}
