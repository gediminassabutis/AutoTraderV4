import { useEffect, useState } from 'react';
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';

const currencyFormatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  maximumFractionDigits: 0,
});

const percentFormatter = new Intl.NumberFormat('en-US', {
  style: 'percent',
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const defaultDashboard = {
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
    {
      symbol: 'MSFT',
      quantity: 14,
      entryPrice: 412.2,
      currentPrice: 428.8,
      unrealizedPnl: 232,
      stopLoss: 406,
      takeProfit: 450,
      confidence: 88,
    },
  ],
  watchlist: [
    { symbol: 'NVDA', rating: 'Strong Buy', forecastReturn: 12.5, riskScore: 22, confidence: 92 },
    { symbol: 'MSFT', rating: 'Buy', forecastReturn: 9.2, riskScore: 28, confidence: 88 },
    { symbol: 'AMD', rating: 'Buy', forecastReturn: 11.8, riskScore: 31, confidence: 84 },
  ],
  marketOverview: [
    { symbol: 'SPX', label: 'S&P 500', value: 5578.6, change: 0.88, trend: 'up' },
    { symbol: 'IXIC', label: 'Nasdaq', value: 18242.1, change: 1.14, trend: 'up' },
    { symbol: 'DJI', label: 'Dow Jones', value: 39446.7, change: 0.54, trend: 'up' },
    { symbol: 'FTSE', label: 'FTSE 100', value: 8334.2, change: -0.16, trend: 'down' },
    { symbol: 'VIX', label: 'VIX', value: 18.42, change: -2.31, trend: 'down' },
  ],
  insights: [
    { title: 'Top Buy Opportunity', summary: 'NVDA remains the highest-conviction setup with strong earnings momentum and clean trend support.', type: 'buy' },
    { title: 'Top Sell Opportunity', summary: 'Defensive sectors are lagging and could offer short-side opportunities if breadth weakens.', type: 'sell' },
    { title: 'Emerging Risks', summary: 'Rate sensitivity is elevated while volatility remains above the long-run mean.', type: 'risk' },
    { title: 'Sector Rotation Analysis', summary: 'Technology continues to lead whereas cyclical energy is fading into the broader trend.', type: 'macro' },
    { title: 'Macro Outlook', summary: 'The macro backdrop remains constructive for quality growth despite tighter liquidity.', type: 'macro' },
  ],
  alerts: [
    { title: 'Trade Executed', message: 'NVDA position was filled at the latest risk-approved entry.', type: 'trade', timeUtc: '2026-01-01T09:15:00Z' },
    { title: 'Stop Loss Triggered', message: 'AAPL stopped out after a sharp move below the risk threshold.', type: 'risk', timeUtc: '2026-01-01T09:30:00Z' },
    { title: 'Take Profit Reached', message: 'MSFT reached the target zone and was closed at the model price.', type: 'trade', timeUtc: '2026-01-01T10:00:00Z' },
    { title: 'Risk Warning', message: 'Portfolio exposure is approaching the maximum allowed limit.', type: 'risk', timeUtc: '2026-01-01T10:30:00Z' },
    { title: 'Portfolio Rebalanced', message: 'Cash was added to reduce exposure after the latest rotation.', type: 'info', timeUtc: '2026-01-01T11:00:00Z' },
    { title: 'API/Data Source Issues', message: 'A market feed latency alert was raised for a short delay window.', type: 'info', timeUtc: '2026-01-01T11:30:00Z' },
  ],
  growthCurve: [
    { label: 'Jan', value: 100000 },
    { label: 'Feb', value: 110500 },
    { label: 'Mar', value: 117000 },
    { label: 'Apr', value: 126300 },
    { label: 'May', value: 131900 },
    { label: 'Jun', value: 140200 },
  ],
  equityCurve: [
    { label: 'Jan', value: 100000 },
    { label: 'Feb', value: 106500 },
    { label: 'Mar', value: 112200 },
    { label: 'Apr', value: 121500 },
    { label: 'May', value: 128750 },
    { label: 'Jun', value: 136400 },
  ],
  drawdownHistory: [
    { label: 'Jan', value: 0 },
    { label: 'Feb', value: 3.2 },
    { label: 'Mar', value: 1.8 },
    { label: 'Apr', value: 5.4 },
    { label: 'May', value: 2.1 },
    { label: 'Jun', value: 4.0 },
  ],
  allocation: [
    { name: 'Technology', value: 42, color: '#8b5cf6' },
    { name: 'Healthcare', value: 18, color: '#22c55e' },
    { name: 'Financials', value: 15, color: '#f59e0b' },
    { name: 'Industrials', value: 13, color: '#38bdf8' },
    { name: 'Cash', value: 12, color: '#cbd5e1' },
  ],
  sectorAllocation: [
    { name: 'Tech', value: 48, color: '#8b5cf6' },
    { name: 'Healthcare', value: 17, color: '#22c55e' },
    { name: 'Finance', value: 14, color: '#f59e0b' },
    { name: 'Energy', value: 11, color: '#ef4444' },
    { name: 'Defensive', value: 10, color: '#60a5fa' },
  ],
  monthlyReturns: [
    { label: 'Jan', value: 1.4 },
    { label: 'Feb', value: 2.3 },
    { label: 'Mar', value: 1.8 },
    { label: 'Apr', value: 3.4 },
    { label: 'May', value: 2.9 },
    { label: 'Jun', value: 4.1 },
  ],
  forecastProjections: [
    { symbol: 'NVDA', horizon: '1D', projectedPrice: 133.4, projectedReturnPercent: 3.1 },
    { symbol: 'NVDA', horizon: '5D', projectedPrice: 137.8, projectedReturnPercent: 5.4 },
    { symbol: 'NVDA', horizon: '30D', projectedPrice: 145.1, projectedReturnPercent: 10.2 },
    { symbol: 'NVDA', horizon: '90D', projectedPrice: 158.4, projectedReturnPercent: 18.6 },
  ],
};

function formatMarketValue(market) {
  const changeValue = Number.isFinite(market.change ?? market.changePercent) ? market.change ?? market.changePercent : 0;
  return market.symbol === 'VIX' ? `${Number(market.value).toFixed(2)}` : Number(market.value).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

export default function App() {
  const [dashboard, setDashboard] = useState(defaultDashboard);
  const [error, setError] = useState('');

  useEffect(() => {
    const loadDashboard = async () => {
      try {
        const response = await fetch('/api/dashboard');
        if (!response.ok) {
          throw new Error(`Request failed with status ${response.status}`);
        }

        const data = await response.json();
        setDashboard(data);
      } catch (loadError) {
        setError('Unable to load dashboard data from the backend.');
        console.error(loadError);
      }
    };

    loadDashboard();
  }, []);

  if (error) {
    return <div className="error-banner">{error}</div>;
  }

  const {
    summary,
    positions,
    watchlist,
    marketOverview,
    insights,
    alerts,
    growthCurve,
    equityCurve,
    drawdownHistory,
    allocation,
    sectorAllocation,
    monthlyReturns,
    forecastProjections,
  } = dashboard;

  return (
    <div className="app-shell">
      <header className="topbar">
        <div>
          <p className="eyebrow">AutoTraderV4</p>
          <h1>Portfolio intelligence dashboard</h1>
        </div>
        <div className="status-badge">System online</div>
      </header>

      <section className="summary-block">
        <h2>Portfolio Summary</h2>
        <div className="summary-grid">
          <article className="metric-card highlight">
            <span>Total Portfolio Value</span>
            <strong>{currencyFormatter.format(summary.totalPortfolioValue)}</strong>
          </article>
          <article className="metric-card">
            <span>Daily P&amp;L</span>
            <strong>{currencyFormatter.format(summary.dailyPnL)}</strong>
          </article>
          <article className="metric-card">
            <span>Weekly P&amp;L</span>
            <strong>{currencyFormatter.format(summary.weeklyPnL)}</strong>
          </article>
          <article className="metric-card">
            <span>Monthly P&amp;L</span>
            <strong>{currencyFormatter.format(summary.monthlyPnL)}</strong>
          </article>
          <article className="metric-card">
            <span>Available Cash</span>
            <strong>{currencyFormatter.format(summary.availableCash)}</strong>
          </article>
          <article className="metric-card">
            <span>Total Exposure %</span>
            <strong>{percentFormatter.format(summary.totalExposurePercent / 100)}</strong>
          </article>
        </div>
      </section>

      <div className="content-grid">
        <section className="panel large-panel">
          <div className="panel-header">
            <h3>Open Positions</h3>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Quantity</th>
                  <th>Entry Price</th>
                  <th>Current Price</th>
                  <th>Unrealised P/L</th>
                  <th>Stop Loss</th>
                  <th>Take Profit</th>
                  <th>Confidence</th>
                </tr>
              </thead>
              <tbody>
                {positions.map((position) => (
                  <tr key={position.symbol}>
                    <td>{position.symbol}</td>
                    <td>{position.quantity}</td>
                    <td>{currencyFormatter.format(position.entryPrice)}</td>
                    <td>{currencyFormatter.format(position.currentPrice)}</td>
                    <td>{currencyFormatter.format(position.unrealizedPnl)}</td>
                    <td>{currencyFormatter.format(position.stopLoss)}</td>
                    <td>{currencyFormatter.format(position.takeProfit)}</td>
                    <td>{position.confidence}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <section className="panel">
          <div className="panel-header">
            <h3>Market Overview</h3>
          </div>
          <div className="market-grid">
            {marketOverview.map((market) => {
              const changeValue = Number.isFinite(market.change ?? market.changePercent) ? (market.change ?? market.changePercent) : 0;
              const trend = market.trend || (changeValue >= 0 ? 'up' : 'down');

              return (
                <div className="market-card" key={market.symbol}>
                  <span>{market.label}</span>
                  <strong>{formatMarketValue(market)}</strong>
                  <small className={trend === 'up' ? 'positive' : 'negative'}>
                    {trend === 'up' ? '+' : ''}{changeValue.toFixed(2)}%
                  </small>
                </div>
              );
            })}
          </div>
        </section>

        <section className="panel large-panel">
          <div className="panel-header">
            <h3>Watchlist</h3>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Rating</th>
                  <th>Forecasted Return</th>
                  <th>Risk Score</th>
                  <th>Confidence</th>
                </tr>
              </thead>
              <tbody>
                {watchlist.map((opportunity) => (
                  <tr key={opportunity.symbol}>
                    <td>{opportunity.symbol}</td>
                    <td>{opportunity.rating}</td>
                    <td>{opportunity.forecastReturn}%</td>
                    <td>{opportunity.riskScore}</td>
                    <td>{opportunity.confidence}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <section className="panel">
          <div className="panel-header">
            <h3>AI Insights</h3>
          </div>
          <div className="insights-list">
            {insights.map((insight) => (
              <article key={insight.title} className="insight-card">
                <span className={`tag ${insight.type}`}>{insight.type}</span>
                <h4>{insight.title}</h4>
                <p>{insight.summary}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="panel chart-panel">
          <div className="panel-header">
            <h3>Portfolio Growth Curve</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={260}>
              <AreaChart data={growthCurve}>
                <defs>
                  <linearGradient id="growthGradient" x1="0" x2="0" y1="0" y2="1">
                    <stop offset="5%" stopColor="#7c3aed" stopOpacity={0.8} />
                    <stop offset="95%" stopColor="#7c3aed" stopOpacity={0.1} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="label" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Area dataKey="value" stroke="#7c3aed" fill="url(#growthGradient)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel chart-panel">
          <div className="panel-header">
            <h3>Asset Allocation</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={260}>
              <PieChart>
                <Pie data={allocation} dataKey="value" nameKey="name" innerRadius={50} outerRadius={90} paddingAngle={3}>
                  {allocation.map((entry) => (
                    <Cell key={entry.name} fill={entry.color} />
                  ))}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel chart-panel">
          <div className="panel-header">
            <h3>Equity Curve</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={220}>
              <LineChart data={equityCurve || growthCurve}>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="label" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Line type="monotone" dataKey="value" stroke="#22c55e" strokeWidth={3} dot={{ r: 3 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel chart-panel">
          <div className="panel-header">
            <h3>Drawdown History</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={220}>
              <AreaChart data={drawdownHistory}>
                <defs>
                  <linearGradient id="drawdownGradient" x1="0" x2="0" y1="0" y2="1">
                    <stop offset="5%" stopColor="#ef4444" stopOpacity={0.8} />
                    <stop offset="95%" stopColor="#ef4444" stopOpacity={0.1} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="label" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Area dataKey="value" stroke="#ef4444" fill="url(#drawdownGradient)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel chart-panel">
          <div className="panel-header">
            <h3>Sector Allocation</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={220}>
              <BarChart data={sectorAllocation || allocation}>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="name" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Bar dataKey="value" radius={[4, 4, 0, 0]}>
                  {(sectorAllocation || allocation).map((entry) => (
                    <Cell key={entry.name} fill={entry.color} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel chart-panel">
          <div className="panel-header">
            <h3>Forecast Projections</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={240}>
              <LineChart data={forecastProjections}>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="horizon" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Line type="monotone" dataKey="projectedReturnPercent" stroke="#38bdf8" strokeWidth={3} dot={{ r: 3 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel chart-panel">
          <div className="panel-header">
            <h3>Monthly Returns</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={220}>
              <BarChart data={monthlyReturns}>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="label" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Bar dataKey="value" fill="#22c55e" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>

        <section className="panel alerts-panel">
          <div className="panel-header">
            <h3>Alerts</h3>
          </div>
          <ul className="alerts-list">
            {alerts.map((alert) => (
              <li key={`${alert.title}-${alert.timeUtc}`} className={`alert-item ${alert.type}`}>
                <div>
                  <strong>{alert.title}</strong>
                  <p>{alert.message}</p>
                </div>
                <span>{new Date(alert.timeUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </div>
  );
}
