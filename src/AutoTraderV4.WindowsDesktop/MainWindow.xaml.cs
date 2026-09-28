using System.Windows;
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
        PortfolioValueText.Text = FormatCurrency(_viewModel.Dashboard.Summary.TotalPortfolioValue);
        CashText.Text = FormatCurrency(_viewModel.Dashboard.Summary.AvailableCash);
        DailyPnLText.Text = FormatCurrency(_viewModel.Dashboard.Summary.DailyPnL);
        WeeklyPnLText.Text = FormatCurrency(_viewModel.Dashboard.Summary.WeeklyPnL);
        MonthlyPnLText.Text = FormatCurrency(_viewModel.Dashboard.Summary.MonthlyPnL);
        ExposureText.Text = $"{_viewModel.Dashboard.Summary.TotalExposurePercent:F2}%";
        StatusText.Text = _viewModel.StatusMessage;

        PositionsGrid.ItemsSource = _viewModel.Positions;
        WatchlistGrid.ItemsSource = _viewModel.Watchlist;
        MarketOverviewList.ItemsSource = _viewModel.MarketOverview;
        InsightsList.ItemsSource = _viewModel.Insights;
        AlertsList.ItemsSource = _viewModel.Alerts;
    }

    private static string FormatCurrency(decimal value)
    {
        return value >= 0
            ? $"${value:N2}"
            : $"-${Math.Abs(value):N2}";
    }
}