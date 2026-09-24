import { useCallback, useEffect, useMemo, useState } from "react";
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type {
  DashboardAlert,
  DashboardInsight,
  ForecastProjection,
  PortfolioAllocation,
  PortfolioChartPoint,
  PortfolioDashboard,
  PortfolioPositionView,
  PortfolioSummary,
  WatchlistOpportunity,
} from "./types";

const refreshIntervalMs = 60_000;
const chartLineColors = ["#8b5cf6", "#06b6d4", "#22c55e", "#f59e0b", "#ef4444"];

function App() {
  const { data, error, isLoading, isRefreshing, reload } = useDashboard();

  return (
    <div className="app-shell">
      <div className="background-glow background-glow-left" />
      <div className="background-glow background-glow-right" />
      <main className="dashboard-page">
        <Hero summary={data?.summary} isRefreshing={isRefreshing} onRefresh={reload} />

        {error ? (
          <section className="banner banner-error">
            <div>
              <strong>Dashboard data unavailable.</strong>
              <p>{error}</p>
            </div>
            <button className="secondary-button" onClick={reload}>
              Retry load
            </button>
          </section>
        ) : null}

        {isLoading && !data ? <LoadingGrid /> : null}

        {data ? (
          <>
            <section className="stats-grid">
              <StatCard label="Portfolio value" value={formatCurrency(data.summary.totalPortfolioValue, data.summary.currency)} />
              <StatCard label="Available cash" value={formatCurrency(data.summary.availableCash, data.summary.currency)} />
              <StatCard label="Daily P&L" value={formatSignedCurrency(data.summary.dailyPnL, data.summary.currency)} tone={getValueTone(data.summary.dailyPnL)} />
              <StatCard label="Weekly P&L" value={formatSignedCurrency(data.summary.weeklyPnL, data.summary.currency)} tone={getValueTone(data.summary.weeklyPnL)} />
              <StatCard label="Monthly P&L" value={formatSignedCurrency(data.summary.monthlyPnL, data.summary.currency)} tone={getValueTone(data.summary.monthlyPnL)} />
              <StatCard label="Exposure" value={formatPercent(data.summary.totalExposurePercent)} tone={data.summary.totalExposurePercent >= 90 ? "warning" : "neutral"} />
            </section>

            <section className="market-strip panel">
              <div className="panel-header">
                <div>
                  <h2>Market overview</h2>
                  <p>Major benchmarks and volatility context.</p>
                </div>
              </div>
              <div className="market-grid">
                {data.marketOverview.length > 0 ? (
                  data.marketOverview.map((item) => (
                    <article className="market-card" key={item.label}>
                      <div className="market-card-header">
                        <span>{item.label}</span>
                        <span className={`pill ${item.changePercent >= 0 ? "pill-positive" : "pill-negative"}`}>
                          {item.changePercent >= 0 ? "+" : ""}
                          {item.changePercent.toFixed(2)}%
                        </span>
                      </div>
                      <strong>{formatCompactNumber(item.value)}</strong>
                      <small>Updated {formatRelative(item.lastUpdatedUtc)}</small>
                    </article>
                  ))
                ) : (
                  <EmptyState title="Awaiting market snapshots" description="The market overview will populate when benchmark snapshots are written to the backend." />
                )}
              </div>
            </section>

            <div className="dashboard-grid">
              <Panel
                title="Open positions"
                subtitle="Live allocation, pricing, and risk posture for current holdings."
                className="panel-span-2"
              >
                <PositionsTable positions={data.positions} currency={data.summary.currency} />
              </Panel>

              <Panel title="AI insights" subtitle="Explainable recommendations and risk narratives.">
                <InsightsPanel insights={data.insights} />
              </Panel>

              <Panel title="Watchlist" subtitle="Top-ranked opportunities ordered by confidence." className="panel-span-2">
                <WatchlistTable watchlist={data.watchlist} currency={data.summary.currency} />
              </Panel>

              <Panel title="Alerts" subtitle="Operational and risk events from the latest backend state.">
                <AlertsFeed alerts={data.alerts} />
              </Panel>

              <Panel title="Portfolio growth" subtitle="Longer-term growth curve for the tracked holdings.">
                <AreaChartCard
                  data={data.growthCurve}
                  valueFormatter={(value) => formatCurrency(value, data.summary.currency)}
                  stroke="#8b5cf6"
                  fill="url(#growthGradient)"
                  gradientId="growthGradient"
                />
              </Panel>

              <Panel title="Equity curve" subtitle="Shorter-horizon view of portfolio mark-to-market value.">
                <AreaChartCard
                  data={data.equityCurve}
                  valueFormatter={(value) => formatCurrency(value, data.summary.currency)}
                  stroke="#06b6d4"
                  fill="url(#equityGradient)"
                  gradientId="equityGradient"
                />
              </Panel>

              <Panel title="Drawdown history" subtitle="Peak-to-trough pressure across the current history window.">
                <AreaChartCard
                  data={data.drawdownHistory}
                  valueFormatter={(value) => formatPercent(value)}
                  stroke="#ef4444"
                  fill="url(#drawdownGradient)"
                  gradientId="drawdownGradient"
                />
              </Panel>

              <Panel title="Asset allocation" subtitle="Position concentration by symbol.">
                <AllocationChart allocations={data.allocation} />
              </Panel>

              <Panel title="Sector allocation" subtitle="Exposure by inferred sector bucket.">
                <AllocationChart allocations={data.sectorAllocation} />
              </Panel>

              <Panel title="Forecast projections" subtitle="Modeled price path by horizon for top opportunities." className="panel-span-2">
                <ForecastChart projections={data.forecastProjections} currency={data.summary.currency} />
              </Panel>

              <Panel title="Monthly returns" subtitle="Month-end performance snapshots from the recorded history.">
                <MonthlyReturnsChart returns={data.monthlyReturns} />
              </Panel>
            </div>
          </>
        ) : null}
      </main>
    </div>
  );
}

