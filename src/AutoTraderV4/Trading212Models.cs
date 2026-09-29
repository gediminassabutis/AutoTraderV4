using System.Globalization;
using System.Text.Json;
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
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal Cash { get; set; }

    [JsonPropertyName("equity")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal Equity { get; set; }
}

public sealed class Trading212DecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var stringValue = reader.GetString();
                if (decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedStringValue))
                {
                    return parsedStringValue;
                }

                throw new JsonException($"Unable to convert string '{stringValue}' to decimal.");
            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    var root = document.RootElement;

                    foreach (var propertyName in new[] { "amount", "value", "total", "balance" })
                    {
                        if (root.TryGetProperty(propertyName, out var propertyValue) && propertyValue.ValueKind != JsonValueKind.Null)
                        {
                            return propertyValue.ValueKind switch
                            {
                                JsonValueKind.Number => propertyValue.GetDecimal(),
                                JsonValueKind.String => decimal.TryParse(propertyValue.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPropertyValue)
                                    ? parsedPropertyValue
                                    : throw new JsonException($"Unable to convert {propertyName} to decimal."),
                                _ => throw new JsonException($"Unable to convert property '{propertyName}' of type '{propertyValue.ValueKind}' to decimal.")
                            };
                        }
                    }
                }


                throw new JsonException("Object payload did not contain a decimal-like amount field.");
            default:
                throw new JsonException($"Unable to convert token type '{reader.TokenType}' to decimal.");
        }
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
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
