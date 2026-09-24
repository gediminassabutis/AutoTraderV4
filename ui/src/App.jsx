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
    totalPortfolioValue: 0,
    dailyPnL: 0,
    weeklyPnL: 0,
    monthlyPnL: 0,
    availableCash: 0,
    totalExposurePercent: 0,
  },
  positions: [],
  watchlist: [],
  marketOverview: [],
  insights: [],
  alerts: [],
  growthCurve: [],
  allocation: [],
};

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

  const { summary, positions, watchlist, marketOverview, insights, alerts, growthCurve, allocation } = dashboard;

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
            <span>Total Exposure</span>
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
            {marketOverview.map((market) => (
              <div className="market-card" key={market.symbol}>
                <span>{market.label}</span>
                <strong>{currencyFormatter.format(market.value)}</strong>
                <small className={market.trend === 'up' ? 'positive' : 'negative'}>
                  {market.trend === 'up' ? '+' : ''}{market.change}%
                </small>
              </div>
            ))}
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
                  <linearGradient id="colorValue" x1="0" x2="0" y1="0" y2="1">
                    <stop offset="5%" stopColor="#7c3aed" stopOpacity={0.8} />
                    <stop offset="95%" stopColor="#7c3aed" stopOpacity={0.1} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="label" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Area dataKey="value" stroke="#7c3aed" fill="url(#colorValue)" />
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
              <LineChart data={growthCurve}>
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
            <h3>Sector Allocation</h3>
          </div>
          <div className="chart-box">
            <ResponsiveContainer width="100%" height={220}>
              <BarChart data={allocation}>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="name" stroke="#cbd5e1" />
                <YAxis stroke="#cbd5e1" />
                <Tooltip />
                <Bar dataKey="value" radius={[4, 4, 0, 0]}>
                  {allocation.map((entry) => (
                    <Cell key={entry.name} fill={entry.color} />
                  ))}
                </Bar>
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