function useDashboard() {
  const [data, setData] = useState<PortfolioDashboard | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);

  const load = useCallback(async (backgroundRefresh = false) => {
    if (backgroundRefresh) {
      setIsRefreshing(true);
    } else {
      setIsLoading(true);
    }

    try {
      const response = await fetch("/api/dashboard", {
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(`Backend responded with ${response.status}.`);
      }

      const payload = (await response.json()) as PortfolioDashboard;
      setData(payload);
      setError(null);
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Unknown error";
      setError(message);
    } finally {
      setIsLoading(false);
      setIsRefreshing(false);
    }
  }, []);

  useEffect(() => {
    void load();

    const timer = window.setInterval(() => {
      void load(true);
    }, refreshIntervalMs);

    return () => {
      window.clearInterval(timer);
    };
  }, [load]);

  return {
    data,
    error,
    isLoading,
    isRefreshing,
    reload: () => {
      void load(true);
    },
  };
}

function Hero({
  summary,
  isRefreshing,
  onRefresh,
}: {
  summary?: PortfolioSummary;
  isRefreshing: boolean;
  onRefresh: () => void;
}) {
  return (
    <section className="hero panel">
      <div>
        <div className="eyebrow">AutoTraderV4</div>
        <h1>Portfolio intelligence dashboard</h1>
        <p>
          Monitor portfolio health, benchmark context, AI-ranked opportunities, and
          risk alerts from one responsive trading workspace.
        </p>
        <div className="hero-meta">
          <span className={`pill ${summary?.defensiveMode ? "pill-warning" : "pill-positive"}`}>
            {summary?.defensiveMode ? "Defensive mode active" : "Risk posture normal"}
          </span>
          {summary ? <span>Last updated {formatRelative(summary.lastUpdatedUtc)}</span> : null}
        </div>
      </div>
      <div className="hero-actions">
        <button className="primary-button" onClick={onRefresh} disabled={isRefreshing}>
          {isRefreshing ? "Refreshing..." : "Refresh dashboard"}
        </button>
      </div>
    </section>
  );
}

