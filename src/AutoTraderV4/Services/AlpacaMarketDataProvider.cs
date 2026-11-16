using System.Globalization;
using System.Net;
using System.Text.Json;
using AutoTraderV4.Indicators;
using AutoTraderV4.Weekly;

namespace AutoTraderV4.Services;

public sealed class AlpacaOptions
{
    public const string SectionName = "Alpaca";
    public const string HttpClientName = "alpaca-market-data";

    /// <summary>Set to false to keep the provider off even when credentials are present.</summary>
    public bool Enabled { get; set; } = true;
    public string ApiKeyId { get; set; } = string.Empty;
    public string ApiSecretKey { get; set; } = string.Empty;
    public string DataBaseUrl { get; set; } = "https://data.alpaca.markets";

    /// <summary>"iex" is available on the free plan; "sip" (full consolidated tape) needs a paid subscription.</summary>
    public string Feed { get; set; } = "iex";

    /// <summary>Minimum gap between requests. The free plan allows 200 requests per minute.</summary>
    public int MinimumRequestIntervalMilliseconds { get; set; } = 350;

    /// <summary>A quote older than this is treated as stale, which blocks trading on it.</summary>
    public int FreshnessSeconds { get; set; } = DataFreshnessRules.DefaultMarketFreshnessSeconds;

    public bool HasCredentials => !string.IsNullOrWhiteSpace(ApiKeyId) && !string.IsNullOrWhiteSpace(ApiSecretKey);

    public bool IsActive => Enabled && HasCredentials;

    public void Validate()
    {
        if (!Uri.TryCreate(DataBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Alpaca:DataBaseUrl must be an absolute https URL.");
        }

        if (string.IsNullOrWhiteSpace(Feed))
        {
            throw new ArgumentException("Alpaca:Feed is required (for example \"iex\" or \"sip\").");
        }

        if (MinimumRequestIntervalMilliseconds < 0)
        {
            throw new ArgumentException("Alpaca:MinimumRequestIntervalMilliseconds cannot be negative.");
        }

        if (FreshnessSeconds <= 0)
        {
            throw new ArgumentException("Alpaca:FreshnessSeconds must be positive.");
        }
    }
}

public static class AlpacaSymbol
{
    private const string UsEquitySuffix = "_US_EQ";

    /// <summary>
    /// Maps a Trading 212 ticker ("AAPL_US_EQ", "BRK_B_US_EQ") or a plain US symbol ("AAPL", "BRK.B")
    /// to the Alpaca symbol. Returns null for instruments Alpaca does not cover, such as non-US listings.
    /// </summary>
    public static string? FromTicker(string? ticker)
    {
        var normalized = (ticker ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.EndsWith(UsEquitySuffix, StringComparison.Ordinal))
        {
            normalized = normalized[..^UsEquitySuffix.Length].Replace('_', '.');
        }
        else if (normalized.Contains('_'))
        {
            return null;
        }

        if (normalized.Length is 0 or > 10)
        {
            return null;
        }

        foreach (var character in normalized)
        {
            if (character is not ((>= 'A' and <= 'Z') or '.'))
            {
                return null;
            }
        }

        return normalized[0] == '.' || normalized[^1] == '.' ? null : normalized;
    }
}

/// <summary>
/// Live US-equity quotes and daily bar history from the Alpaca Market Data API.
/// </summary>
public sealed class AlpacaMarketDataProvider : IMarketDataProvider, IPriceHistoryProvider, IDailyBarSource
{
    public const string Name = "alpaca";

    private readonly Func<HttpClient> _httpClientFactory;
    private readonly AlpacaOptions _options;
    private readonly TimeProvider _timeProvider;

    public AlpacaMarketDataProvider(Func<HttpClient> httpClientFactory, AlpacaOptions options, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options.Validate();
        if (!_options.HasCredentials)
        {
            throw new ArgumentException("Alpaca:ApiKeyId and Alpaca:ApiSecretKey are required.", nameof(options));
        }
    }

    public bool SupportsLiveData => true;

    public TimeSpan MinimumRequestInterval => TimeSpan.FromMilliseconds(_options.MinimumRequestIntervalMilliseconds);

    public string ProviderName => Name;

    public bool Supports(string symbol) => AlpacaSymbol.FromTicker(symbol) is not null;

