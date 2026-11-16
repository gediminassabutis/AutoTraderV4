using System.Globalization;

namespace AutoTraderV4.Weekly;

public sealed record BacktestTrade(
    string Symbol,
    DateOnly EntryDate,
    decimal EntryPrice,
    DateOnly ExitDate,
    decimal ExitPrice,
    decimal Quantity,
    decimal ProfitLoss,
    decimal ReturnPercent);

public sealed record OpenBacktestPosition(
    string Symbol,
    DateOnly EntryDate,
    decimal EntryPrice,
    decimal Quantity,
    decimal LastPrice,
    decimal UnrealisedProfitLoss);

public sealed record EquityPoint(DateOnly Date, decimal Equity, decimal BenchmarkEquity);

public sealed class BacktestReport
{
    public string Strategy { get; init; } = string.Empty;
    public string BenchmarkSymbol { get; init; } = string.Empty;
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int Weeks { get; init; }
    public decimal InitialCapital { get; init; }
    public decimal FinalEquity { get; init; }
    public decimal TotalReturnPercent { get; init; }
    public decimal AnnualisedReturnPercent { get; init; }
    public decimal MaxDrawdownPercent { get; init; }

    /// <summary>Share of closed trades that made money after costs. Null when no trade has closed.</summary>
    public decimal? WinRatePercent { get; init; }
    public int ClosedTrades { get; init; }
    public int Orders { get; init; }
    public decimal BenchmarkTotalReturnPercent { get; init; }
    public decimal BenchmarkMaxDrawdownPercent { get; init; }
    public decimal ExcessReturnPercent { get; init; }
    public int WeeksBlockedByRegimeFilter { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<EquityPoint> EquityCurve { get; init; } = [];
    public IReadOnlyList<BacktestTrade> Trades { get; init; } = [];
    public IReadOnlyList<OpenBacktestPosition> OpenPositions { get; init; } = [];
}

public sealed record RankedSymbol(string Symbol, decimal Score);

/// <summary>
/// Replays history week by week for one strategy. Signals use data up to the last trading day of a
/// week; orders fill at the open of the first trading day of the following week, less costs.
/// </summary>
public sealed class Backtester
{
    private readonly MarketDataSet _data;
    private readonly UniverseOptions _universe;

    public Backtester(MarketDataSet data, UniverseOptions universe)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _universe = universe ?? throw new ArgumentNullException(nameof(universe));
    }

