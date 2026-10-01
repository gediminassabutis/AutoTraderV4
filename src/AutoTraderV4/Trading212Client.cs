using System.Net.Http.Headers;
using System.Text.Json;

namespace AutoTraderV4;

public interface ITrading212Client
{
    Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Trading212Position>> GetPositionsAsync(CancellationToken cancellationToken = default);
    Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default);
}

public sealed class DemoTrading212Client : ITrading212Client
{
    public Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Trading212AccountSummary
        {
            Id = 42,
            Currency = "USD",
            CashDetails = new Trading212Cash
            {
                AvailableToTrade = 12500.50m,
                InPies = 200.25m,
                ReservedForOrders = 75.50m
            },
            Investments = new Trading212Investments
            {
                CurrentValue = 25000.50m,
                RealizedProfitLoss = 1200.25m,
                TotalCost = 22000.00m,
                UnrealizedProfitLoss = 3000.50m
            },
            TotalValue = 48750.25m
        });
    }

    public Task<IReadOnlyList<Trading212Position>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Trading212Position>>([]);
    }

    public Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        return Task.FromResult(new Trading212OrderResult
        {
            Id = 99,
            Ticker = order.Ticker,
            Status = "demo-accepted"
        });
    }
}

public sealed class Trading212Client : ITrading212Client
{
    private readonly HttpClient _httpClient;
    private readonly Trading212Options _options;

    public Trading212Client(HttpClient httpClient, Trading212Options options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/api/v0/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_options.ApiKey}:{_options.ApiSecret}")));
    }

    public async Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("equity/account/summary", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Trading212AccountSummary>(json, JsonOptions.Default)
            ?? throw new InvalidOperationException("Account summary payload was empty.");
    }

    public async Task<IReadOnlyList<Trading212Position>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("equity/positions", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<Trading212Position>>(json, JsonOptions.Default)
            ?? throw new InvalidOperationException("Positions payload was empty.");
    }

    public async Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        order.Validate();

        var endpoint = order.Type switch
        {
            Trading212OrderType.Market => "equity/orders/market",
            Trading212OrderType.Limit => "equity/orders/limit",
            Trading212OrderType.Stop => "equity/orders/stop",
            Trading212OrderType.StopLimit => "equity/orders/stop_limit",
            _ => throw new NotSupportedException($"Trading 212 order type '{order.Type}' is not supported.")
        };

        var payload = new Dictionary<string, object?>
        {
            ["ticker"] = order.Ticker,
            ["quantity"] = order.Quantity
        };

        switch (order.Type)
        {
            case Trading212OrderType.Market:
                if (order.ExtendedHours)
                {
                    payload["extendedHours"] = true;
                }
                break;
            case Trading212OrderType.Limit:
                if (!order.LimitPrice.HasValue)
                {
                    throw new ArgumentException("Limit price is required for limit orders.", "order");
                }

                payload["limitPrice"] = order.LimitPrice.Value;
                payload["timeValidity"] = Trading212TimeValidityOptions.Normalize(order.TimeValidity);
                break;
            case Trading212OrderType.Stop:
                if (!order.StopPrice.HasValue)
                {
                    throw new ArgumentException("Stop price is required for stop orders.", "order");
                }

                payload["stopPrice"] = order.StopPrice.Value;
                payload["timeValidity"] = Trading212TimeValidityOptions.Normalize(order.TimeValidity);
                break;
            case Trading212OrderType.StopLimit:
                if (!order.LimitPrice.HasValue)
                {
                    throw new ArgumentException("Limit price is required for stop-limit orders.", "order");
                }

                if (!order.StopPrice.HasValue)
                {
                    throw new ArgumentException("Stop price is required for stop-limit orders.", "order");
                }

                payload["limitPrice"] = order.LimitPrice.Value;
                payload["stopPrice"] = order.StopPrice.Value;
                payload["timeValidity"] = Trading212TimeValidityOptions.Normalize(order.TimeValidity);
                break;
            default:
                throw new NotSupportedException($"Trading 212 order type '{order.Type}' is not supported.");
        }

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions.Default), System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Trading212OrderResult>(json, JsonOptions.Default)
            ?? throw new InvalidOperationException("Order payload was empty.");
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
