export interface PortfolioSummary {
  currency: string;
  totalPortfolioValue: number;
  dailyPnL: number;
  weeklyPnL: number;
  monthlyPnL: number;
  availableCash: number;
  totalExposurePercent: number;
  defensiveMode: boolean;
  lastUpdatedUtc: string;
}

export interface PortfolioPositionView {
  symbol: string;
  quantity: number;
  entryPrice: number;
  currentPrice: number;
  unrealizedPnl: number;
  stopLoss?: number | null;
  takeProfit?: number | null;
  confidence?: number | null;
  sector: string;
  currency: string;
  allocationPercent: number;
  lastUpdatedUtc: string;
}

export interface WatchlistOpportunity {
  symbol: string;
  rating: string;
  confidence: number;
  forecastReturn: number;
  riskScore: number;
  price: number;
  sector: string;
  strategy: string;
  topFactors: string[];
  lastUpdatedUtc: string;
}

export interface MarketOverviewCard {
  symbol: string;
  label: string;
  value: number;
  changePercent: number;
  trend: string;
  lastUpdatedUtc: string;
}

export interface DashboardInsight {
  title: string;
  summary: string;
  type: string;
  symbol?: string | null;
}

export interface DashboardAlert {
  title: string;
  message: string;
  type: string;
  severity: string;
  timeUtc: string;
}

export interface PortfolioChartPoint {
  label: string;
  value: number;
  timestampUtc: string;
}

export interface PortfolioAllocation {
  name: string;
  value: number;
  color: string;
}

export interface ForecastProjection {
  symbol: string;
  horizon: string;
  projectedPrice: number;
  projectedReturnPercent: number;
}

export interface PortfolioDashboard {
  summary: PortfolioSummary;
  positions: PortfolioPositionView[];
  watchlist: WatchlistOpportunity[];
  marketOverview: MarketOverviewCard[];
  insights: DashboardInsight[];
  alerts: DashboardAlert[];
  growthCurve: PortfolioChartPoint[];
  equityCurve: PortfolioChartPoint[];
  drawdownHistory: PortfolioChartPoint[];
  monthlyReturns: PortfolioChartPoint[];
  allocation: PortfolioAllocation[];
  sectorAllocation: PortfolioAllocation[];
  forecastProjections: ForecastProjection[];
  lastUpdatedUtc: string;
}
