# Infrastructure and SOLID Assessment

## Repository infrastructure overview

### Top-level layout

- `src/AutoTraderV4`: ASP.NET Core backend (minimal APIs, domain models, services, persistence).
- `src/AutoTraderV4.WindowsDesktop`: WPF desktop client.
- `ui`: React/Vite dashboard project.
- `tests/AutoTraderV4.Tests`: backend integration/unit test suite.
- `.github/workflows`: CI workflow definitions.

### Backend runtime and composition

- **Runtime**: .NET 10 web app (`Microsoft.NET.Sdk.Web`).
- **Entry/composition root**: `src/AutoTraderV4/Program.cs`.
- **DI container**: built-in ASP.NET Core DI with service registrations for trading, strategy, risk, dashboard, and persistence services.
- **API style**: minimal APIs mapped directly in `Program.cs`.

### Data and persistence

- **ORM**: Entity Framework Core 10.
- **Primary DB**: PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`.
- **Fallback DB**: in-memory EF provider for demo/dev/test scenarios.
- **Repository abstraction**: `IPortfolioRepository` in `src/AutoTraderV4/PortfolioRepository.cs`.

### External/system integrations

- Trading API abstraction with `ITrading212Client` and two implementations:
  - `DemoTrading212Client`
  - `Trading212Client`
- Market and sentiment provider abstractions:
  - `IMarketDataProvider` (`Services/MarketDataService.cs`)
  - `ISentimentProvider` (`Services/SentimentService.cs`)

## SOLID pattern check

Status legend:

- ✅ = mostly follows
- ⚠️ = partially follows / notable gaps
- ❌ = does not follow

| Principle | Status | Evidence |
|---|---|---|
| **S — Single Responsibility** | ⚠️ | Several services are focused and cohesive (e.g., `IPortfolioRepository`, `RiskGovernanceService`), but `Program.cs` currently mixes composition root concerns with extensive endpoint definitions and request orchestration. |
| **O — Open/Closed** | ⚠️ | Interface-based providers support extension (`ITrading212Client`, `IMarketDataProvider`, `ISentimentProvider`), but strategy scoring logic is hardcoded in `WeightedStrategyScoringService` and `StrategyEvaluator`, so adding/changing strategy families requires direct modification. |
| **L — Liskov Substitution** | ✅ | Demo and production implementations for `ITrading212Client` are substitutable through DI; provider interfaces use consistent contracts and nullability expectations. |
| **I — Interface Segregation** | ✅ | Interfaces are generally small and role-specific (`IPortfolioRepository`, `ITrading212Client`, `IMarketDataProvider`, `ISentimentProvider`, `IPortfolioDashboardService`). |
| **D — Dependency Inversion** | ⚠️ | DI is used in many places, but there are direct concrete instantiations (`new StrategyEvaluator()`, `new StrategyEngineService()`, `new ForecastingService()`) and default concrete fallbacks in constructors (`new DemoMarketDataProvider()`, `new DemoSentimentProvider()`), which bypass abstractions and reduce testability/extensibility. |

## Conclusion

The repo **partially follows SOLID**:

- Strong use of interfaces and DI in core infrastructure.
- Clear architectural intent toward pluggable providers and risk governance.
- Main gaps are around dependency inversion in a few services and open/closed flexibility in strategy composition.

## Highest-value improvements (priority order)

1. **Refactor `Program.cs` endpoint mapping into extension modules** (e.g., `MapTradingEndpoints`, `MapRiskEndpoints`) to improve SRP and maintainability.
2. **Inject `IStrategyEvaluator` / `IStrategyEngine` / `IForecastingService` abstractions** instead of `new`ing concrete implementations in service methods.
3. **Move strategy weights/rules into configuration or per-strategy plug-ins** to improve OCP (new strategies without modifying core evaluator code).
4. **Remove constructor-time concrete provider defaults** in ingest services; rely on DI registrations and fail-fast validation when providers are missing.
5. **Add architecture tests** in `tests/AutoTraderV4.Tests` to enforce no direct construction of domain services outside composition root.
