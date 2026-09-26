using System.Net.Http.Headers;
using System.Text.Json;

namespace AutoTraderV4;

public interface ITrading212Client
{
    Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default);
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
            Cash = 12500.50m,
            Equity = 48750.25m
        });
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

    public async Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var payload = new Dictionary<string, object>
        {
            ["ticker"] = order.Ticker,
            ["quantity"] = order.Quantity,
            ["type"] = order.Type.ToString().ToLowerInvariant()
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions.Default), System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("equity/orders/market", content, cancellationToken).ConfigureAwait(false);
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
