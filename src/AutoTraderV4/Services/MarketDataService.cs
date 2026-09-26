using System.Text.Json;

namespace AutoTraderV4.Services;

public enum DataFreshnessStatus
{
    Fresh,
    Stale,
    Missing,
    Invalid
}

public static class DataFreshnessRules
{
    public const int DefaultMarketFreshnessSeconds = 300;
    public const int DefaultSentimentFreshnessSeconds = 900;

    public static DataFreshnessStatus Evaluate(DateTimeOffset? observedAtUtc, TimeSpan? freshnessWindow, DateTimeOffset? referenceTimeUtc = null)
    {
        if (!observedAtUtc.HasValue)
        {
            return DataFreshnessStatus.Missing;
        }

        var reference = referenceTimeUtc ?? DateTimeOffset.UtcNow;
        var window = freshnessWindow ?? TimeSpan.FromSeconds(DefaultMarketFreshnessSeconds);
        var age = reference - observedAtUtc.Value;

        if (age < TimeSpan.Zero)
        {
            return DataFreshnessStatus.Invalid;
        }

        return age <= window ? DataFreshnessStatus.Fresh : DataFreshnessStatus.Stale;
    }
}

public sealed class SourceMetadata
{
    public string Source { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public DateTimeOffset FetchedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReceivedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan FreshnessWindow { get; init; } = TimeSpan.FromSeconds(DataFreshnessRules.DefaultMarketFreshnessSeconds);
    public string MetadataJson { get; init; } = "{}";
}

public sealed record MarketDataContract
{
    public string Symbol { get; init; } = string.Empty;
    public string Ticker { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? Open { get; init; }
    public decimal? High { get; init; }
    public decimal? Low { get; init; }
    public decimal? Close { get; init; }
    public decimal? Volume { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "market-data";
    public string ProviderName { get; init; } = "unknown";
    public TimeSpan FreshnessWindow { get; init; } = TimeSpan.FromSeconds(DataFreshnessRules.DefaultMarketFreshnessSeconds);
    public Dictionary<string, object> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? Ticker : Symbol;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Symbol) && string.IsNullOrWhiteSpace(Ticker))
        {
            throw new ArgumentException("Symbol is required.", nameof(Symbol));
        }

        if (Price < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(Price), "Price cannot be negative.");
        }

        if (Volume is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(Volume), "Volume cannot be negative.");
        }

        if (TimestampUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(TimestampUtc), "TimestampUtc is required.");
        }
    }

    public MarketSnapshot ToSnapshot(DateTimeOffset? referenceTimeUtc = null)
    {
        var reference = referenceTimeUtc ?? DateTimeOffset.UtcNow;
        var freshness = DataFreshnessRules.Evaluate(TimestampUtc, FreshnessWindow, reference);

        return new MarketSnapshot
        {
            Ticker = NormalizedSymbol.Trim(),
            Symbol = NormalizedSymbol.Trim(),
            Price = Price,
            Open = Open ?? Price,
            High = High ?? Price,
            Low = Low ?? Price,
            Close = Close ?? Price,
            Volume = Volume ?? 0m,
            Source = Source,
            ProviderName = ProviderName,
            FetchedAtUtc = TimestampUtc,
            ReceivedAtUtc = reference,
            DataAgeSeconds = (decimal)Math.Max(0, (reference - TimestampUtc).TotalSeconds),
            FreshnessStatus = freshness.ToString(),
            MetadataJson = JsonSerializer.Serialize(Metadata),
            LastUpdatedUtc = TimestampUtc,
        };
    }
}

public sealed class MarketDataIngestionResult
{
    public bool Success { get; init; }
    public DataFreshnessStatus FreshnessStatus { get; init; }
    public string ActiveProvider { get; init; } = string.Empty;
    public MarketDataContract? Data { get; init; }
    public MarketSnapshot? PersistedSnapshot { get; init; }
    public List<string> Failures { get; init; } = [];
}

