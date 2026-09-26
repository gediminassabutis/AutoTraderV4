# Repository improvement checklist

## Executive summary

The repository is in a healthy state overall: the backend test suite passes, the UI test suite passes after installing frontend dependencies, and the production dashboard build succeeds. The repo is a solid trading starter and already demonstrates a good level of implementation depth.

Validation baseline from this review:

- Backend: 48 tests passed (`dotnet test AutoTraderV4.slnx --nologo`)
- Frontend: 1 UI test passed (`npm test -- --run` in `ui/`)
- Frontend build: succeeded (`npm run build` in `ui/`)
- One non-blocking warning: the Vite bundle is larger than 500 kB and should be optimized

The main opportunities are around maintainability, security hardening, and CI coverage rather than product correctness.

## What is already working well

- Clear onboarding instructions in [README.md](README.md)
- Demo-mode support and graceful database fallback in [src/AutoTraderV4/Program.cs](src/AutoTraderV4/Program.cs)
- Good test coverage across backend and dashboard behavior in [tests/AutoTraderV4.Tests](tests/AutoTraderV4.Tests)
- Functional dashboard and risk/policy scaffolding across the app
- Roadmap and implementation notes are captured in [plan.md](plan.md) and [TODO.md](TODO.md)

## Improvement opportunities

### 1. Consolidate duplicate risk service implementations (High)

There are two overlapping risk service implementations that define similar types and logic:

- [src/AutoTraderV4/PortfolioRiskService.cs](src/AutoTraderV4/PortfolioRiskService.cs)
- [src/AutoTraderV4/Services/PortfolioRiskService.cs](src/AutoTraderV4/Services/PortfolioRiskService.cs)

This duplication creates ambiguity, increases maintenance cost, and makes it harder to reason about which implementation is being used at runtime. The service registration in [src/AutoTraderV4/Program.cs](src/AutoTraderV4/Program.cs) also registers multiple risk-related services, which suggests the class hierarchy was expanded without a single canonical source of truth.

Recommended action:

- keep a single `PortfolioRiskService` implementation
- remove duplicated model types (`TradeReductionPlan`, `RiskCheckResult`, etc.) unless both namespaces intentionally serve different purposes
- align DI registration with one canonical risk service and one namespace convention

### 2. Tighten CORS and API exposure defaults (High)

The application enables very permissive CORS in [src/AutoTraderV4/Program.cs](src/AutoTraderV4/Program.cs):

- `AllowAnyOrigin()`
- `AllowAnyHeader()`
- `AllowAnyMethod()`

This is acceptable for local development, but it is a poor default for any non-local deployment. Sensitive endpoints such as order processing, portfolio writes, and risk evaluation should not be open to arbitrary external origins without explicit configuration.

Recommended action:

- restrict CORS to a known allowlist for production
- add authentication/authorization for sensitive endpoints if the app is exposed externally
- add `UseHttpsRedirection()` in production scenarios
- keep Swagger/UI enabled only for non-production or behind authenticated access

### 3. Expand CI to cover the UI and end-to-end checks (High)

The GitHub Actions workflow in [.github/workflows/dotnet-desktop.yml](.github/workflows/dotnet-desktop.yml) only restores, builds, and tests the .NET project. It does not validate the Vite React app or the dashboard build.

This is important because the UI has its own dependency chain and build expectations. The local validation showed that the UI test/build can pass, but the CI workflow currently misses that coverage.

Recommended action:

- add `npm install` / `npm ci` for `ui/`
- run `npm test -- --run`
- run `npm run build`
- keep the backend and UI checks in the same CI workflow

### 4. Reduce frontend bundle size and charting overhead (Medium)

The production build completed successfully, but Vite reported a chunk warning:

- `dist/assets/index-SGc6dp3Z.js` was about 639 kB after minification

This is not a functional blocker, but it is a maintainability and performance issue. The dashboard already imports multiple Recharts chart components into one page, which is a likely contributor to the large bundle.

Recommended action:

- lazy-load secondary dashboard sections or chart sets
- split chart-heavy modules into separate chunks
- consider lighter charting strategies or component-level code splitting if the app grows
- add bundle-size monitoring to CI and guardrails for large regressions

### 5. Add stronger runtime observability and operational guardrails (Medium)

