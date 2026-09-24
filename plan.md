# AutoTraderV4 Implementation Plan

This plan translates the trading-agent requirements in [AGENTS.md](AGENTS.md) into a staged delivery roadmap for the current codebase.

## 1. Repo assessment

The repository already contains a strong starting point for a Trading 212 automation service:

- [src/AutoTraderV4/Program.cs](src/AutoTraderV4/Program.cs) wires the ASP.NET Core app, dependency injection, EF Core, and minimal API endpoints.
- [src/AutoTraderV4/Trading212Client.cs](src/AutoTraderV4/Trading212Client.cs) implements the Trading 212 HTTP client and order submission flow.
- [src/AutoTraderV4/ApplicationDbContext.cs](src/AutoTraderV4/ApplicationDbContext.cs) stores positions, market snapshots, and strategy records.
- [src/AutoTraderV4/StrategyEvaluator.cs](src/AutoTraderV4/StrategyEvaluator.cs) and [src/AutoTraderV4/MovingAverageStrategy.cs](src/AutoTraderV4/MovingAverageStrategy.cs) provide the initial signal-to-decision pattern.
- [tests/AutoTraderV4.Tests](tests/AutoTraderV4.Tests) already covers auth, order normalization, repository persistence, and simple strategy logic.

The main gaps relative to [AGENTS.md](AGENTS.md) are:

- no multi-source market data pipeline for price, fundamentals, news, and sentiment
- no weighted multi-strategy scoring engine beyond the simple moving-average baseline
- no forecast ensemble for 1D/5D/30D/90D horizons
- no portfolio risk engine or defensive mode logic
- no React dashboard, alerting, or audit trail beyond application logs
- no explicit handling for the full trade lifecycle required by the AGENTS specification

## 2. Agent set for execution

The implementation is split across specialized Copilot agents in [.github/agents](.github/agents):

- [market-data.agent.md](.github/agents/market-data.agent.md) for market, macro, and sentiment ingestion
- [strategy-engine.agent.md](.github/agents/strategy-engine.agent.md) for weighted strategy scoring and opportunity generation
- [risk-governance.agent.md](.github/agents/risk-governance.agent.md) for capital protection and defensive-mode logic
- [dashboard-analytics.agent.md](.github/agents/dashboard-analytics.agent.md) for market dashboards, watchlists, and alerts
- [qa-validation.agent.md](.github/agents/qa-validation.agent.md) for regression and release validation

This keeps the work modular while preserving a single execution plan across the trading lifecycle.

## 3. Implementation strategy

### Phase 1: harden the platform foundation

Goal: create the operational backbone for a governed trading agent.

Tasks:

1. Add production-grade configuration for secrets, market-data providers, and feature flags.
2. Centralize logging and telemetry for trade decisions, API failures, and market-data freshness.
3. Extend the database schema to cover price history, news events, macro indicators, portfolio state, risk limits, and audit records.
4. Add health checks and safe startup validation so the app fails early when config or dependencies are missing.
5. Standardize trade DTOs and response models so each decision contains a full explainability payload.

Key files to touch:

- [src/AutoTraderV4/Program.cs](src/AutoTraderV4/Program.cs)
- [src/AutoTraderV4/ApplicationDbContext.cs](src/AutoTraderV4/ApplicationDbContext.cs)
- [src/AutoTraderV4/appsettings.json](src/AutoTraderV4/appsettings.json)

Acceptance criteria:

- application starts with valid config and clean dependency registration
- all key data domains are persisted and queryable
- trade decisions carry traceable metadata

### Phase 2: build the data and signal ingestion layer

Goal: supply the scoring engine with complete, up-to-date market inputs.

Tasks:

1. Add a market-data abstraction layer for equities, ETFs, indices, and sector proxies.
2. Integrate at least one primary market data source and one fallback source for resilience.
3. Pull price history, volume, VWAP, RSI, MACD, moving averages, ATR, and relative-strength data.
4. Add news/sentiment ingestion for earnings calls, financial news, regulatory events, and analyst changes.
5. Add a macro data feed for inflation, interest rates, sector rotation, and broader market context.
6. Store market snapshots and sentiment events with timestamps and source metadata.

Suggested additions:

- `src/AutoTraderV4/Services/MarketDataService.cs`
- `src/AutoTraderV4/Services/SentimentService.cs`
- `src/AutoTraderV4/Models/MarketSnapshot.cs` or equivalent domain models

Acceptance criteria:

- each symbol has current and historical price/volume context
- market and sentiment data are timestamped and source-attributed
- stale data can be detected and blocked from trade execution

### Phase 3: implement the multi-factor strategy engine

Goal: move from a single moving-average signal to a weighted agent strategy framework.

Tasks:

1. Implement Strategy A: Trend Following with MA crossovers, RSI trend confirmation, and volume validation.
2. Implement Strategy B: Momentum scoring using relative strength, earnings growth, and rising volume.
3. Implement Strategy C: Mean reversion checks for oversold quality setups and dislocations.
4. Implement Strategy D: Earnings surprise strategy using guidance changes and analyst revision signals.
5. Implement Strategy E: AI Sentiment scoring using news, transcripts, filings, and commentary sentiment.
6. Combine the five strategy outputs with explicit weights (35/25/15/15/10).
7. Convert the combined signal into a risk-adjusted final score in the 0-100 band and enforce the minimum threshold of 75.

Key files to evolve:

- [src/AutoTraderV4/StrategyEvaluator.cs](src/AutoTraderV4/StrategyEvaluator.cs)
- [src/AutoTraderV4/MovingAverageStrategy.cs](src/AutoTraderV4/MovingAverageStrategy.cs)
- [src/AutoTraderV4/StrategyExecutionService.cs](src/AutoTraderV4/StrategyExecutionService.cs)