public interface IMarketDataProvider
{
    Task<MarketDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class DemoMarketDataProvider : IMarketDataProvider
{
    public Task<MarketDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = (symbol ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Task.FromResult<MarketDataContract?>(null);
        }

        var seed = Math.Abs((normalized.Length * 97) + normalized.GetHashCode());
        var price = 90m + (seed % 250m);
        var now = DateTimeOffset.UtcNow;

        return Task.FromResult<MarketDataContract?>(new MarketDataContract
        {
            Ticker = normalized,
            Symbol = normalized,
            Price = price,
            Open = price * 0.98m,
            High = price * 1.03m,
            Low = price * 0.95m,
            Close = price,
            Volume = 1500000m + (seed % 500000m),
            TimestampUtc = now,
            Source = "demo",
            ProviderName = "demo-market-data",
            FreshnessWindow = TimeSpan.FromMinutes(5),
            Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["source_type"] = "demo",
                ["currency"] = "USD"
            }
        });
    }
}

public sealed class FallbackMarketDataProvider : IMarketDataProvider
{
    public Task<MarketDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = (symbol ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Task.FromResult<MarketDataContract?>(null);
        }

        var seed = Math.Abs((normalized.Length * 31) + normalized.GetHashCode() + 17);
        var price = 85m + (seed % 200m);
        var now = DateTimeOffset.UtcNow;

        return Task.FromResult<MarketDataContract?>(new MarketDataContract
        {
            Ticker = normalized,
            Symbol = normalized,
            Price = price,
            Open = price * 0.985m,
            High = price * 1.025m,
            Low = price * 0.97m,
            Close = price,
            Volume = 1200000m + (seed % 400000m),
            TimestampUtc = now,
            Source = "fallback",
            ProviderName = "fallback-market-data",
            FreshnessWindow = TimeSpan.FromMinutes(5),
            Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["source_type"] = "fallback",
                ["currency"] = "USD",
                ["fallback_enabled"] = true
            }
        });
    }
}

public sealed class MarketDataService
{
    private readonly ApplicationDbContext _context;
    private readonly IReadOnlyList<IMarketDataProvider> _providers;
    private readonly TimeProvider _timeProvider;

    public MarketDataService(
        ApplicationDbContext context,
        IEnumerable<IMarketDataProvider>? providers = null,
        TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _providers = (providers ?? new[] { new DemoMarketDataProvider() }).ToList();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<MarketDataIngestionResult> IngestAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        var normalizedSymbol = symbol.Trim();
        var providerFailures = new List<string>();
        MarketDataContract? bestStaleCandidate = null;

        foreach (var provider in _providers)
        {
            try
            {
                var candidate = await provider.GetAsync(normalizedSymbol, cancellationToken);
                if (candidate is null)
                {
                    providerFailures.Add($"{provider.GetType().Name}: provider returned no data");
                    continue;
                }

                candidate.Validate();

                var reference = _timeProvider.GetUtcNow();
                var freshness = DataFreshnessRules.Evaluate(candidate.TimestampUtc, candidate.FreshnessWindow, reference);
                if (freshness == DataFreshnessStatus.Fresh)
                {
                    var snapshot = candidate.ToSnapshot(reference);
                    _context.MarketSnapshots.Add(snapshot);
                    await _context.SaveChangesAsync(cancellationToken);

                    return new MarketDataIngestionResult
                    {
                        Success = true,
                        FreshnessStatus = DataFreshnessStatus.Fresh,
                        ActiveProvider = candidate.ProviderName,
                        Data = candidate,
                        PersistedSnapshot = snapshot,
                        Failures = providerFailures
                    };
                }

                providerFailures.Add($"{candidate.ProviderName}: stale data ({candidate.TimestampUtc:O})");
                bestStaleCandidate ??= candidate;
            }
            catch (Exception ex)
            {
                providerFailures.Add($"{provider.GetType().Name}: {ex.Message}");
            }
        }

        if (bestStaleCandidate is not null)
        {
            var reference = _timeProvider.GetUtcNow();
            var staleSnapshot = bestStaleCandidate.ToSnapshot(reference);
            _context.MarketSnapshots.Add(staleSnapshot);
            await _context.SaveChangesAsync(cancellationToken);

            return new MarketDataIngestionResult
            {
                Success = false,
                FreshnessStatus = DataFreshnessStatus.Stale,
                ActiveProvider = bestStaleCandidate.ProviderName,
                Data = bestStaleCandidate,
                PersistedSnapshot = staleSnapshot,
                Failures = providerFailures
            };
        }

        return new MarketDataIngestionResult
        {
            Success = false,
            FreshnessStatus = DataFreshnessStatus.Missing,
            ActiveProvider = "none",
            Failures = providerFailures
        };
    }
}
