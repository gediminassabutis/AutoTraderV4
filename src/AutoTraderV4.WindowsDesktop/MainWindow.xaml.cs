using System.Windows;
using System.Globalization;
using AutoTraderV4.WindowsDesktop.Services;
using AutoTraderV4.WindowsDesktop.ViewModels;

namespace AutoTraderV4.WindowsDesktop;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var dashboardClient = new PortfolioDashboardClient();
        _viewModel = new DashboardViewModel(dashboardClient);
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        RefreshButton.Click += RefreshButton_Click;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        BindViewModel();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        BindViewModel();
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

        PositionsGrid.ItemsSource = _viewModel.Positions;
        WatchlistGrid.ItemsSource = _viewModel.Watchlist;
        MarketOverviewList.ItemsSource = _viewModel.MarketOverview;
        InsightsList.ItemsSource = _viewModel.Insights;
        AlertsList.ItemsSource = _viewModel.Alerts;
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