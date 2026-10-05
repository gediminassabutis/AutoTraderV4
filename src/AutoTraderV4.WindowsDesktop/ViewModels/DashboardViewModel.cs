using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoTraderV4.WindowsDesktop.Models;
using AutoTraderV4.WindowsDesktop.Services;

namespace AutoTraderV4.WindowsDesktop.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly PortfolioDashboardClient _dashboardClient;
    private PortfolioDashboard _dashboard = PortfolioDashboardClient.CreateFallbackDashboard();
    private bool _isLoading;
    private bool _isRunningStockTracking;
    private string _statusMessage = "Connecting to the AutoTrader backend...";
    private bool _stocktwitsMcpEnabled;
    private bool _stocktwitsSettingsLoaded;
    private bool _isUpdatingStocktwitsMcp;
    private string _stocktwitsMcpStatusMessage = "Loading Stocktwits MCP setting...";

    public DashboardViewModel(PortfolioDashboardClient dashboardClient)
    {
        _dashboardClient = dashboardClient ?? throw new ArgumentNullException(nameof(dashboardClient));
        Positions = new ObservableCollection<PortfolioPositionView>();
        Watchlist = new ObservableCollection<WatchlistOpportunity>();
        MarketOverview = new ObservableCollection<MarketOverviewCard>();
        Insights = new ObservableCollection<DashboardInsight>();
        Alerts = new ObservableCollection<DashboardAlert>();
    }

    public PortfolioDashboard Dashboard
    {
        get => _dashboard;
        set
        {
            if (ReferenceEquals(_dashboard, value))
            {
                return;
            }

            _dashboard = value;
            OnPropertyChanged();
            RefreshCollections();
        }
    }

    public ObservableCollection<PortfolioPositionView> Positions { get; }

    public ObservableCollection<WatchlistOpportunity> Watchlist { get; }

    public ObservableCollection<MarketOverviewCard> MarketOverview { get; }

    public ObservableCollection<DashboardInsight> Insights { get; }

    public ObservableCollection<DashboardAlert> Alerts { get; }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsStocktwitsMcpEnabled
    {
        get => _stocktwitsMcpEnabled;
        private set
        {
            if (_stocktwitsMcpEnabled == value)
            {
                return;
            }

            _stocktwitsMcpEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool CanUpdateStocktwitsMcp => _stocktwitsSettingsLoaded && !_isUpdatingStocktwitsMcp;

    public string StocktwitsMcpStatusMessage
    {
        get => _stocktwitsMcpStatusMessage;
        private set
        {
            if (_stocktwitsMcpStatusMessage == value)
            {
                return;
            }

            _stocktwitsMcpStatusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsRunningStockTracking
    {
        get => _isRunningStockTracking;
        private set
        {
            if (_isRunningStockTracking == value)
            {
                return;
            }

            _isRunningStockTracking = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = "Loading dashboard data...";

        try
        {
            try
            {
                var settings = await _dashboardClient.GetStocktwitsMcpSettingsAsync(cancellationToken);
                IsStocktwitsMcpEnabled = settings.Enabled;
                _stocktwitsSettingsLoaded = true;
                StocktwitsMcpStatusMessage = settings.Enabled
                    ? "Enabled; stats update during tracking cycles."
                    : "Disabled; no Stocktwits requests will be made.";
            }
            catch (Exception ex)
            {
                _stocktwitsSettingsLoaded = false;
                StocktwitsMcpStatusMessage = $"Unable to load setting: {ex.Message}";
            }

            var dashboard = await _dashboardClient.GetDashboardAsync(cancellationToken);
            Dashboard = dashboard;
            StatusMessage = dashboard.Summary.TotalPortfolioValue > 0m
                ? $"Portfolio refreshed at {dashboard.LastUpdatedUtc:HH:mm:ss} UTC"
                : "Dashboard data loaded in fallback mode.";
        }
        catch (Exception ex)
        {
            Dashboard = PortfolioDashboardClient.CreateFallbackDashboard();
            StatusMessage = $"Unable to contact backend: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task UpdateStocktwitsMcpEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!CanUpdateStocktwitsMcp || enabled == IsStocktwitsMcpEnabled)
        {
            return;
        }

        _isUpdatingStocktwitsMcp = true;
        StocktwitsMcpStatusMessage = "Saving Stocktwits MCP setting...";
        OnPropertyChanged(nameof(CanUpdateStocktwitsMcp));

        try
        {
            var settings = await _dashboardClient.UpdateStocktwitsMcpSettingsAsync(enabled, cancellationToken);
            IsStocktwitsMcpEnabled = settings.Enabled;
            StocktwitsMcpStatusMessage = settings.Enabled
                ? "Enabled; stats update during tracking cycles."
                : "Disabled; no Stocktwits requests will be made.";
        }
        catch (Exception ex)
        {
            StocktwitsMcpStatusMessage = $"Unable to update setting: {ex.Message}";
        }
        finally
        {
            _isUpdatingStocktwitsMcp = false;
            OnPropertyChanged(nameof(CanUpdateStocktwitsMcp));
        }
    }

    public async Task RunStockTrackingCycleAsync(CancellationToken cancellationToken = default)
    {
        IsRunningStockTracking = true;
        StatusMessage = "Running StockTrackingService cycle...";

        try
        {
            var result = await _dashboardClient.RunStockTrackingCycleAsync(cancellationToken);
            await LoadAsync(cancellationToken);

            var status = $"Stock tracking completed: {result.StockTicksCollected} stock ticks collected across "
                + $"{result.TrackedStockCount} tracked stocks, {result.UpdatedTickers.Count} held prices updated, "
                + $"{result.SellRecommendations.Count} sell recommendations, "
                + $"{result.BuyOpportunities.Count} buy opportunities.";
            var newStockTickers = result.NewStocks
                .Select(stock => stock.Ticker)
                .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
                .Take(5)
                .ToArray();
            status += result.NewlyTrackedStockCount == 0
                ? " No new stocks added to the research universe."
                : result.NewStocks.Count == 0
                    ? $" {result.NewlyTrackedStockCount} new stocks added to the research universe."
                    : $" {result.NewlyTrackedStockCount} new stocks added to the research universe; eligible sample: {string.Join(", ", newStockTickers)}.";
            if (result.DeferredStockTickCount > 0)
            {
                status += !result.MarketDataReadiness.LiveProviderConfigured
                    ? $" {result.DeferredStockTickCount} stock ticks are waiting for a live provider."
                    : result.StockTickCollectionBudgetExhausted
                        ? $" The collection time budget was reached; {result.DeferredStockTickCount} stocks will resume next cycle."
                        : $" {result.DeferredStockTickCount} stocks are deferred to later cycles by the batch limit.";
            }

            if (!string.IsNullOrWhiteSpace(result.NewStockDiscoveryError))
            {
                status += $" New stock discovery failed: {result.NewStockDiscoveryError}";
            }

            if (result.SentimentTickers.Count > 0)
            {
                status += $" Stocktwits stats saved for {result.SentimentTickers.Count} positions.";
            }

            if (result.SentimentFailures.Count > 0)
            {
                status += $" Stocktwits ingestion failed: {string.Join("; ", result.SentimentFailures)}";
            }

            if (!result.MarketDataReadiness.LiveProviderConfigured
                && !string.IsNullOrWhiteSpace(result.MarketDataReadiness.Message))
            {
                status += $" {result.MarketDataReadiness.Message}";
            }

            StatusMessage = status;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stock tracking failed: {ex.Message}";
        }
        finally
        {
            IsRunningStockTracking = false;
        }
    }

    private void RefreshCollections()
    {
        Positions.Clear();
        foreach (var position in Dashboard.Positions)
        {
            Positions.Add(position);
        }

        Watchlist.Clear();
        foreach (var opportunity in Dashboard.Watchlist)
        {
            Watchlist.Add(opportunity);
        }

        MarketOverview.Clear();
        foreach (var item in Dashboard.MarketOverview)
        {
            MarketOverview.Add(item);
        }

        Insights.Clear();
        foreach (var insight in Dashboard.Insights)
        {
            Insights.Add(insight);
        }

        Alerts.Clear();
        foreach (var alert in Dashboard.Alerts)
        {
            Alerts.Add(alert);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
