# TODO

This repo has a usable trading starter in place, and the remaining roadmap items from [AGENTS.md](./AGENTS.md) and [plan.md](./plan.md) have been completed and validated.

## Status snapshot

- [x] ASP.NET Core app scaffold and demo-mode Trading 212 client are present.
- [x] EF Core persistence, portfolio repository, and dashboard models are implemented.
- [x] Weighted strategy, risk, and dashboard services are implemented and verified.
- [x] Market-data ingestion, sentiment fallback, stale-data detection, and source attribution are in place.
- [x] Forecasting, explainability, execution gating, and audit logging have been validated.
- [x] Backend tests, React tests, and the production UI build all pass.
- [x] The app starts cleanly in demo mode and responds successfully on the health endpoint.

## Completed implementation checklist

### 1. Validate environment and startup configuration
- [x] Confirm all required configuration values are documented and safe for local/dev use.
- [x] Ensure secret handling is explicit for live Trading 212 credentials.
- [x] Validate startup behavior when Postgres is unavailable or when demo mode is enabled.
- [x] Review `Program.cs`, `appsettings.json`, and environment overrides for edge cases.

### 2. Complete the market-data and sentiment pipeline
- [x] Add a resilient multi-source market-data layer for price, volume, fundamentals, news, and sentiment.
- [x] Support fallback providers when the primary data source fails.
- [x] Persist timestamped snapshots and source metadata for each symbol.
- [x] Add stale-data detection so trade execution is blocked when market inputs are too old.
- [x] Review the current `ApplicationDbContext` and dashboard services for missing data contracts.

### 3. Finish the weighted multi-strategy engine
- [x] Confirm each strategy emits structured signals with confidence and rationale.
- [x] Finalize the full weighted scoring model across trend, momentum, mean reversion, earnings surprise, and sentiment.
- [x] Enforce the AGENTS minimum execution threshold of 75 and validate the rating bands.
- [x] Review `StrategyEngineService.cs`, `MovingAverageStrategy.cs`, and `WeightedStrategyScoringService` for rollout gaps.

### 4. Add forecasting and explainability
- [x] Implement a forecast ensemble for 1D, 5D, 30D, and 90D horizons.
- [x] Produce recommendation payloads containing symbol, rating, confidence, entry/stop/take-profit, risk-reward, and top factors.
- [x] Persist forecast inputs and outputs with trade decisions for full traceability.
- [x] Review recommendation output against the AGENTS examples and dashboard contracts.

### 5. Harden portfolio risk governance and defensive mode
- [x] Confirm the risk service enforces exposure, position, sector, and cash-reserve limits before execution.
- [x] Validate defensive mode triggers and trade reduction behavior when limits are crossed.
- [x] Check stop-loss, take-profit, and liquidity checks for execution safety.
- [x] Review `PortfolioRiskService.cs` and `PortfolioRiskPolicy` for edge-case validation.

### 6. Complete execution and audit logging
- [x] Ensure every trade decision records timestamp, symbol, strategy, signal scores, forecast output, risk assessment, and final recommendation.
- [x] Verify order lifecycle tracking is consistent from signal generation to execution result.
- [x] Review `AuditLoggingService.cs`, `StrategyExecutionService.cs`, and the database schema for missing fields.

### 7. Validate dashboard and agent requirements against the spec
- [x] Verify the React dashboard includes all required summary cards, watchlist views, charts, and alerts.
- [x] Confirm the market overview uses the required benchmarks (S&P 500, Nasdaq, Dow Jones, FTSE 100, VIX).
- [x] Ensure AI insights and portfolio growth/drawdown projections match the AGENTS-defined dashboard requirements.
- [x] Review the UI implementation in `ui/src/App.jsx` and the dashboard tests for remaining gaps.

### 8. Run the full QA pass and fix regressions
- [x] Execute the backend test suite and UI build/tests.
- [x] Resolve any failing contract, strategy, or dashboard validation tests.
- [x] Confirm the final repo state matches the plan and AGENTS requirements.

## Definition of done

All required conditions are satisfied:

- the app starts reliably in demo mode and with valid local configuration
- market inputs are timestamped, source-attributed, and usable for execution decisions
- every trade passes the weighted-score and risk gates before execution
- forecasts and explanations are attached to trade decisions
- the dashboard covers the required summary, positions, watchlist, benchmarks, and alerts
- the test suite passes without regressions
