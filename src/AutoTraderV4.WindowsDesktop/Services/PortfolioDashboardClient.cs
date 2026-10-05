using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AutoTraderV4.WindowsDesktop.Models;

namespace AutoTraderV4.WindowsDesktop.Services;

public sealed class PortfolioDashboardClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;

    public PortfolioDashboardClient()
        : this(CreateClient())
    {
    }

    public PortfolioDashboardClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Log($"Initialized dashboard client with base URL: {_httpClient.BaseAddress}");
    }

    private static string? ReadBackendUrlFromAppSettings()
    {
        var configFiles = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json")
        };

        foreach (var configFile in configFiles)
        {
            if (!File.Exists(configFile))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(configFile));
                if (document.RootElement.TryGetProperty("AUTO_TRADER_BACKEND_URL", out var value))
                {
                    var result = value.GetString();
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        return result;
                    }
                }
            }
            catch
            {
                // Ignore malformed config files and continue to the next source.
            }
        }

        return null;
    }

    private static HttpClient CreateClient()
    {
        var baseUrl = Environment.GetEnvironmentVariable("AUTO_TRADER_BACKEND_URL")
            ?? ReadBackendUrlFromAppSettings()
            ?? "http://localhost:5065";

        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };

        Log($"Using backend base URL: {client.BaseAddress}");
        return client;
    }

    private static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var formatted = $"[{timestamp}] {message}";
        Console.WriteLine(formatted);
        Debug.WriteLine(formatted);
    }

    public async Task<PortfolioDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Log("Requesting dashboard from backend.");
            var response = await _httpClient.GetAsync("/api/dashboard", cancellationToken);
            Log($"Dashboard response status: {(int)response.StatusCode} {response.StatusCode}.");

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<PortfolioDashboard>(SerializerOptions, cancellationToken);
            if (payload is null)
            {
                Log("Dashboard payload was null; using fallback data.");
                return CreateFallbackDashboard();
            }

            Log("Dashboard retrieved successfully from backend.");
            return payload;
        }
        catch (Exception ex)
        {
            Log($"Dashboard request failed: {ex.Message}");
            return CreateFallbackDashboard();
        }
    }

    public async Task<StockTrackingCycleResult> RunStockTrackingCycleAsync(CancellationToken cancellationToken = default)
    {
        Log("Requesting a stock tracking cycle from the backend.");
        using var response = await _httpClient.PostAsync("/api/stock-tracking/run-cycle", content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = string.IsNullOrWhiteSpace(detail)
                ? $"Backend responded with {(int)response.StatusCode} ({response.StatusCode})."
                : $"Backend responded with {(int)response.StatusCode} ({response.StatusCode}): {detail.Trim()}";
            Log($"Stock tracking request failed: {message}");
            throw new HttpRequestException(message, null, response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<StockTrackingCycleResult>(SerializerOptions, cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("The backend returned an empty stock tracking result.");
        }

        Log($"Stock tracking cycle completed with {result.UpdatedTickers.Count} updated tickers.");
        return result;
    }

    public async Task<StocktwitsMcpSettingsView> GetStocktwitsMcpSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("/api/settings/stocktwits-mcp", cancellationToken);
        response.EnsureSuccessStatusCode();

        var settings = await response.Content.ReadFromJsonAsync<StocktwitsMcpSettingsView>(SerializerOptions, cancellationToken);
        return settings ?? throw new InvalidOperationException("The backend returned an empty Stocktwits MCP setting.");
    }

    public async Task<StocktwitsMcpSettingsView> UpdateStocktwitsMcpSettingsAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PutAsJsonAsync(
            "/api/settings/stocktwits-mcp",
            new StocktwitsMcpSettingsView { Enabled = enabled },
            SerializerOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Backend responded with {(int)response.StatusCode} ({response.StatusCode})."
                    : $"Backend responded with {(int)response.StatusCode} ({response.StatusCode}): {detail.Trim()}",
                null,
                response.StatusCode);
        }

        var settings = await response.Content.ReadFromJsonAsync<StocktwitsMcpSettingsView>(SerializerOptions, cancellationToken);
        return settings ?? throw new InvalidOperationException("The backend returned an empty Stocktwits MCP setting.");
    }

    public static PortfolioDashboard CreateFallbackDashboard()
    {
        Log("Creating fallback dashboard data.");
        var now = DateTimeOffset.UtcNow;

        return new PortfolioDashboard
        {
            Summary = new PortfolioSummary
            {
                Currency = "USD",
                TotalPortfolioValue = 245_680.42m,
                DailyPnL = 2_140.75m,
                WeeklyPnL = 8_960.15m,
                MonthlyPnL = 12_410.30m,
                AvailableCash = 46_000m,
                TotalExposurePercent = 81.3m,
                DefensiveMode = false,
                LastUpdatedUtc = now
            },
            Positions =
            [
                new PortfolioPositionView { Symbol = "NVDA", Quantity = 60m, EntryPrice = 110.25m, CurrentPrice = 126.95m, UnrealizedPnl = 999.0m, StopLoss = 104.00m, TakeProfit = 145.00m, Confidence = 92, Sector = "Technology", Currency = "USD", AllocationPercent = 31.4m, LastUpdatedUtc = now },
                new PortfolioPositionView { Symbol = "MSFT", Quantity = 48m, EntryPrice = 428.10m, CurrentPrice = 451.40m, UnrealizedPnl = 1123.2m, StopLoss = 410.00m, TakeProfit = 490.00m, Confidence = 87, Sector = "Technology", Currency = "USD", AllocationPercent = 22.8m, LastUpdatedUtc = now },
                new PortfolioPositionView { Symbol = "AAPL", Quantity = 110m, EntryPrice = 192.30m, CurrentPrice = 201.88m, UnrealizedPnl = 1058.0m, StopLoss = 184.00m, TakeProfit = 220.00m, Confidence = 84, Sector = "Technology", Currency = "USD", AllocationPercent = 23.0m, LastUpdatedUtc = now },
                new PortfolioPositionView { Symbol = "XLE", Quantity = 220m, EntryPrice = 86.90m, CurrentPrice = 92.60m, UnrealizedPnl = 1230.0m, StopLoss = 82.50m, TakeProfit = 99.50m, Confidence = 78, Sector = "Energy", Currency = "USD", AllocationPercent = 21.2m, LastUpdatedUtc = now }
            ],
            Watchlist =
            [
                new WatchlistOpportunity { Symbol = "AMD", Rating = "Strong Buy", Confidence = 91, ForecastReturn = 12.8m, RiskScore = 26m, Price = 168.40m, Sector = "Semiconductors", Strategy = "Momentum", TopFactors = ["Earnings surprise", "Strong volume", "Relative strength"], LastUpdatedUtc = now },
                new WatchlistOpportunity { Symbol = "META", Rating = "Buy", Confidence = 88, ForecastReturn = 9.6m, RiskScore = 29m, Price = 515.10m, Sector = "Communication Services", Strategy = "Trend Following", TopFactors = ["AI demand", "Ad revenue growth", "Bullish trend"], LastUpdatedUtc = now },
                new WatchlistOpportunity { Symbol = "CRM", Rating = "Buy", Confidence = 83, ForecastReturn = 7.1m, RiskScore = 33m, Price = 290.75m, Sector = "Software", Strategy = "Earnings Surprise", TopFactors = ["Guidance lift", "Analyst upgrades", "Healthy cash flow"], LastUpdatedUtc = now }
            ],
            MarketOverview =
            [
                new MarketOverviewCard { Symbol = "SPX", Label = "S&P 500", Value = 5624.18m, ChangePercent = 0.86m, Trend = "Bullish", LastUpdatedUtc = now },
                new MarketOverviewCard { Symbol = "IXIC", Label = "Nasdaq", Value = 18412.73m, ChangePercent = 1.11m, Trend = "Bullish", LastUpdatedUtc = now },
                new MarketOverviewCard { Symbol = "DJI", Label = "Dow Jones", Value = 40218.55m, ChangePercent = 0.47m, Trend = "Positive", LastUpdatedUtc = now },
                new MarketOverviewCard { Symbol = "FTSE", Label = "FTSE 100", Value = 8394.20m, ChangePercent = 0.21m, Trend = "Neutral", LastUpdatedUtc = now },
                new MarketOverviewCard { Symbol = "VIX", Label = "VIX", Value = 13.14m, ChangePercent = -5.19m, Trend = "Lower volatility", LastUpdatedUtc = now }
            ],
            Insights =
            [
                new DashboardInsight { Title = "Top buy opportunity", Summary = "AMD remains the highest-conviction trade with strong momentum and favorable earnings revisions.", Type = "buy", Symbol = "AMD" },
                new DashboardInsight { Title = "Defensive posture", Summary = "Portfolio remains within risk limits and cash reserves stay above the minimum threshold.", Type = "risk" },
                new DashboardInsight { Title = "Sector rotation", Summary = "Technology remains the primary sector leader while energy still contributes healthy cash flow support.", Type = "macro" }
            ],
            Alerts =
            [
                new DashboardAlert { Title = "Trade executed", Message = "NVDA position scaled into the portfolio with a 92 confidence score.", Type = "trade", Severity = "info", TimeUtc = now.AddMinutes(-8) },
                new DashboardAlert { Title = "Risk check", Message = "Portfolio exposure remains below the 95% ceiling.", Type = "risk", Severity = "success", TimeUtc = now.AddMinutes(-16) },
                new DashboardAlert { Title = "Market watch", Message = "Sector breadth is improving and breadth remains supportive of the current trend.", Type = "market", Severity = "info", TimeUtc = now.AddMinutes(-22) }
            ],
            LastUpdatedUtc = now
        };
    }
}