The repository has good test coverage and a friendly setup story, but operational telemetry is still light. Trading systems benefit from explicit monitoring around:

- external API latency and timeouts
- stale-market-data detection and alerts
- failed order submissions and retry behavior
- structured audit logs tied to execution decisions
- health checks for database and upstream services

Recommended action:

- add health checks for PostgreSQL and upstream Trading 212 connectivity
- centralize logging/metrics for order events and dashboard errors
- add request-level trace IDs and correlation across backend calls
- document alert thresholds and operational runbooks

### 6. Improve consistency of project structure and naming (Medium)

The repo mixes naming patterns and placement conventions across service classes and namespaces. Examples include several services under both the root namespace and the `Services` folder, and the codebase contains multiple definitions that appear to serve overlapping responsibilities.

Recommended action:

- adopt one canonical directory convention
- document the intended responsibility of each service namespace
- standardize naming (`*Service` vs `*Engine`, repository vs. app-layer classes)
- remove dead/duplicate types as they are identified

### 7. Add more production-oriented contract and smoke tests (Medium)

The tests already cover a lot of behavior, which is a strong sign. However, the repo would benefit from additional contract-level coverage around the highest-risk flows:

- portfolio state writes
- position updates and validation
- risk evaluation edge cases
- dashboard API contract stability
- browser-level smoke tests for the UI

Recommended action:

- add API contract tests for the critical routes in [src/AutoTraderV4/Program.cs](src/AutoTraderV4/Program.cs)
- add a Playwright or end-to-end smoke test for the dashboard experience
- keep regression checks in CI for both backend and frontend

### 8. Align the codebase with clean architecture, SOLID, and KISS principles (High)

The repository has a good feature set, but some parts still reflect a more pragmatic prototype structure than a fully layered clean-architecture implementation. This is especially visible in the concentration of responsibilities in startup configuration, service registration, and domain logic spread across multiple entry points.

Recommended actions:

- Separate responsibilities into clear layers: API, application, domain, and infrastructure.
  - keep HTTP/API contracts and minimal endpoints in the presentation layer
  - keep business rules and validation in domain/application services
  - keep external integrations, EF Core, and Trading 212 clients in infrastructure code
- Respect Single Responsibility Principle:
  - avoid mixing signal evaluation, risk validation, and persistence concerns in the same class where possible
  - split large service responsibilities into smaller, focused services with explicit boundaries
- Respect Open/Closed Principle:
  - add extension points for strategy engines, market providers, and risk rules rather than hardcoding branching in one service
  - make new strategies/providers pluggable instead of editing central logic everywhere
- Respect Liskov Substitution Principle:
  - any provider or client abstraction should honor the same contract without surprising callers
  - avoid “fake” implementations with different semantics from the primary implementation
- Respect Interface Segregation Principle:
  - keep narrow interfaces for market data, sentiment, and order execution rather than exposing large, broad abstractions
- Respect Dependency Inversion Principle:
  - depend on abstractions such as `ITrading212Client`, provider interfaces, and repositories instead of concrete implementations wherever possible
  - keep infrastructure dependencies behind application-facing contracts
- Apply KISS principles:
  - prefer small, readable services and explicit DTOs over large monolithic methods
  - reduce duplicate logic across risk services and duplicated model definitions
  - simplify startup and endpoint configuration where possible instead of stacking many one-off registrations in one method
- Consider a package-by-feature or folder-by-feature structure as the project grows
  - for example, separate `TradeExecution`, `PortfolioRisk`, `MarketData`, and `Dashboard` responsibilities into cohesive modules

This would improve maintainability as the codebase grows and makes future changes safer, especially for risk logic, trade execution, and API contracts.

## Suggested priority order

1. Consolidate duplicate risk service implementations
2. Tighten CORS and production security defaults
3. Add UI checks to CI
4. Reduce bundle size and optimize chart loading
5. Add runtime observability and operational guardrails
6. Standardize project structure and naming conventions
7. Align the codebase with clean architecture, SOLID, and KISS principles

## Conclusion

The repository is already in a strong state and does not appear to have major functional blockers. The most valuable improvements are not massive rewrites but cleanup and hardening work: reducing duplication, tightening security defaults, covering the UI in CI, improving observability and bundle performance, and aligning the codebase with clean architecture and SOLID/KISS principles so it remains maintainable as the application grows.
