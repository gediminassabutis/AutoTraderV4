using System.Windows;
using System.Globalization;
using System.Windows.Threading;
using AutoTraderV4.WindowsDesktop.Services;
using AutoTraderV4.WindowsDesktop.ViewModels;

namespace AutoTraderV4.WindowsDesktop;

public partial class MainWindow : Window
{
    private const int DefaultRefreshIntervalSeconds = 60;
    private const int MinimumRefreshIntervalSeconds = 5;
    private const int MaximumRefreshIntervalSeconds = 3600;

    private readonly DashboardViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;
    private DateTimeOffset _nextRefreshAtUtc;
    private int _refreshIntervalSeconds = DefaultRefreshIntervalSeconds;
    private bool _isDashboardRefreshRunning;

    public MainWindow()
    {
        InitializeComponent();

        var dashboardClient = new PortfolioDashboardClient();
        _viewModel = new DashboardViewModel(dashboardClient);
        DataContext = _viewModel;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        ApplyRefreshIntervalButton.Click += ApplyRefreshIntervalButton_Click;
        StocktwitsMcpCheckBox.Click += StocktwitsMcpCheckBox_Click;
        RefreshButton.Click += RefreshButton_Click;
        RunStockTrackingButton.Click += RunStockTrackingButton_Click;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDashboardAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDashboardAsync();
    }

    private async void StocktwitsMcpCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (StocktwitsMcpCheckBox.IsChecked is not bool enabled)
        {
            return;
        }

        var updateTask = _viewModel.UpdateStocktwitsMcpEnabledAsync(enabled);
        BindViewModel();
        await updateTask;
        BindViewModel();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        UpdateRefreshProgress();
        if (DateTimeOffset.UtcNow >= _nextRefreshAtUtc)
        {
            await RefreshDashboardAsync();
        }
    }

    private void ApplyRefreshIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshIntervalInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalSeconds)
            || intervalSeconds < MinimumRefreshIntervalSeconds
            || intervalSeconds > MaximumRefreshIntervalSeconds)
        {
            _viewModel.StatusMessage = $"Enter a refresh interval between {MinimumRefreshIntervalSeconds} and {MaximumRefreshIntervalSeconds} seconds.";
            BindViewModel();
            return;
        }

        _refreshIntervalSeconds = intervalSeconds;
        _viewModel.StatusMessage = $"Dashboard auto-refresh set to every {intervalSeconds} seconds.";
        BindViewModel();

        if (!_isDashboardRefreshRunning && !_viewModel.IsRunningStockTracking)
        {
            ScheduleNextRefresh();
        }
    }

    private async void RunStockTrackingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDashboardRefreshRunning || _viewModel.IsRunningStockTracking)
        {
            return;
        }

        _refreshTimer.Stop();
        RefreshProgressBar.IsIndeterminate = true;
        CountdownText.Text = "Stock tracking in progress...";

        var trackingTask = _viewModel.RunStockTrackingCycleAsync();
        BindViewModel();

        try
        {
            await trackingTask;
        }
        finally
        {
            BindViewModel();
            RefreshProgressBar.IsIndeterminate = false;
            ScheduleNextRefresh();
        }
    }

    private async Task RefreshDashboardAsync()
    {
        if (_isDashboardRefreshRunning || _viewModel.IsRunningStockTracking)
        {
            return;
        }

        _isDashboardRefreshRunning = true;
        _refreshTimer.Stop();
        RefreshProgressBar.IsIndeterminate = true;
        CountdownText.Text = "Refreshing dashboard...";

        var loadTask = _viewModel.LoadAsync();
        BindViewModel();

        try
        {
            await loadTask;
        }
        finally
        {
            _isDashboardRefreshRunning = false;
            BindViewModel();
            RefreshProgressBar.IsIndeterminate = false;
            ScheduleNextRefresh();
        }
    }

    private void ScheduleNextRefresh()
    {
        _refreshTimer.Stop();
        _nextRefreshAtUtc = DateTimeOffset.UtcNow.AddSeconds(_refreshIntervalSeconds);
        RefreshProgressBar.Value = 0;
        UpdateRefreshProgress();
        _refreshTimer.Start();
    }

    private void UpdateRefreshProgress()
    {
        var remainingSeconds = Math.Clamp(
            (int)Math.Ceiling((_nextRefreshAtUtc - DateTimeOffset.UtcNow).TotalSeconds),
            0,
            _refreshIntervalSeconds);
        RefreshProgressBar.Value = remainingSeconds * 100d / _refreshIntervalSeconds;
        CountdownText.Text = $"Next dashboard refresh in {remainingSeconds / 60:D2}:{remainingSeconds % 60:D2}";
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
    }

    private void BindViewModel()
    {
        var currency = _viewModel.Dashboard.Summary.Currency;
        PortfolioValueText.Text = FormatCurrency(_viewModel.Dashboard.Summary.TotalPortfolioValue, currency);
        CashText.Text = FormatCurrency(_viewModel.Dashboard.Summary.AvailableCash, currency);
        DailyPnLText.Text = FormatCurrency(_viewModel.Dashboard.Summary.DailyPnL, currency);
        WeeklyPnLText.Text = FormatCurrency(_viewModel.Dashboard.Summary.WeeklyPnL, currency);
        MonthlyPnLText.Text = FormatCurrency(_viewModel.Dashboard.Summary.MonthlyPnL, currency);
        ExposureText.Text = $"{_viewModel.Dashboard.Summary.TotalExposurePercent:F2}%";
        StatusText.Text = _viewModel.StatusMessage;
        StocktwitsMcpCheckBox.IsChecked = _viewModel.IsStocktwitsMcpEnabled;
        StocktwitsMcpCheckBox.IsEnabled = _viewModel.CanUpdateStocktwitsMcp;
        StocktwitsMcpStatusText.Text = _viewModel.StocktwitsMcpStatusMessage;
        StocktwitsMcpStatusText.ToolTip = _viewModel.StocktwitsMcpStatusMessage;

        PositionsGrid.ItemsSource = _viewModel.Positions;
        WatchlistGrid.ItemsSource = _viewModel.Watchlist;
        MarketOverviewList.ItemsSource = _viewModel.MarketOverview;
        InsightsList.ItemsSource = _viewModel.Insights;
        AlertsList.ItemsSource = _viewModel.Alerts;
        RefreshButton.IsEnabled = !_isDashboardRefreshRunning && !_viewModel.IsRunningStockTracking;
        RunStockTrackingButton.IsEnabled = !_isDashboardRefreshRunning && !_viewModel.IsRunningStockTracking;
        RunStockTrackingButton.Content = _viewModel.IsRunningStockTracking ? "Running tracking..." : "Run tracking now";
    }

    private static string FormatCurrency(decimal value, string? currency)
    {
        var currencyCode = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        var amount = Math.Abs(value).ToString("N2", CultureInfo.CurrentCulture);
        return value >= 0m
            ? $"{currencyCode} {amount}"
            : $"-{currencyCode} {amount}";
    }
}