using System.Text.Json.Serialization;

namespace AutoTraderV4;

public sealed class Trading212Options
{
    public string BaseUrl { get; set; } = "https://demo.trading212.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public bool UseDemoData { get; set; } = true;
}

public static class Trading212Credentials
{
    public static string CreateAuthorizationHeader(string apiKey, string apiSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiSecret);

        var raw = $"{apiKey}:{apiSecret}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
        return $"Basic {encoded}";
    }
}

public enum Trading212OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit,
    TrailingStop
}

public sealed record Trading212OrderRequest
{
    public string Ticker { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public Trading212OrderType Type { get; init; }

    public static Trading212OrderRequest CreateBuy(string ticker, decimal quantity, Trading212OrderType type)
    {
        return new Trading212OrderRequest
        {
            Ticker = ticker,
            Quantity = Math.Abs(quantity),
            Type = type
        };
    }

    public static Trading212OrderRequest CreateSell(string ticker, decimal quantity, Trading212OrderType type)
    {
        return new Trading212OrderRequest
        {
            Ticker = ticker,
            Quantity = -Math.Abs(quantity),
            Type = type
        };
    }
}

public sealed class Trading212AccountSummary
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("cash")]
    public decimal Cash { get; set; }

    [JsonPropertyName("equity")]
    public decimal Equity { get; set; }
}

public sealed class Trading212OrderResult
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
