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
    private string _statusMessage = "Connecting to the AutoTrader backend...";

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

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = "Loading dashboard data...";

        try
        {
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
