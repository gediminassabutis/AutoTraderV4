using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoTraderV4.Weekly;

public sealed class UniverseOptions
{
    /// <summary>Candidate symbols (plain US tickers such as "AAPL").</summary>
    public List<string> Symbols { get; set; } = [];
    public decimal MinimumPrice { get; set; } = 5m;

    /// <summary>
    /// Minimum average daily traded value over <see cref="LiquidityLookbackDays"/>, measured on the
    /// configured bar feed. The IEX feed carries only a fraction of consolidated volume.
    /// </summary>
    public decimal MinimumAverageDollarVolume { get; set; } = 1_000_000m;
    public int LiquidityLookbackDays { get; set; } = 20;
}

public sealed class BacktestOptions
{
    /// <summary>How many top-ranked stocks to hold.</summary>
    public int TopN { get; set; } = 10;

    /// <summary>A stock is only bought or kept while its index is above this.</summary>
    public decimal MinimumScore { get; set; } = 50m;
    public decimal InitialCapital { get; set; } = 10_000m;
    public decimal CommissionPerOrder { get; set; }

    /// <summary>Slippage per fill in basis points: buys fill above the open, sells below it.</summary>
    public decimal SlippageBps { get; set; } = 5m;

    /// <summary>When true, no new positions are opened while the market-regime index is below <see cref="RegimeMinimumScore"/>.</summary>
    public bool UseRegimeFilter { get; set; }
    public decimal RegimeMinimumScore { get; set; } = 50m;

    public void Validate()
    {
        if (TopN <= 0)
        {
            throw new ArgumentException("Backtest: TopN must be positive.");
        }

        if (InitialCapital <= 0m)
        {
            throw new ArgumentException("Backtest: InitialCapital must be positive.");
        }

        if (CommissionPerOrder < 0m || SlippageBps < 0m || SlippageBps >= 10_000m)
        {
            throw new ArgumentException("Backtest: CommissionPerOrder and SlippageBps cannot be negative.");
        }

        if (MinimumScore is < 0m or > 100m || RegimeMinimumScore is < 0m or > 100m)
        {
            throw new ArgumentException("Backtest: score thresholds must be between 0 and 100.");
        }
    }

    public BacktestOptions Clone() => (BacktestOptions)MemberwiseClone();
}

public sealed class WeeklyStrategyOptions
{
    public TrendFollowingOptions TrendFollowing { get; set; } = new();
    public ShortTermReversalOptions ShortTermReversal { get; set; } = new();
    public MarketRegimeOptions MarketRegime { get; set; } = new();
}

public sealed class WeeklyOptions
{
    public const string SectionName = "Weekly";

    /// <summary>SQLite file holding downloaded daily bars. Relative paths resolve against the content root.</summary>
    public string BarStorePath { get; set; } = "data/weekly-bars.db";
    public string BenchmarkSymbol { get; set; } = "SPY";
    public int HistoryYears { get; set; } = 5;

    /// <summary>Symbols per download request.</summary>
    public int SyncBatchSize { get; set; } = 50;
    public UniverseOptions Universe { get; set; } = new();
    public BacktestOptions Backtest { get; set; } = new();
    public WeeklyStrategyOptions Strategies { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BarStorePath))
        {
            throw new ArgumentException("Weekly:BarStorePath is required.");
        }

        if (string.IsNullOrWhiteSpace(BenchmarkSymbol))
        {
            throw new ArgumentException("Weekly:BenchmarkSymbol is required.");
        }

        if (HistoryYears is <= 0 or > 30)
        {
            throw new ArgumentException("Weekly:HistoryYears must be between 1 and 30.");
        }

        if (SyncBatchSize is <= 0 or > 200)
        {
            throw new ArgumentException("Weekly:SyncBatchSize must be between 1 and 200.");
        }

        if (Universe.LiquidityLookbackDays <= 0 || Universe.MinimumPrice < 0m || Universe.MinimumAverageDollarVolume < 0m)
        {
            throw new ArgumentException("Weekly:Universe filters cannot be negative, and LiquidityLookbackDays must be positive.");
        }

        Backtest.Validate();
        Strategies.TrendFollowing.Validate();
        Strategies.ShortTermReversal.Validate();
        Strategies.MarketRegime.Validate();
    }
}

/// <summary>Builds strategies from configured parameters, optionally overridden per request.</summary>
public sealed class StrategyFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WeeklyStrategyOptions _defaults;

    public StrategyFactory(WeeklyOptions options)
    {
        _defaults = (options ?? throw new ArgumentNullException(nameof(options))).Strategies;
    }

    public static IReadOnlyList<string> Names { get; } =
    [
        TrendFollowingStrategy.StrategyName,
        ShortTermReversalStrategy.StrategyName,
        MarketRegimeStrategy.StrategyName
    ];

    public IReadOnlyList<IStrategy> CreateAll() => Names.Select(name => Create(name)).ToArray();

    /// <summary>
    /// Creates a strategy by name. <paramref name="parameters"/> is an optional JSON object whose
    /// properties replace the configured defaults. Throws <see cref="ArgumentException"/> for an
    /// unknown name or invalid parameters.
    /// </summary>
    public IStrategy Create(string name, JsonElement? parameters = null)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (string.Equals(normalized, TrendFollowingStrategy.StrategyName, StringComparison.OrdinalIgnoreCase))
        {
            return new TrendFollowingStrategy(Merge(_defaults.TrendFollowing, parameters));
        }

        if (string.Equals(normalized, ShortTermReversalStrategy.StrategyName, StringComparison.OrdinalIgnoreCase))
        {
            return new ShortTermReversalStrategy(Merge(_defaults.ShortTermReversal, parameters));
        }

        if (string.Equals(normalized, MarketRegimeStrategy.StrategyName, StringComparison.OrdinalIgnoreCase))
        {
            return new MarketRegimeStrategy(Merge(_defaults.MarketRegime, parameters));
        }

        throw new ArgumentException($"Unknown strategy \"{name}\". Available: {string.Join(", ", Names)}.");
    }

    private static T Merge<T>(T defaults, JsonElement? overrides)
        where T : class
    {
        var merged = JsonSerializer.SerializeToNode(defaults, JsonOptions)!.AsObject();
        if (overrides is { ValueKind: JsonValueKind.Object } element)
        {
            foreach (var property in element.EnumerateObject())
            {
                var existing = merged.FirstOrDefault(item =>
                    string.Equals(item.Key, property.Name, StringComparison.OrdinalIgnoreCase));
                if (existing.Key is null)
                {
                    throw new ArgumentException($"Unknown parameter \"{property.Name}\" for {typeof(T).Name}.");
                }

                merged[existing.Key] = JsonNode.Parse(property.Value.GetRawText());
            }
        }
        else if (overrides is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            throw new ArgumentException("Strategy parameters must be a JSON object.");
        }

        try
        {
            return merged.Deserialize<T>(JsonOptions)!;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"Invalid parameters for {typeof(T).Name}: {exception.Message}");
        }
    }
}