Acceptance criteria:

- each strategy can emit a structured signal with confidence and rationale
- combined final scores align to the AGENTS rating table
- no trade is created below the required score threshold

### Phase 4: add forecasting and explainability

Goal: produce forecast-driven recommendations and traceable explanations.

Tasks:

1. Implement a forecasting service for 1-day, 5-day, 30-day, and 90-day windows.
2. Combine ensemble models (tree-based, deep learning, and time-series methods) using weighted output aggregation.
3. Generate a recommendation payload with symbol, rating, confidence, entry price, stop loss, take profit, risk/reward, and top factors.
4. Attach the forecast output and signal scores to every trade decision for full traceability.
5. Ensure recommendations include explainable reasoning and not just a raw numerical score.

Suggested additions:

- `src/AutoTraderV4/Services/ForecastingService.cs`
- `src/AutoTraderV4/Services/RecommendationBuilder.cs`

Acceptance criteria:

- each recommendation contains the required explainability fields
- forecast output is persisted with the trade decision
- all signals can be audited back to a decision record

### Phase 5: add risk management and defensive controls

Goal: enforce capital preservation and portfolio safety before profit-seeking.

Tasks:

1. Enforce maximum portfolio exposure, minimum cash reserve, max position sizing, sector caps, daily loss limits, and drawdown thresholds.
2. Add a risk service that blocks any order that violates the configured caps.
3. Implement defensive mode to suspend new trades, reduce exposure, raise cash, and prioritize weak-position exits when limits are breached.
4. Support stop-loss, trailing stop, and take-profit logic aligned to valid execution policies.
5. Validate liquidity and spread before trade execution.

Suggested additions:

- `src/AutoTraderV4/Services/PortfolioRiskService.cs`
- `src/AutoTraderV4/Services/PositionSizingService.cs`

Acceptance criteria:

- no trade can exceed max exposure, sector, or position limits
- defensive mode automatically reduces portfolio risk
- every order includes entry/stop/take-profit values and size

### Phase 6: implement execution and audit logging

Goal: make every action fully traceable and explainable.

Tasks:

1. Extend execution logic to support market, limit, stop-limit, and trailing-stop order flows.
2. Persist audit logs containing timestamp, symbol, strategy trigger, signal scores, forecast outputs, prices, risk assessment, and final confidence.
3. Record both trade execution and trade closure events.
4. Add an order-status view so portfolio decisions can be replayed.
5. Ensure sell-side and buy-side operations follow the Trading 212 contract (negative sell quantity semantics).

Key files to evolve:

- [src/AutoTraderV4/TradeDecision.cs](src/AutoTraderV4/TradeDecision.cs)
- [src/AutoTraderV4/Trading212Client.cs](src/AutoTraderV4/Trading212Client.cs)
- [src/AutoTraderV4/ApplicationDbContext.cs](src/AutoTraderV4/ApplicationDbContext.cs)

Acceptance criteria:

- each decision has a persisted audit record
- trade events can be reconstructed from the database
- action logs include both recommendation and final execution details

### Phase 7: add the React dashboard and alerts panel

Goal: satisfy the dashboard requirements in the AGENTS specification.

Tasks:

1. Build a dashboard shell with a portfolio summary, open positions table, watchlist, market overview, alerts, and AI insights panel.
2. Add Recharts-based visualizations: portfolio growth curve, equity curve, asset allocation, drawdown, sector allocation, forecast projections, and monthly returns.
3. Integrate live market metrics for S&P 500, Nasdaq, Dow Jones, FTSE 100, and VIX.
4. Surface alerts for executed trades, risk warnings, rebalance events, and data-source failures.
5. Create a watchlist ranked by confidence score with risk and forecast metrics.

Suggested additions:

- `src/AutoTraderV4.Web/` or a front-end workspace under `src/` for a React app
- `src/AutoTraderV4/Endpoints/PortfolioDashboardEndpoints.cs` or equivalent API endpoints

Acceptance criteria:

- dashboard displays all required KPIs and charts
- alerts are actionable and sourced from the trade engine
- the watchlist is ordered by confidence and monitors top opportunities

### Phase 8: continuous automation and integration testing

Goal: operationalize the agent in a production-like loop.

Tasks:

1. Add a scheduled analysis loop that runs every five minutes.
2. Sequence the workflow: fetch data, score strategies, generate forecasts, assess risk, execute eligible trades, persist logs, and refresh dashboard state.
3. Add end-to-end tests for scoring, risk gating, and order creation.
4. Add integration tests for the database and mock market provider behavior.
5. Add CI validation for dotnet build and test execution.

Acceptance criteria:

- analysis loop can run on a consistent schedule
- trade execution is gated by confidence and risk checks
- regression tests cover critical path behavior

## 4. Recommended delivery order

1. Foundation and persistence
2. Market data + sentiment ingestion
3. Strategy engine and scoring
4. Forecasting and explainability
5. Risk management and execution safety
6. Dashboard, alerts, and market overview
7. Continuous automation and QA

## 5. Success definition

This project is considered complete when it can:

- ingest and normalize market, news, and macro inputs
- compute a weighted multi-strategy recommendation score
- apply risk constraints before every order
- persist full trade and recommendation audit records
- render a dashboard that explains portfolio health and candidate trades
- operate in a repeatable five-minute analysis loop with transparent, auditable decisions

## 6. Immediate next step

The next concrete move is to implement the risk and data layers first, because they are the gating dependencies for all AGENTS.md features. Once the market-data contract and portfolio-risk service are in place, the strategy and dashboard layers can be built against a stable platform.