    public static void ConfigureHttpClient(HttpClient client, AlpacaOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        client.BaseAddress = new Uri(options.DataBaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", options.ApiKeyId.Trim());
        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", options.ApiSecretKey.Trim());
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MarketDataContract?> GetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var alpacaSymbol = AlpacaSymbol.FromTicker(symbol);
        if (alpacaSymbol is null)
        {
            return null;
        }

        using var document = await GetJsonAsync(
            $"v2/stocks/{Uri.EscapeDataString(alpacaSymbol)}/snapshot?feed={Uri.EscapeDataString(_options.Feed)}",
            cancellationToken);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var trade = GetObject(root, "latestTrade");
        var price = GetDecimal(trade, "p");
        var tradeTime = GetTimestamp(trade, "t");
        if (price is not > 0m || tradeTime is null)
        {
            return null;
        }

        var quote = GetObject(root, "latestQuote");
        var bid = GetDecimal(quote, "bp");
        var ask = GetDecimal(quote, "ap");
        var quoteTime = GetTimestamp(quote, "t");
        var dailyBar = GetObject(root, "dailyBar");
        var previousDailyBar = GetObject(root, "prevDailyBar");

        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["source_type"] = "live",
            ["feed"] = _options.Feed,
            ["currency"] = "USD",
            ["alpaca_symbol"] = alpacaSymbol
        };
        if (bid is > 0m && ask is > 0m)
        {
            metadata["bid"] = bid.Value;
            metadata["ask"] = ask.Value;
        }

        if (quoteTime is not null)
        {
            metadata["quote_timestamp_utc"] = quoteTime.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        return new MarketDataContract
        {
            Ticker = symbol.Trim(),
            Symbol = symbol.Trim(),
            Price = price.Value,
            Open = GetDecimal(dailyBar, "o"),
            High = GetDecimal(dailyBar, "h"),
            Low = GetDecimal(dailyBar, "l"),
            Close = GetDecimal(dailyBar, "c"),
            Volume = GetDecimal(dailyBar, "v"),
            LiquidityScore = CalculateLiquidityScore(dailyBar, previousDailyBar),
            SpreadPercent = CalculateSpreadPercent(bid, ask),
            IsSynthetic = false,
            TimestampUtc = tradeTime.Value,
            Source = Name,
            ProviderName = Name,
            FreshnessWindow = TimeSpan.FromSeconds(_options.FreshnessSeconds),
            Metadata = metadata
        };
    }

    public async Task<IReadOnlyList<PriceBar>?> GetDailyBarsAsync(
        string symbol,
        int minimumBars,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumBars);
        var alpacaSymbol = AlpacaSymbol.FromTicker(symbol);
        if (alpacaSymbol is null)
        {
            return null;
        }

        // Roughly 252 trading days per 365 calendar days, plus a margin for holidays.
        var calendarDays = (int)Math.Ceiling(minimumBars * 365d / 252d) + 30;
        var start = _timeProvider.GetUtcNow().AddDays(-calendarDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var bars = new List<PriceBar>();
        string? pageToken = null;

        do
        {
            var path =
                $"v2/stocks/{Uri.EscapeDataString(alpacaSymbol)}/bars?timeframe=1Day&start={start}&limit=1000"
                + $"&adjustment=all&sort=asc&feed={Uri.EscapeDataString(_options.Feed)}"
                + (pageToken is null ? string.Empty : $"&page_token={Uri.EscapeDataString(pageToken)}");
            using var document = await GetJsonAsync(path, cancellationToken);
            if (document is null)
            {
                return null;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("bars", out var barsElement) && barsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var bar in barsElement.EnumerateArray())
                {
                    var timestamp = GetTimestamp(bar, "t");
                    var open = GetDecimal(bar, "o");
                    var high = GetDecimal(bar, "h");
                    var low = GetDecimal(bar, "l");
                    var close = GetDecimal(bar, "c");
                    if (timestamp is null || open is null || high is null || low is null || close is not > 0m)
                    {
                        continue;
                    }

                    bars.Add(new PriceBar(
                        timestamp.Value,
                        open.Value,
                        high.Value,
                        low.Value,
                        close.Value,
                        GetDecimal(bar, "v") ?? 0m,
                        GetDecimal(bar, "vw")));
                }
            }

            pageToken = root.TryGetProperty("next_page_token", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return bars;
    }

    /// <summary>
    /// Downloads daily bars for several symbols per request, following pagination and pausing
    /// <see cref="MinimumRequestInterval"/> between requests to stay inside the rate limit.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>> GetDailyBarsAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var result = new Dictionary<string, List<DailyBar>>(StringComparer.OrdinalIgnoreCase);
        var alpacaSymbols = symbols
            .Select(AlpacaSymbol.FromTicker)
            .Where(symbol => symbol is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (alpacaSymbols.Length == 0 || start > end)
        {
            return new Dictionary<string, IReadOnlyList<DailyBar>>();
        }

        string? pageToken = null;
        var firstRequest = true;
        do
        {
            if (!firstRequest && MinimumRequestInterval > TimeSpan.Zero)
            {
                await Task.Delay(MinimumRequestInterval, _timeProvider, cancellationToken);
            }

            firstRequest = false;
            var path =
                $"v2/stocks/bars?symbols={Uri.EscapeDataString(string.Join(',', alpacaSymbols))}&timeframe=1Day"
                + $"&start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}&limit=10000&adjustment=all&sort=asc"
                + $"&feed={Uri.EscapeDataString(_options.Feed)}"
                + (pageToken is null ? string.Empty : $"&page_token={Uri.EscapeDataString(pageToken)}");
            using var document = await GetJsonAsync(path, cancellationToken);
            if (document is null)
            {
                break;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("bars", out var barsElement) && barsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var symbolBars in barsElement.EnumerateObject())
                {
                    if (symbolBars.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    if (!result.TryGetValue(symbolBars.Name, out var list))
                    {
                        list = [];
                        result[symbolBars.Name] = list;
                    }

                    foreach (var bar in symbolBars.Value.EnumerateArray())
                    {
                        var timestamp = GetTimestamp(bar, "t");
                        var open = GetDecimal(bar, "o");
                        var high = GetDecimal(bar, "h");
                        var low = GetDecimal(bar, "l");
                        var close = GetDecimal(bar, "c");
                        if (timestamp is null || open is not > 0m || high is null || low is null || close is not > 0m)
                        {
                            continue;
                        }

                        // Alpaca stamps a daily bar at midnight New York time, which falls on the same date in UTC.
                        list.Add(new DailyBar(
                            DateOnly.FromDateTime(timestamp.Value.UtcDateTime),
                            open.Value,
                            high.Value,
                            low.Value,
                            close.Value,
                            GetDecimal(bar, "v") ?? 0m));
                    }
                }
            }

            pageToken = root.TryGetProperty("next_page_token", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return result.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<DailyBar>)item.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 0-100 score from the larger of today's and the previous session's traded value on the configured
    /// feed: $1M scores 40, $10M scores 60, $100M scores 80. The IEX feed carries only a fraction of
    /// consolidated volume, so it understates liquidity; that errs on the side of rejecting trades.
    /// </summary>
    public static decimal? CalculateLiquidityScore(JsonElement? dailyBar, JsonElement? previousDailyBar)
    {
        var dollarVolume = Math.Max(DollarVolume(dailyBar), DollarVolume(previousDailyBar));
        if (dollarVolume <= 0m)
        {
            return null;
        }

        var score = (20m * (decimal)Math.Log10((double)dollarVolume)) - 80m;
        return Math.Round(Math.Clamp(score, 0m, 100m), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Bid/ask spread as a percentage of the mid price, capped at the contract maximum of 25.</summary>
    public static decimal? CalculateSpreadPercent(decimal? bid, decimal? ask)
    {
        if (bid is not > 0m || ask is not > 0m || ask < bid)
        {
            return null;
        }

        var mid = (bid.Value + ask.Value) / 2m;
        var spread = ((ask.Value - bid.Value) / mid) * 100m;
        return Math.Round(Math.Min(spread, 25m), 4, MidpointRounding.AwayFromZero);
    }

    private static decimal DollarVolume(JsonElement? bar)
    {
        var volume = GetDecimal(bar, "v");
        var price = GetDecimal(bar, "vw") ?? GetDecimal(bar, "c");
        return volume is > 0m && price is > 0m ? volume.Value * price.Value : 0m;
    }

    private async Task<JsonDocument?> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory();
        using var response = await client.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Alpaca rejected the request ({(int)response.StatusCode}). Check Alpaca:ApiKeyId, Alpaca:ApiSecretKey and that the account's data plan includes the \"{_options.Feed}\" feed.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("Alpaca rate limit reached (429). Increase Alpaca:MinimumRequestIntervalMilliseconds.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Alpaca request failed with status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static JsonElement? GetObject(JsonElement parent, string name)
    {
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object
                ? value
                : null;
    }

    private static decimal? GetDecimal(JsonElement? parent, string name)
    {
        return parent is { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
                ? number
                : null;
    }

    private static DateTimeOffset? GetTimestamp(JsonElement? parent, string name)
    {
        return parent is { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp)
                ? timestamp
                : null;
    }
}
