using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace AutoTraderV4.Services;

public sealed class StocktwitsMcpSentimentProvider : IStocktwitsSentimentProvider, IDisposable
{
    private const string SentimentToolName = "get_symbol_sentiment";
    private const string StockStatsToolName = "get_stock_price";
    private const int MaximumCachedSymbols = 512;
    private const int SymbolLockCount = 64;
    private static readonly TimeSpan FailedRequestBackoff = TimeSpan.FromMinutes(5);

    private readonly IStocktwitsMcpToolClient _client;
    private readonly StocktwitsMcpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaximumCachedSymbols });
    private readonly SemaphoreSlim[] _symbolLocks = Enumerable.Range(0, SymbolLockCount)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public StocktwitsMcpSentimentProvider(
        IStocktwitsMcpToolClient client,
        StocktwitsMcpOptions options,
        TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SentimentDataContract?> GetAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        _options.Validate();

        var portfolioTicker = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        var stocktwitsSymbol = NormalizeStocktwitsSymbol(portfolioTicker);
        if (string.IsNullOrWhiteSpace(stocktwitsSymbol))
        {
            return null;
        }

        var symbolLockIndex = StringComparer.OrdinalIgnoreCase.GetHashCode(portfolioTicker) & int.MaxValue;
        var symbolLock = _symbolLocks[symbolLockIndex % _symbolLocks.Length];
        await symbolLock.WaitAsync(cancellationToken);
        try
        {
            if (!_options.Enabled)
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            if (_cache.TryGetValue(portfolioTicker, out CachedProviderResult? cached))
            {
                if (cached?.Failure is not null)
                {
                    throw new InvalidOperationException(
                        $"Stocktwits refresh for {portfolioTicker} is backed off after a recent failure: {cached.Failure}");
                }

                if (cached?.Data is { } cachedData)
                {
                    var cacheAge = now - cachedData.TimestampUtc;
                    if (cacheAge >= TimeSpan.Zero && cacheAge < _options.RefreshInterval)
                    {
                        return cachedData;
                    }
                }
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.RequestTimeout);

                var sentimentStats = await _client.CallAsync(SentimentToolName, stocktwitsSymbol, timeout.Token);
                EnsureNoToolError(sentimentStats, SentimentToolName);

                var stockStats = await _client.CallAsync(StockStatsToolName, stocktwitsSymbol, timeout.Token);
                EnsureNoToolError(stockStats, StockStatsToolName);

                var messagesAnalyzed = ReadNonNegativeInteger(sentimentStats, "messages_analyzed");
                var bullish = ReadNonNegativeInteger(sentimentStats, "bullish");
                var bearish = ReadNonNegativeInteger(sentimentStats, "bearish");
                var neutral = ReadNonNegativeInteger(sentimentStats, "neutral");
                if (bullish + bearish + neutral != messagesAnalyzed)
                {
                    throw new InvalidDataException("Stocktwits sentiment counts do not add up to messages_analyzed.");
                }

                var providerScore = ReadDecimal(sentimentStats, "sentiment_score");
                if (providerScore is < 0m or > 100m)
                {
                    throw new InvalidDataException("Stocktwits sentiment_score must be between 0 and 100.");
                }

                var sentimentLabel = ReadString(sentimentStats, "sentiment_label");
                var score = (providerScore - 50m) * 2m;
                var confidence = Math.Min(100m, messagesAnalyzed * 5m);
                var fetchedAtUtc = _timeProvider.GetUtcNow();
                var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source_type"] = "stocktwits-mcp",
                    ["messages_analyzed"] = messagesAnalyzed,
                    ["bullish"] = bullish,
                    ["bearish"] = bearish,
                    ["neutral"] = neutral,
                    ["sentiment_score"] = providerScore,
                    ["sentiment_label"] = sentimentLabel,
                    ["confidence_method"] = "min(messages_analyzed * 5, 100)",
                    ["stock_stats"] = stockStats,
                    ["fetched_at_utc"] = fetchedAtUtc
                };

                var result = new SentimentDataContract
                {
                    Ticker = portfolioTicker,
                    Symbol = stocktwitsSymbol,
                    Score = score,
                    Magnitude = Math.Abs(score),
                    Confidence = confidence,
                    TimestampUtc = fetchedAtUtc,
                    Source = "stocktwits",
                    ProviderName = "stocktwits-mcp",
                    FreshnessWindow = _options.RefreshInterval,
                    Metadata = metadata
                };
                result.Validate();
                _cache.Set(
                    portfolioTicker,
                    new CachedProviderResult(result, null),
                    CreateCacheOptions(_options.RefreshInterval));
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _cache.Set(
                    portfolioTicker,
                    new CachedProviderResult(null, exception.Message),
                    CreateCacheOptions(FailedRequestBackoff));
                throw;
            }
        }
        finally
        {
            symbolLock.Release();
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
        foreach (var symbolLock in _symbolLocks)
        {
            symbolLock.Dispose();
        }
    }

    private static MemoryCacheEntryOptions CreateCacheOptions(TimeSpan expiration)
    {
        return new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = expiration
        };
    }

    private sealed record CachedProviderResult(SentimentDataContract? Data, string? Failure);

    private static string NormalizeStocktwitsSymbol(string symbol)
    {
        var suffixStart = symbol.IndexOf('_');
        var normalized = suffixStart > 0 ? symbol[..suffixStart] : symbol;
        return normalized.TrimStart('$', '^');
    }

    private static void EnsureNoToolError(JsonElement result, string toolName)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Stocktwits MCP tool '{toolName}' did not return a JSON object.");
        }

        if (result.TryGetProperty("error", out var error))
        {
            var message = error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : error.GetRawText();
            throw new InvalidDataException($"Stocktwits MCP tool '{toolName}' failed: {message}");
        }
    }

    private static int ReadNonNegativeInteger(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value)
            || value < 0)
        {
            throw new InvalidDataException($"Stocktwits MCP response is missing a valid '{propertyName}' value.");
        }

        return value;
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetDecimal(out var value))
        {
            throw new InvalidDataException($"Stocktwits MCP response is missing a valid '{propertyName}' value.");
        }

        return value;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Stocktwits MCP response is missing a valid '{propertyName}' value.");
        }

        return property.GetString()!;
    }
}