    /// <summary>
    /// Symbols that pass the universe filters and can be scored on <paramref name="asOfDate"/>, best first.
    /// Only data dated on or before <paramref name="asOfDate"/> is used.
    /// </summary>
    public IReadOnlyList<RankedSymbol> Rank(IStrategy strategy, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var ranked = new List<RankedSymbol>();
        foreach (var symbol in _data.Symbols)
        {
            if (string.Equals(symbol, _data.BenchmarkSymbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var view = _data.View(symbol, asOfDate);
            if (!IsEligible(view, asOfDate))
            {
                continue;
            }

            var score = strategy.Score(symbol, asOfDate, view);
            if (score is not null)
            {
                ranked.Add(new RankedSymbol(symbol, Math.Clamp(score.Value, 0m, 100m)));
            }
        }

        return ranked
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .ToArray();
    }

    public BacktestReport Run(
        IStrategy strategy,
        BacktestOptions options,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        IStrategy? regimeStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (options.UseRegimeFilter && regimeStrategy is null)
        {
            throw new ArgumentException("A regime strategy is required when UseRegimeFilter is true.", nameof(regimeStrategy));
        }

        var benchmark = _data.Benchmark
            ?? throw new InvalidOperationException($"No bars are stored for the benchmark {_data.BenchmarkSymbol}.");
        var weeks = GroupIntoWeeks(benchmark.Bars
            .Select(bar => bar.Date)
            .Where(date => (startDate is null || date >= startDate) && (endDate is null || date <= endDate)));
        var warnings = new List<string>
        {
            "Survivorship bias: the universe is today's symbol list, so companies that were delisted or acquired during the test period are missing. Real results would be worse."
        };
        if (weeks.Count < 2)
        {
            warnings.Add("Not enough trading weeks in the requested period to run a backtest.");
            return new BacktestReport
            {
                Strategy = strategy.Name,
                BenchmarkSymbol = _data.BenchmarkSymbol,
                InitialCapital = options.InitialCapital,
                FinalEquity = options.InitialCapital,
                Weeks = weeks.Count,
                Warnings = warnings
            };
        }

        var slippage = options.SlippageBps / 10_000m;
        var cash = options.InitialCapital;
        var holdings = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);
        var trades = new List<BacktestTrade>();
        var orders = 0;
        var weeksBlocked = 0;
        var equityCurve = new List<EquityPoint>
        {
            new(weeks[0][^1], options.InitialCapital, options.InitialCapital)
        };
        decimal? benchmarkShares = null;
        var benchmarkCash = options.InitialCapital;

        for (var weekIndex = 0; weekIndex < weeks.Count - 1; weekIndex++)
        {
            var signalDate = weeks[weekIndex][^1];
            var fillDate = weeks[weekIndex + 1][0];
            var valuationDate = weeks[weekIndex + 1][^1];

            // Decide using data up to the signal date only.
            var targets = Rank(strategy, signalDate)
                .Where(item => item.Score > options.MinimumScore)
                .Take(options.TopN)
                .Select(item => item.Symbol)
                .ToList();
            var buysAllowed = true;
            if (options.UseRegimeFilter)
            {
                var regimeScore = regimeStrategy!.Score(
                    _data.BenchmarkSymbol,
                    signalDate,
                    _data.View(_data.BenchmarkSymbol, signalDate));
                buysAllowed = regimeScore is not null && regimeScore.Value >= options.RegimeMinimumScore;
                if (!buysAllowed)
                {
                    weeksBlocked++;
                }
            }

            // Sell what dropped out, at the next session's open.
            foreach (var symbol in holdings.Keys.Where(symbol => !targets.Contains(symbol, StringComparer.OrdinalIgnoreCase)).ToArray())
            {
                var bar = _data.Get(symbol)?.BarOn(fillDate);
                if (bar is null || bar.Open <= 0m)
                {
                    continue; // No session for this symbol that day; try again next week.
                }

                var holding = holdings[symbol];
                var exitPrice = bar.Open * (1m - slippage);
                var proceeds = (holding.Quantity * exitPrice) - options.CommissionPerOrder;
                cash += proceeds;
                orders++;
                var profitLoss = proceeds - holding.Cost;
                trades.Add(new BacktestTrade(
                    symbol,
                    holding.EntryDate,
                    Round(holding.EntryPrice),
                    fillDate,
                    Round(exitPrice),
                    holding.Quantity,
                    Round(profitLoss),
                    Round(holding.Cost == 0m ? 0m : (profitLoss / holding.Cost) * 100m)));
                holdings.Remove(symbol);
            }

            if (buysAllowed)
            {
                var equityAtOpen = cash + holdings.Sum(item => item.Value.Quantity * PriceAt(item.Key, fillDate, useOpen: true));
                var slotValue = equityAtOpen / options.TopN;
                foreach (var symbol in targets)
                {
                    if (holdings.Count >= options.TopN)
                    {
                        break;
                    }

                    if (holdings.ContainsKey(symbol))
                    {
                        continue;
                    }

                    var bar = _data.Get(symbol)?.BarOn(fillDate);
                    if (bar is null || bar.Open <= 0m)
                    {
                        continue;
                    }

                    var budget = Math.Min(slotValue, cash) - options.CommissionPerOrder;
                    if (budget <= 0m)
                    {
                        continue;
                    }

                    var entryPrice = bar.Open * (1m + slippage);
                    var quantity = Math.Floor((budget / entryPrice) * 1_000_000m) / 1_000_000m;
                    if (quantity <= 0m)
                    {
                        continue;
                    }

                    var cost = (quantity * entryPrice) + options.CommissionPerOrder;
                    cash -= cost;
                    orders++;
                    holdings[symbol] = new Holding(fillDate, entryPrice, quantity, cost);
                }
            }

            if (benchmarkShares is null)
            {
                var benchmarkBar = benchmark.BarOn(fillDate);
                if (benchmarkBar is not null && benchmarkBar.Open > 0m)
                {
                    var price = benchmarkBar.Open * (1m + slippage);
                    benchmarkShares = (benchmarkCash - options.CommissionPerOrder) / price;
                    benchmarkCash = 0m;
                }
            }

            var equity = cash + holdings.Sum(item => item.Value.Quantity * PriceAt(item.Key, valuationDate, useOpen: false));
            var benchmarkEquity = benchmarkCash
                + ((benchmarkShares ?? 0m) * PriceAt(_data.BenchmarkSymbol, valuationDate, useOpen: false));
            equityCurve.Add(new EquityPoint(valuationDate, Round(equity), Round(benchmarkEquity)));
        }

        var lastDate = equityCurve[^1].Date;
        var openPositions = holdings
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item =>
            {
                var lastPrice = PriceAt(item.Key, lastDate, useOpen: false);
                return new OpenBacktestPosition(
                    item.Key,
                    item.Value.EntryDate,
                    Round(item.Value.EntryPrice),
                    item.Value.Quantity,
                    Round(lastPrice),
                    Round((item.Value.Quantity * lastPrice) - item.Value.Cost));
            })
            .ToArray();

        var finalEquity = equityCurve[^1].Equity;
        var totalReturn = PercentChange(options.InitialCapital, finalEquity);
        var benchmarkReturn = PercentChange(options.InitialCapital, equityCurve[^1].BenchmarkEquity);
        var days = lastDate.DayNumber - equityCurve[0].Date.DayNumber;
        var annualised = Annualise(options.InitialCapital, finalEquity, days);
        var maxDrawdown = MaxDrawdownPercent(equityCurve.Select(point => point.Equity));
        decimal? winRate = trades.Count == 0
            ? null
            : Round((decimal)trades.Count(trade => trade.ProfitLoss > 0m) / trades.Count * 100m);

        AddQualityWarnings(warnings, weeks.Count, trades.Count, winRate, annualised, maxDrawdown, days);

        return new BacktestReport
        {
            Strategy = strategy.Name,
            BenchmarkSymbol = _data.BenchmarkSymbol,
            StartDate = equityCurve[0].Date,
            EndDate = lastDate,
            Weeks = weeks.Count,
            InitialCapital = options.InitialCapital,
            FinalEquity = finalEquity,
            TotalReturnPercent = Round(totalReturn),
            AnnualisedReturnPercent = Round(annualised),
            MaxDrawdownPercent = Round(maxDrawdown),
            WinRatePercent = winRate,
            ClosedTrades = trades.Count,
            Orders = orders,
            BenchmarkTotalReturnPercent = Round(benchmarkReturn),
            BenchmarkMaxDrawdownPercent = Round(MaxDrawdownPercent(equityCurve.Select(point => point.BenchmarkEquity))),
            ExcessReturnPercent = Round(totalReturn - benchmarkReturn),
            WeeksBlockedByRegimeFilter = weeksBlocked,
            Warnings = warnings,
            EquityCurve = equityCurve,
            Trades = trades,
            OpenPositions = openPositions
        };
    }

