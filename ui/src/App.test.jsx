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
  ],
  insights: [
    { title: 'Top buy opportunity', summary: 'NVDA stands out.', type: 'buy' },
  ],
  alerts: [
    { title: 'Trade executed', message: 'Test order filled.', type: 'trade', timeUtc: '2026-01-01T00:00:00Z' },
  ],
  growthCurve: [
    { label: 'Jan', value: 100000 },
    { label: 'Feb', value: 110000 },
  ],
  allocation: [
    { name: 'Technology', value: 60, color: '#7c3aed' },
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

  it('renders portfolio summary and key trading views', async () => {
    render(<App />);

    expect(await screen.findByText(/Portfolio Summary/i)).toBeInTheDocument();
    expect(screen.getByText(/Open Positions/i)).toBeInTheDocument();
    expect(screen.getByText(/Watchlist/i)).toBeInTheDocument();

    const positionsSection = screen.getByText(/Open Positions/i).closest('section');
    expect(positionsSection).not.toBeNull();
    expect(within(positionsSection).getByText('NVDA')).toBeInTheDocument();

    const watchlistSection = screen.getByText(/Watchlist/i).closest('section');
    expect(watchlistSection).not.toBeNull();
    expect(within(watchlistSection).getByText('Strong Buy')).toBeInTheDocument();
  });
});
