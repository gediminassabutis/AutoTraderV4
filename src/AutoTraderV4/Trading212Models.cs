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
    public decimal? LimitPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public bool ExtendedHours { get; init; }
    public string TimeValidity { get; init; } = "DAY";

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

public sealed class Trading212Cash
{
    [JsonPropertyName("availableToTrade")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal AvailableToTrade { get; set; }

    [JsonPropertyName("inPies")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal InPies { get; set; }

    [JsonPropertyName("reservedForOrders")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal ReservedForOrders { get; set; }
}

public sealed class Trading212Investments
{
    [JsonPropertyName("currentValue")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal CurrentValue { get; set; }

    [JsonPropertyName("realizedProfitLoss")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal RealizedProfitLoss { get; set; }

    [JsonPropertyName("totalCost")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal TotalCost { get; set; }

    [JsonPropertyName("unrealizedProfitLoss")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal UnrealizedProfitLoss { get; set; }
}

public sealed class Trading212AccountSummary
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("cash")]
    [JsonConverter(typeof(Trading212CashConverter))]
    public Trading212Cash CashDetails { get; set; } = new();

    [JsonPropertyName("investments")]
    public Trading212Investments Investments { get; set; } = new();

    [JsonPropertyName("totalValue")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal TotalValue { get; set; }

    [JsonPropertyName("equity")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal Equity
    {
        get => TotalValue;
        set => TotalValue = value;
    }

    [JsonIgnore]
    public decimal Cash
    {
        get => CashDetails.AvailableToTrade;
        set => CashDetails.AvailableToTrade = value;
    }
}

public sealed class Trading212Instrument
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("isin")]
    public string Isin { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;
}

public sealed class Trading212OrderResult
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal Quantity { get; set; }

    [JsonPropertyName("filledQuantity")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal FilledQuantity { get; set; }

    [JsonPropertyName("filledValue")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal FilledValue { get; set; }

    [JsonPropertyName("limitPrice")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal LimitPrice { get; set; }

    [JsonPropertyName("stopPrice")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal StopPrice { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(Trading212DecimalConverter))]
    public decimal Value { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("timeInForce")]
    public string TimeInForce { get; set; } = string.Empty;

    [JsonPropertyName("extendedHours")]
    public bool ExtendedHours { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("instrument")]
    public Trading212Instrument Instrument { get; set; } = new();
}

public sealed class Trading212CashConverter : JsonConverter<Trading212Cash>
{
    public override Trading212Cash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return new Trading212Cash
            {
                AvailableToTrade = reader.GetDecimal()
            };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Unable to convert token type '{reader.TokenType}' to Trading212Cash.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var result = new Trading212Cash();

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "amount":
                case "value":
                case "balance":
                    if (root.TryGetProperty("availableToTrade", out _) || !root.TryGetProperty("inPies", out _) && !root.TryGetProperty("reservedForOrders", out _))
                    {
                        result.AvailableToTrade = ReadDecimal(property.Value);
                    }
                    break;
                case "availableToTrade":
                case "available_to_trade":
                    result.AvailableToTrade = ReadDecimal(property.Value);
                    break;
                case "inPies":
                    result.InPies = ReadDecimal(property.Value);
                    break;
                case "reservedForOrders":
                    result.ReservedForOrders = ReadDecimal(property.Value);
                    break;
            }
        }

        if (result.AvailableToTrade == 0m && root.TryGetProperty("amount", out var amount))
        {
            result.AvailableToTrade = ReadDecimal(amount);
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Trading212Cash value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("availableToTrade");
        writer.WriteNumberValue(value.AvailableToTrade);
        writer.WritePropertyName("inPies");
        writer.WriteNumberValue(value.InPies);
        writer.WritePropertyName("reservedForOrders");
        writer.WriteNumberValue(value.ReservedForOrders);
        writer.WriteEndObject();
    }

    private static decimal ReadDecimal(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new JsonException($"Unable to convert string '{value.GetString()}' to decimal."),
            _ => throw new JsonException($"Unable to convert property '{value}' to decimal.")
        };
    }
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

                    foreach (var propertyName in new[] { "amount", "value", "total", "balance", "availableToTrade", "currentValue", "realizedProfitLoss", "totalCost", "unrealizedProfitLoss" })
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
