using System.Text.Json;

namespace AutoTraderV4.Services;

public sealed record SentimentDataContract
{
    public string Symbol { get; init; } = string.Empty;
    public string Ticker { get; init; } = string.Empty;
    public decimal Score { get; init; }
    public decimal Magnitude { get; init; }
    public decimal Confidence { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "sentiment";
    public string ProviderName { get; init; } = "unknown";
    public TimeSpan FreshnessWindow { get; init; } = TimeSpan.FromSeconds(DataFreshnessRules.DefaultSentimentFreshnessSeconds);
    public Dictionary<string, object> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string NormalizedSymbol => string.IsNullOrWhiteSpace(Symbol) ? Ticker : Symbol;

    public bool MatchesRequestedSymbol(string requestedSymbol)
    {
        if (string.IsNullOrWhiteSpace(requestedSymbol))
        {
            return false;
        }

        return string.Equals(NormalizedSymbol.Trim(), requestedSymbol.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Symbol) && string.IsNullOrWhiteSpace(Ticker))
        {
            throw new ArgumentException("Symbol is required.", nameof(Symbol));
        }

        if (Score < -100m || Score > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(Score), "Score must be between -100 and 100.");
        }

        if (Magnitude < 0m || Magnitude > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(Magnitude), "Magnitude must be between 0 and 100.");
        }

        if (Confidence < 0m || Confidence > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(Confidence), "Confidence must be between 0 and 100.");
        }

        if (TimestampUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(TimestampUtc), "TimestampUtc is required.");
        }
    }

    public SentimentRecord ToRecord(DateTimeOffset? referenceTimeUtc = null)
    {
        var reference = referenceTimeUtc ?? DateTimeOffset.UtcNow;
        var freshness = DataFreshnessRules.Evaluate(TimestampUtc, FreshnessWindow, reference);

        return new SentimentRecord
        {
            Ticker = NormalizedSymbol.Trim(),
            Symbol = NormalizedSymbol.Trim(),
            Source = Source,
            ProviderName = ProviderName,
            FreshnessStatus = freshness.ToString(),
            Score = Score,
            Magnitude = Magnitude,
            Confidence = Confidence,
            MetadataJson = JsonSerializer.Serialize(Metadata),
            CreatedUtc = TimestampUtc,
            RecordedAtUtc = reference,
        };
    }
}

public sealed class SentimentIngestionResult
{
    public bool Success { get; init; }
    public DataFreshnessStatus FreshnessStatus { get; init; }
    public string ActiveProvider { get; init; } = string.Empty;
    public SentimentDataContract? Data { get; init; }
    public SentimentRecord? PersistedRecord { get; init; }
    public List<string> Failures { get; init; } = [];
}

public interface ISentimentProvider
{
    Task<SentimentDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default);
}

public sealed class DemoSentimentProvider : ISentimentProvider
{
    public Task<SentimentDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = (symbol ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Task.FromResult<SentimentDataContract?>(null);
        }

        var seed = Math.Abs(normalized.GetHashCode() % 100);
        var sentiment = 55m + (seed % 35m) - 18m;

        return Task.FromResult<SentimentDataContract?>(new SentimentDataContract
        {
            Ticker = normalized,
            Symbol = normalized,
            Score = sentiment,
            Magnitude = 65m,
            Confidence = 80m,
            TimestampUtc = DateTimeOffset.UtcNow,
            Source = "demo",
            ProviderName = "demo-sentiment",
            FreshnessWindow = TimeSpan.FromMinutes(15),
            Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["source_type"] = "demo",
                ["asset_class"] = "equity"
            }
        });
    }
}

public sealed class FallbackSentimentProvider : ISentimentProvider
{
    public Task<SentimentDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = (symbol ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Task.FromResult<SentimentDataContract?>(null);
        }

        var seed = Math.Abs((normalized.GetHashCode() * 7) % 100);
        var sentiment = 48m + (seed % 30m) - 15m;

        return Task.FromResult<SentimentDataContract?>(new SentimentDataContract
        {
            Ticker = normalized,
            Symbol = normalized,
            Score = sentiment,
            Magnitude = 72m,
            Confidence = 76m,
            TimestampUtc = DateTimeOffset.UtcNow,
            Source = "fallback",
            ProviderName = "fallback-sentiment",
            FreshnessWindow = TimeSpan.FromMinutes(15),
            Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["source_type"] = "fallback",
                ["asset_class"] = "equity",
                ["fallback_enabled"] = true
            }
        });
    }
}

public sealed class SentimentService
{
    private readonly ApplicationDbContext _context;
    private readonly IReadOnlyList<ISentimentProvider> _providers;
    private readonly TimeProvider _timeProvider;

    public SentimentService(
        ApplicationDbContext context,
        IEnumerable<ISentimentProvider>? providers = null,
        TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _providers = (providers ?? new[] { new DemoSentimentProvider() }).ToList();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SentimentIngestionResult> IngestAsync(string symbol, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        var normalizedSymbol = symbol.Trim();
        var providerFailures = new List<string>();
        SentimentDataContract? bestStaleCandidate = null;

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

                if (!candidate.MatchesRequestedSymbol(normalizedSymbol))
                {
                    throw new InvalidOperationException($"Provider returned data for symbol '{candidate.NormalizedSymbol}' instead of '{normalizedSymbol}'.");
                }

                var reference = _timeProvider.GetUtcNow();
                var freshness = DataFreshnessRules.Evaluate(candidate.TimestampUtc, candidate.FreshnessWindow, reference);
                if (freshness == DataFreshnessStatus.Fresh)
                {
                    var record = candidate.ToRecord(reference);
                    _context.SentimentRecords.Add(record);
                    await _context.SaveChangesAsync(cancellationToken);

                    return new SentimentIngestionResult
                    {
                        Success = true,
                        FreshnessStatus = DataFreshnessStatus.Fresh,
                        ActiveProvider = candidate.ProviderName,
                        Data = candidate,
                        PersistedRecord = record,
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
            var staleRecord = bestStaleCandidate.ToRecord(reference);
            _context.SentimentRecords.Add(staleRecord);
            await _context.SaveChangesAsync(cancellationToken);

            return new SentimentIngestionResult
            {
                Success = false,
                FreshnessStatus = DataFreshnessStatus.Stale,
                ActiveProvider = bestStaleCandidate.ProviderName,
                Data = bestStaleCandidate,
                PersistedRecord = staleRecord,
                Failures = providerFailures
            };
        }

        return new SentimentIngestionResult
        {
            Success = false,
            FreshnessStatus = DataFreshnessStatus.Missing,
            ActiveProvider = "none",
            Failures = providerFailures
        };
    }
}
