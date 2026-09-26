# TODO

This repo has a usable trading starter in place, but a few roadmap items from [AGENTS.md](./AGENTS.md) and [plan.md](./plan.md) are still open or require hardening. The list below captures the remaining work after reviewing the current codebase.

## Status snapshot

- ASP.NET Core app scaffold and demo-mode Trading 212 client are present.
- EF Core persistence, portfolio repository, and dashboard models are implemented.
- Weighted strategy, risk, and dashboard services already exist in the codebase.
- Remaining gaps are mostly around production hardening, end-to-end data integration, and full QA validation.

## Missing / incomplete tasks

### 1. Validate environment and startup configuration
- [ ] Confirm all required configuration values are documented and safe for local/dev use.
- [ ] Ensure secret handling is explicit for live Trading 212 credentials.
- [ ] Validate startup behavior when Postgres is unavailable or when demo mode is enabled.
- [ ] Review `Program.cs`, `appsettings.json`, and environment overrides for edge cases.

### 2. Complete the market-data and sentiment pipeline
- [ ] Add a resilient multi-source market-data layer for price, volume, fundamentals, news, and sentiment.
- [ ] Support fallback providers when the primary data source fails.
- [ ] Persist timestamped snapshots and source metadata for each symbol.
- [ ] Add stale-data detection so trade execution is blocked when market inputs are too old.
- [ ] Review the current `ApplicationDbContext` and dashboard services for missing data contracts.

### 3. Finish the weighted multi-strategy engine
- [ ] Confirm each strategy emits structured signals with confidence and rationale.
- [ ] Finalize the full weighted scoring model across trend, momentum, mean reversion, earnings surprise, and sentiment.
- [ ] Enforce the AGENTS minimum execution threshold of 75 and validate the rating bands.
- [ ] Review `StrategyEngineService.cs`, `MovingAverageStrategy.cs`, and `WeightedStrategyScoringService` for rollout gaps.

### 4. Add forecasting and explainability
- [ ] Implement a forecast ensemble for 1D, 5D, 30D, and 90D horizons.
- [ ] Produce recommendation payloads containing symbol, rating, confidence, entry/stop/take-profit, risk-reward, and top factors.
- [ ] Persist forecast inputs and outputs with trade decisions for full traceability.
- [ ] Review recommendation output against the AGENTS examples and dashboard contracts.

### 5. Harden portfolio risk governance and defensive mode
- [ ] Confirm the risk service enforces exposure, position, sector, and cash-reserve limits before execution.
- [ ] Validate defensive mode triggers and trade reduction behavior when limits are crossed.
- [ ] Check stop-loss, take-profit, and liquidity checks for execution safety.
- [ ] Review `PortfolioRiskService.cs` and `PortfolioRiskPolicy` for edge-case validation.

### 6. Complete execution and audit logging
- [ ] Ensure every trade decision records timestamp, symbol, strategy, signal scores, forecast output, risk assessment, and final recommendation.
- [ ] Verify order lifecycle tracking is consistent from signal generation to execution result.
- [ ] Review `AuditLoggingService.cs`, `StrategyExecutionService.cs`, and the database schema for missing fields.

### 7. Validate dashboard and agent requirements against the spec
- [ ] Verify the React dashboard includes all required summary cards, watchlist views, charts, and alerts.
- [ ] Confirm the market overview uses the required benchmarks (S&P 500, Nasdaq, Dow Jones, FTSE 100, VIX).
- [ ] Ensure AI insights and portfolio growth/drawdown projections match the AGENTS-defined dashboard requirements.
- [ ] Review the UI implementation in `ui/src/App.jsx` and the dashboard tests for remaining gaps.

### 8. Run the full QA pass and fix regressions
- [ ] Execute the backend test suite and UI build/tests.
- [ ] Resolve any failing contract, strategy, or dashboard validation tests.
- [ ] Confirm the final repo state matches the plan and AGENTS requirements.

## Recommended order of execution

1. Configuration and local startup validation
2. Market-data and sentiment ingestion
3. Strategy scoring and forecasting completion
4. Risk governance and defensive mode hardening
5. Audit logging and execution traceability
6. Dashboard compliance and final QA pass

## Definition of done

The work is complete when all of the following are true:

- the app starts reliably in demo mode and with valid local configuration
- market inputs are timestamped, source-attributed, and usable for execution decisions
- every trade passes the weighted-score and risk gates before execution
- forecasts and explanations are attached to trade decisions
- the dashboard covers the required summary, positions, watchlist, bench market, and alerts
- the test suite passes without regressions