function Panel({
  title,
  subtitle,
  className,
  children,
}: {
  title: string;
  subtitle: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <section className={`panel ${className ?? ""}`.trim()}>
      <div className="panel-header">
        <div>
          <h2>{title}</h2>
          <p>{subtitle}</p>
        </div>
      </div>
      {children}
    </section>
  );
}

function StatCard({
  label,
  value,
  tone = "neutral",
}: {
  label: string;
  value: string;
  tone?: "neutral" | "positive" | "negative" | "warning";
}) {
  return (
    <article className={`stat-card stat-card-${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  );
}

function PositionsTable({
  positions,
  currency,
}: {
  positions: PortfolioPositionView[];
  currency: string;
}) {
  if (positions.length === 0) {
    return (
      <EmptyState
        title="No positions yet"
        description="Once holdings are written to the backend repository, they will appear here with pricing, P&L, and stop guidance."
      />
    );
  }

  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            <th>Symbol</th>
            <th>Sector</th>
            <th>Qty</th>
            <th>Entry</th>
            <th>Current</th>
            <th>Unrealized P&L</th>
            <th>Stop</th>
            <th>Target</th>
            <th>Confidence</th>
          </tr>
        </thead>
        <tbody>
          {positions.map((position) => (
            <tr key={position.symbol}>
              <td>
                <div className="primary-cell">
                  <strong>{position.symbol}</strong>
                  <small>{position.allocationPercent.toFixed(1)}% allocation</small>
                </div>
              </td>
              <td>{position.sector}</td>
              <td>{formatQuantity(position.quantity)}</td>
              <td>{formatCurrency(position.entryPrice, currency)}</td>
              <td>{formatCurrency(position.currentPrice, currency)}</td>
              <td className={getToneClass(position.unrealizedPnl)}>
                {formatSignedCurrency(position.unrealizedPnl, currency)}
              </td>
              <td>{formatOptionalCurrency(position.stopLoss, currency)}</td>
              <td>{formatOptionalCurrency(position.takeProfit, currency)}</td>
              <td>{position.confidence ? `${position.confidence}%` : "--"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function WatchlistTable({
  watchlist,
  currency,
}: {
  watchlist: WatchlistOpportunity[];
  currency: string;
}) {
  if (watchlist.length === 0) {
    return (
      <EmptyState
        title="Watchlist not ranked yet"
        description="Strategy signals will automatically populate the top opportunities table after evaluation requests are stored."
      />
    );
  }

  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          <tr>
            <th>Symbol</th>
            <th>Rating</th>
            <th>Strategy</th>
            <th>Forecast return</th>
            <th>Risk score</th>
            <th>Confidence</th>
            <th>Price</th>
            <th>Top factors</th>
          </tr>
        </thead>
        <tbody>
          {watchlist.map((item) => (
            <tr key={item.symbol}>
              <td>
                <div className="primary-cell">
                  <strong>{item.symbol}</strong>
                  <small>{item.sector}</small>
                </div>
              </td>
              <td>{item.rating}</td>
              <td>{item.strategy}</td>
              <td className={getToneClass(item.forecastReturn)}>{formatSignedPercent(item.forecastReturn)}</td>
              <td>{item.riskScore.toFixed(1)}</td>
              <td>{item.confidence}%</td>
              <td>{formatCurrency(item.price, currency)}</td>
              <td>{item.topFactors.join(", ")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function InsightsPanel({ insights }: { insights: DashboardInsight[] }) {
  if (insights.length === 0) {
    return (
      <EmptyState
        title="No insights available"
        description="Insights will appear after the backend has enough watchlist and market context to explain opportunities."
      />
    );
  }

  return (
    <div className="stack-list">
      {insights.map((insight) => (
        <article className="insight-card" key={insight.title}>
          <div className={`pill ${getInsightPillClass(insight.type)}`}>{insight.title}</div>
          <p>{insight.summary}</p>
          {insight.symbol ? <small>Focus symbol: {insight.symbol}</small> : null}
        </article>
      ))}
    </div>
  );
}

function AlertsFeed({ alerts }: { alerts: DashboardAlert[] }) {
  if (alerts.length === 0) {
    return (
      <EmptyState
        title="No active alerts"
        description="Risk, data, and signal alerts will stream in here as the backend records new events."
      />
    );
  }

  return (
    <div className="stack-list">
      {alerts.map((alert) => (
        <article className="alert-card" key={`${alert.title}-${alert.timeUtc}`}>
          <div className="alert-header">
            <span className={`pill ${getAlertPillClass(alert.severity)}`}>{alert.title}</span>
            <small>{formatRelative(alert.timeUtc)}</small>
          </div>
          <p>{alert.message}</p>
        </article>
      ))}
    </div>
  );
}

function AreaChartCard({
  data,
  valueFormatter,
  stroke,
  fill,
  gradientId,
}: {
  data: PortfolioChartPoint[];
  valueFormatter: (value: number) => string;
  stroke: string;
  fill: string;
  gradientId: string;
}) {
  if (data.length === 0) {
    return <EmptyState title="Chart pending data" description="This chart will populate after the backend records enough history." />;
  }

  return (
    <div className="chart-frame">
      <ResponsiveContainer width="100%" height={260}>
        <AreaChart data={data}>
          <defs>
            <linearGradient id={gradientId} x1="0" x2="0" y1="0" y2="1">
              <stop offset="5%" stopColor={stroke} stopOpacity={0.35} />
              <stop offset="95%" stopColor={stroke} stopOpacity={0.03} />
            </linearGradient>
          </defs>
          <CartesianGrid stroke="rgba(148, 163, 184, 0.12)" vertical={false} />
          <XAxis dataKey="label" stroke="#94a3b8" tickLine={false} axisLine={false} />
          <YAxis stroke="#94a3b8" tickLine={false} axisLine={false} width={90} tickFormatter={(value) => compactAxisValue(value)} />
          <Tooltip formatter={(value: number) => valueFormatter(value)} />
          <Area type="monotone" dataKey="value" stroke={stroke} fill={fill} strokeWidth={2.5} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}

function AllocationChart({ allocations }: { allocations: PortfolioAllocation[] }) {
  if (allocations.length === 0) {
    return <EmptyState title="Allocation unavailable" description="Holdings are needed before allocation can be visualized." />;
  }

  return (
    <div className="chart-frame">
      <ResponsiveContainer width="100%" height={260}>
        <PieChart>
          <Tooltip formatter={(value: number) => `${value.toFixed(1)}%`} />
          <Pie data={allocations} dataKey="value" nameKey="name" innerRadius={52} outerRadius={86} paddingAngle={2}>
            {allocations.map((entry) => (
              <Cell key={entry.name} fill={entry.color} />
            ))}
          </Pie>
          <Legend verticalAlign="bottom" align="center" />
        </PieChart>
      </ResponsiveContainer>
    </div>
  );
}

function ForecastChart({
  projections,
  currency,
}: {
  projections: ForecastProjection[];
  currency: string;
}) {
  const chartData = useMemo(() => {
    const rows = new Map<string, Record<string, number | string>>();

    for (const projection of projections) {
      const row = rows.get(projection.horizon) ?? { horizon: projection.horizon };
      row[projection.symbol] = projection.projectedPrice;
      rows.set(projection.horizon, row);
    }

    return Array.from(rows.values());
  }, [projections]);

  const symbols = useMemo(
    () => Array.from(new Set(projections.map((projection) => projection.symbol))),
    [projections]
  );

  if (projections.length === 0) {
    return <EmptyState title="No forecast projections yet" description="Forecast curves are generated from ranked watchlist opportunities." />;
  }

  return (
    <div className="chart-frame">
      <ResponsiveContainer width="100%" height={320}>
        <LineChart data={chartData}>
          <CartesianGrid stroke="rgba(148, 163, 184, 0.12)" vertical={false} />
          <XAxis dataKey="horizon" stroke="#94a3b8" tickLine={false} axisLine={false} />
          <YAxis stroke="#94a3b8" tickLine={false} axisLine={false} width={90} tickFormatter={(value) => compactAxisValue(value)} />
          <Tooltip formatter={(value: number) => formatCurrency(value, currency)} />
          <Legend />
          {symbols.map((symbol, index) => (
            <Line
              key={symbol}
              type="monotone"
              dataKey={symbol}
              stroke={chartLineColors[index % chartLineColors.length]}
              strokeWidth={2.5}
              dot={{ r: 3 }}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function MonthlyReturnsChart({ returns }: { returns: PortfolioChartPoint[] }) {
  if (returns.length === 0) {
    return <EmptyState title="Monthly returns pending" description="Month-end returns will appear once historical data spans multiple dates." />;
  }

  return (
    <div className="chart-frame">
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={returns}>
          <CartesianGrid stroke="rgba(148, 163, 184, 0.12)" vertical={false} />
          <XAxis dataKey="label" stroke="#94a3b8" tickLine={false} axisLine={false} />
          <YAxis stroke="#94a3b8" tickLine={false} axisLine={false} width={80} tickFormatter={(value) => `${value}%`} />
          <Tooltip formatter={(value: number) => formatSignedPercent(value)} />
          <Bar dataKey="value" radius={[8, 8, 0, 0]}>
            {returns.map((entry) => (
              <Cell key={entry.timestampUtc} fill={entry.value >= 0 ? "#22c55e" : "#ef4444"} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

function EmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="empty-state">
      <strong>{title}</strong>
      <p>{description}</p>
    </div>
  );
}

function LoadingGrid() {
  return (
    <section className="stats-grid">
      {Array.from({ length: 6 }).map((_, index) => (
        <div className="loading-card" key={index} />
      ))}
    </section>
  );
}

function formatCurrency(value: number, currency: string) {
  return new Intl.NumberFormat("en-GB", {
    style: "currency",
    currency: currency || "USD",
    maximumFractionDigits: 2,
  }).format(value);
}

function formatOptionalCurrency(value: number | null | undefined, currency: string) {
  return value === null || value === undefined ? "--" : formatCurrency(value, currency);
}

function formatSignedCurrency(value: number, currency: string) {
  const prefix = value > 0 ? "+" : "";
  return `${prefix}${formatCurrency(value, currency)}`;
}

function formatPercent(value: number) {
  return `${value.toFixed(1)}%`;
}

function formatSignedPercent(value: number) {
  return `${value > 0 ? "+" : ""}${value.toFixed(1)}%`;
}

function formatCompactNumber(value: number) {
  return new Intl.NumberFormat("en-GB", {
    notation: "compact",
    maximumFractionDigits: 2,
  }).format(value);
}

function compactAxisValue(value: number) {
  return formatCompactNumber(value);
}

function formatQuantity(quantity: number) {
  return new Intl.NumberFormat("en-GB", {
    maximumFractionDigits: 2,
  }).format(quantity);
}

function formatRelative(isoDate: string) {
  const date = new Date(isoDate);
  const deltaMs = Date.now() - date.getTime();
  const deltaMinutes = Math.round(deltaMs / 60_000);

  if (Number.isNaN(deltaMinutes)) {
    return "unknown";
  }

  if (deltaMinutes < 1) {
    return "just now";
  }

  if (deltaMinutes < 60) {
    return `${deltaMinutes}m ago`;
  }

  const deltaHours = Math.round(deltaMinutes / 60);
  if (deltaHours < 24) {
    return `${deltaHours}h ago`;
  }

  const deltaDays = Math.round(deltaHours / 24);
  return `${deltaDays}d ago`;
}

function getToneClass(value: number) {
  if (value > 0) {
    return "text-positive";
  }

  if (value < 0) {
    return "text-negative";
  }

  return "";
}

function getValueTone(value: number): "neutral" | "positive" | "negative" | "warning" {
  if (value > 0) {
    return "positive";
  }

  if (value < 0) {
    return "negative";
  }

  return "neutral";
}

function getAlertPillClass(severity: string) {
  return severity === "critical"
    ? "pill-negative"
    : severity === "warning"
      ? "pill-warning"
      : "pill-positive";
}

function getInsightPillClass(type: string) {
  return type === "risk"
    ? "pill-warning"
    : type === "sell"
      ? "pill-negative"
      : "pill-positive";
}

export default App;
