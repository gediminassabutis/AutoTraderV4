using System.Text.Json;

namespace AutoTraderV4.Weekly;

public sealed class WeeklyBacktestRequest
{
    public string Strategy { get; set; } = string.Empty;

    /// <summary>Optional strategy parameters that replace the configured defaults for this run.</summary>
    public JsonElement? Parameters { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? TopN { get; set; }
    public decimal? MinimumScore { get; set; }
    public decimal? InitialCapital { get; set; }
    public decimal? CommissionPerOrder { get; set; }
    public decimal? SlippageBps { get; set; }
    public bool? UseRegimeFilter { get; set; }
}

public sealed class WeeklyWalkForwardRequest
{
    public string Strategy { get; set; } = string.Empty;

    /// <summary>Parameter sets to choose between on each training period. Empty uses the configured defaults only.</summary>
    public List<JsonElement> Variants { get; set; } = [];
    public int TrainWeeks { get; set; } = 104;
    public int TestWeeks { get; set; } = 26;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool? UseRegimeFilter { get; set; }
}

public sealed record SymbolScores(string Symbol, IReadOnlyDictionary<string, decimal?> Scores);

public sealed class WeeklyScoreBoard
{
    public DateOnly? AsOfDate { get; init; }
    public IReadOnlyList<string> Strategies { get; init; } = [];
    public int SymbolsInStore { get; init; }
    public IReadOnlyList<SymbolScores> Symbols { get; init; } = [];
}

/// <summary>Runs scoring, backtests and walk-forward tests against the local bar store.</summary>
public sealed class WeeklyService
{
    private readonly BarStore _store;
    private readonly WeeklyOptions _options;
    private readonly StrategyFactory _strategyFactory;

    public WeeklyService(BarStore store, WeeklyOptions options, StrategyFactory strategyFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    }

    /// <summary>
    /// Every strategy's index for each symbol that passes the universe filters on the date.
    /// Defaults to the benchmark's most recent stored session.
    /// </summary>
    public async Task<WeeklyScoreBoard> GetScoresAsync(DateOnly? asOfDate, CancellationToken cancellationToken = default)
    {
        var data = await _store.LoadAsync(_options.BenchmarkSymbol, cancellationToken);
        var strategies = _strategyFactory.CreateAll();
        var date = asOfDate is null
            ? data.Benchmark?.Bars.LastOrDefault()?.Date
            : data.Benchmark?.LastOnOrBefore(asOfDate.Value)?.Date;
        if (date is null)
        {
            return new WeeklyScoreBoard
            {
                Strategies = strategies.Select(strategy => strategy.Name).ToArray(),
                SymbolsInStore = data.Symbols.Count
            };
        }

        var backtester = new Backtester(data, _options.Universe);
        var scores = new SortedDictionary<string, Dictionary<string, decimal?>>(StringComparer.Ordinal);
        foreach (var strategy in strategies)
        {
            foreach (var ranked in backtester.Rank(strategy, date.Value))
            {
                if (!scores.TryGetValue(ranked.Symbol, out var symbolScores))
                {
                    symbolScores = strategies.ToDictionary(item => item.Name, _ => (decimal?)null);
                    scores[ranked.Symbol] = symbolScores;
                }

                symbolScores[strategy.Name] = Math.Round(ranked.Score, 2, MidpointRounding.AwayFromZero);
            }
        }

        return new WeeklyScoreBoard
        {
            AsOfDate = date,
            Strategies = strategies.Select(strategy => strategy.Name).ToArray(),
            SymbolsInStore = data.Symbols.Count,
            Symbols = scores.Select(item => new SymbolScores(item.Key, item.Value)).ToArray()
        };
    }

    /// <summary>Throws <see cref="ArgumentException"/> for an unknown strategy or invalid parameters.</summary>
    public async Task<BacktestReport> RunBacktestAsync(WeeklyBacktestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var strategy = _strategyFactory.Create(request.Strategy, request.Parameters);
        var options = _options.Backtest.Clone();
        options.TopN = request.TopN ?? options.TopN;
        options.MinimumScore = request.MinimumScore ?? options.MinimumScore;
        options.InitialCapital = request.InitialCapital ?? options.InitialCapital;
        options.CommissionPerOrder = request.CommissionPerOrder ?? options.CommissionPerOrder;
        options.SlippageBps = request.SlippageBps ?? options.SlippageBps;
        options.UseRegimeFilter = request.UseRegimeFilter ?? options.UseRegimeFilter;
        options.Validate();
        ValidateDates(request.StartDate, request.EndDate);

        var data = await _store.LoadAsync(_options.BenchmarkSymbol, cancellationToken);
        return new Backtester(data, _options.Universe).Run(
            strategy,
            options,
            request.StartDate,
            request.EndDate,
            _strategyFactory.Create(MarketRegimeStrategy.StrategyName));
    }

    /// <summary>Throws <see cref="ArgumentException"/> for an unknown strategy or invalid parameters.</summary>
    public async Task<WalkForwardReport> RunWalkForwardAsync(
        WeeklyWalkForwardRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TrainWeeks < 2 || request.TestWeeks < 2)
        {
            throw new ArgumentException("TrainWeeks and TestWeeks must each be at least 2.");
        }

        ValidateDates(request.StartDate, request.EndDate);
        var variants = request.Variants.Count == 0
            ? [new StrategyVariant("configured defaults", _strategyFactory.Create(request.Strategy))]
            : request.Variants
                .Select(parameters => new StrategyVariant(
                    parameters.GetRawText(),
                    _strategyFactory.Create(request.Strategy, parameters)))
                .ToList();
        var options = _options.Backtest.Clone();
        options.UseRegimeFilter = request.UseRegimeFilter ?? options.UseRegimeFilter;

        var data = await _store.LoadAsync(_options.BenchmarkSymbol, cancellationToken);
        return new WalkForwardRunner(data, _options.Universe).Run(
            variants,
            options,
            request.TrainWeeks,
            request.TestWeeks,
            request.StartDate,
            request.EndDate,
            _strategyFactory.Create(MarketRegimeStrategy.StrategyName));
    }

    private static void ValidateDates(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate is not null && endDate is not null && startDate > endDate)
        {
            throw new ArgumentException("StartDate must be on or before EndDate.");
        }
    }
}