    public static IReadOnlyList<IReadOnlyList<DateOnly>> GroupIntoWeeks(IEnumerable<DateOnly> tradingDays)
    {
        ArgumentNullException.ThrowIfNull(tradingDays);
        return tradingDays
            .Distinct()
            .OrderBy(date => date)
            .GroupBy(date =>
            {
                var dateTime = date.ToDateTime(TimeOnly.MinValue);
                return (ISOWeek.GetYear(dateTime), ISOWeek.GetWeekOfYear(dateTime));
            })
            .Select(group => (IReadOnlyList<DateOnly>)group.ToArray())
            .ToArray();
    }

    public static decimal MaxDrawdownPercent(IEnumerable<decimal> equity)
    {
        var peak = 0m;
        var maxDrawdown = 0m;
        foreach (var value in equity)
        {
            peak = Math.Max(peak, value);
            if (peak > 0m)
            {
                maxDrawdown = Math.Max(maxDrawdown, ((peak - value) / peak) * 100m);
            }
        }

        return maxDrawdown;
    }

    private static void AddQualityWarnings(
        List<string> warnings,
        int weeks,
        int closedTrades,
        decimal? winRate,
        decimal annualisedReturn,
        decimal maxDrawdown,
        int days)
    {
        if (weeks < 104)
        {
            warnings.Add($"Short test period ({weeks} weeks). Results over less than two years say little about how a strategy behaves across market conditions.");
        }

        if (closedTrades < 30)
        {
            warnings.Add($"Only {closedTrades} closed trades. Too few to tell skill from luck.");
        }

        if (winRate is > 70m && closedTrades >= 30)
        {
            warnings.Add($"Win rate of {winRate:F1}% is unusually high. Check for overfitting or look-ahead bias before trusting it.");
        }

        if (annualisedReturn > 50m)
        {
            warnings.Add($"Annualised return of {annualisedReturn:F1}% looks too good. Check for overfitting or look-ahead bias before trusting it.");
        }

        if (days >= 365 && closedTrades >= 30 && maxDrawdown < 5m)
        {
            warnings.Add($"Maximum drawdown of only {maxDrawdown:F1}% over more than a year looks too good. Check for overfitting or look-ahead bias.");
        }
    }

    private bool IsEligible(StrategyData view, DateOnly asOfDate)
    {
        var bars = view.Bars;
        if (bars.Count < _universe.LiquidityLookbackDays || bars[^1].Date != asOfDate)
        {
            return false;
        }

        if (bars[^1].Close < _universe.MinimumPrice)
        {
            return false;
        }

        var dollarVolume = 0m;
        for (var index = bars.Count - _universe.LiquidityLookbackDays; index < bars.Count; index++)
        {
            dollarVolume += bars[index].Close * bars[index].Volume;
        }

        return dollarVolume / _universe.LiquidityLookbackDays >= _universe.MinimumAverageDollarVolume;
    }

    private decimal PriceAt(string symbol, DateOnly date, bool useOpen)
    {
        var series = _data.Get(symbol);
        var bar = series?.BarOn(date);
        if (bar is not null)
        {
            return useOpen ? bar.Open : bar.Close;
        }

        // No session that day: carry the last known close.
        return series?.LastOnOrBefore(date)?.Close ?? 0m;
    }

    private static decimal PercentChange(decimal from, decimal to)
    {
        return from == 0m ? 0m : ((to - from) / from) * 100m;
    }

    private static decimal Annualise(decimal from, decimal to, int days)
    {
        if (days <= 0 || from <= 0m || to <= 0m)
        {
            return 0m;
        }

        var annualised = (Math.Pow((double)(to / from), 365d / days) - 1d) * 100d;
        return double.IsFinite(annualised) && Math.Abs(annualised) < 1e9 ? (decimal)annualised : 0m;
    }

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed record Holding(DateOnly EntryDate, decimal EntryPrice, decimal Quantity, decimal Cost);
}
