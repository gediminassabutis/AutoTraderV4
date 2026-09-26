import { render, screen, within } from '@testing-library/react';
import App from './App.jsx';

const mockDashboard = {
  summary: {
    totalPortfolioValue: 200000,
    dailyPnL: 1200,
    weeklyPnL: 3500,
    monthlyPnL: 9000,
    availableCash: 40000,
    totalExposurePercent: 80,
  },
  positions: [
    {
      symbol: 'NVDA',
      quantity: 32,
      entryPrice: 118.5,
      currentPrice: 132.4,
      unrealizedPnl: 445,
      stopLoss: 120,
      takeProfit: 150,
      confidence: 92,
    },
  ],
  watchlist: [
    {
      symbol: 'NVDA',
      rating: 'Strong Buy',
      forecastReturn: 12.5,
      riskScore: 22,
      confidence: 92,
    },
  ],
  marketOverview: [
    { symbol: 'SPX', label: 'S&P 500', value: 5578.6, change: 0.88, trend: 'up' },
    { symbol: 'IXIC', label: 'Nasdaq', value: 18242.1, change: 1.14, trend: 'up' },
    { symbol: 'DJI', label: 'Dow Jones', value: 39446.7, change: 0.54, trend: 'up' },
    { symbol: 'FTSE', label: 'FTSE 100', value: 8334.2, change: -0.16, trend: 'down' },
    { symbol: 'VIX', label: 'VIX', value: 18.42, change: -2.31, trend: 'down' },
  ],
  insights: [
    { title: 'Top Buy Opportunity', summary: 'NVDA stands out.', type: 'buy' },
    { title: 'Top Sell Opportunity', summary: 'Cash is preferred for hedging.', type: 'sell' },
    { title: 'Emerging Risks', summary: 'Rates remain a concern.', type: 'risk' },
    { title: 'Sector Rotation Analysis', summary: 'Technology remains ahead.', type: 'macro' },
    { title: 'Macro Outlook', summary: 'Growth remains constructive.', type: 'macro' },
  ],
  alerts: [
    { title: 'Trade Executed', message: 'Test order filled.', type: 'trade', timeUtc: '2026-01-01T00:00:00Z' },
    { title: 'Risk Warning', message: 'Exposure rising.', type: 'risk', timeUtc: '2026-01-01T00:10:00Z' },
    { title: 'Portfolio Rebalanced', message: 'Cash repositioned.', type: 'info', timeUtc: '2026-01-01T00:20:00Z' },
  ],
  growthCurve: [
    { label: 'Jan', value: 100000 },
    { label: 'Feb', value: 110000 },
  ],
  equityCurve: [
    { label: 'Jan', value: 96000 },
    { label: 'Feb', value: 108000 },
  ],
  drawdownHistory: [
    { label: 'Jan', value: 0 },
    { label: 'Feb', value: 2.4 },
  ],
  allocation: [
    { name: 'Technology', value: 60, color: '#7c3aed' },
    { name: 'Cash', value: 40, color: '#cbd5e1' },
  ],
  sectorAllocation: [
    { name: 'Technology', value: 65, color: '#7c3aed' },
    { name: 'Defensive', value: 35, color: '#60a5fa' },
  ],
  monthlyReturns: [
    { label: 'Jan', value: 1.2 },
    { label: 'Feb', value: 2.7 },
  ],
  forecastProjections: [
    { symbol: 'NVDA', horizon: '1D', projectedPrice: 133.4, projectedReturnPercent: 3.1 },
    { symbol: 'NVDA', horizon: '5D', projectedPrice: 137.8, projectedReturnPercent: 5.4 },
  ],
};

describe('App', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => mockDashboard,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the AGENTS dashboard sections and required content', async () => {
    render(<App />);

    expect(await screen.findByText(/Portfolio Summary/i)).toBeInTheDocument();
    expect(screen.getByText('Total Portfolio Value')).toBeInTheDocument();
    expect(screen.getByText('Daily P&L')).toBeInTheDocument();
    expect(screen.getByText('Weekly P&L')).toBeInTheDocument();
    expect(screen.getByText('Monthly P&L')).toBeInTheDocument();
    expect(screen.getByText('Available Cash')).toBeInTheDocument();
    expect(screen.getByText('Total Exposure %')).toBeInTheDocument();

    expect(screen.getByText(/Open Positions/i)).toBeInTheDocument();
    expect(screen.getByText(/Market Overview/i)).toBeInTheDocument();
    expect(screen.getByText(/Watchlist/i)).toBeInTheDocument();
    expect(screen.getByText(/AI Insights/i)).toBeInTheDocument();
    expect(screen.getByText(/Portfolio Growth Curve/i)).toBeInTheDocument();
    expect(screen.getByText(/Equity Curve/i)).toBeInTheDocument();
    expect(screen.getByText(/Asset Allocation/i)).toBeInTheDocument();
    expect(screen.getByText(/Drawdown History/i)).toBeInTheDocument();
    expect(screen.getByText(/Sector Allocation/i)).toBeInTheDocument();
    expect(screen.getByText(/Forecast Projections/i)).toBeInTheDocument();
    expect(screen.getByText(/Monthly Returns/i)).toBeInTheDocument();
    expect(screen.getByText(/Alerts/i)).toBeInTheDocument();

    expect(screen.getByText('S&P 500')).toBeInTheDocument();
    expect(screen.getByText('Nasdaq')).toBeInTheDocument();
    expect(screen.getByText('Dow Jones')).toBeInTheDocument();
    expect(screen.getByText('FTSE 100')).toBeInTheDocument();
    expect(screen.getByText('VIX')).toBeInTheDocument();

    const positionsSection = screen.getByText(/Open Positions/i).closest('section');
    expect(positionsSection).not.toBeNull();
    expect(within(positionsSection).getByText('NVDA')).toBeInTheDocument();

    const watchlistSection = screen.getByText(/Watchlist/i).closest('section');
    expect(watchlistSection).not.toBeNull();
    expect(within(watchlistSection).getByText('Strong Buy')).toBeInTheDocument();

    expect(screen.getByText('Top Buy Opportunity')).toBeInTheDocument();
    expect(screen.getByText('Top Sell Opportunity')).toBeInTheDocument();
    expect(screen.getByText('Emerging Risks')).toBeInTheDocument();
    expect(screen.getByText('Sector Rotation Analysis')).toBeInTheDocument();
    expect(screen.getByText('Macro Outlook')).toBeInTheDocument();

    expect(screen.getByText('Trade Executed')).toBeInTheDocument();
    expect(screen.getByText('Risk Warning')).toBeInTheDocument();
    expect(screen.getByText('Portfolio Rebalanced')).toBeInTheDocument();
  });
});
