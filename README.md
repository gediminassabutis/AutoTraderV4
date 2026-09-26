# AutoTraderV4

A .NET 10 C# trading automation starter for the Trading 212 Public API, with demo-mode defaults and optional PostgreSQL persistence.

## Features

- Trading 212 API client with Basic Authentication
- ASP.NET Core Web API endpoints
- PostgreSQL persistence via Entity Framework Core
- Weighted multi-strategy scoring engine with risk gates
- Portfolio risk policy and defensive-mode validation
- Unit tests covering auth, order behavior, and strategy/risk checks
- Docker Compose setup for local PostgreSQL and pgAdmin

## Prerequisites

- .NET 10 SDK
- Docker Desktop or a PostgreSQL instance
- Trading 212 Invest or Stocks ISA API credentials

## Run PostgreSQL locally

```bash
docker compose up -d
```

This starts:

- PostgreSQL on `localhost:5432`
- pgAdmin on `http://localhost:5050`

## Configure credentials

Keep the committed `src/AutoTraderV4/appsettings.json` safe by leaving the PostgreSQL connection string blank and supplying secrets through user secrets or environment variables.

Example:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": ""
  },
  "Database": {
    "UseInMemory": true,
    "InMemoryDatabaseName": "AutoTraderV4_Test"
  },
  "Trading212": {
    "BaseUrl": "https://demo.trading212.com",
    "UseDemoData": true,
    "ApiKey": "YOUR_API_KEY",
    "ApiSecret": "YOUR_API_SECRET"
  }
}
```

Set `ConnectionStrings__DefaultConnection` only when you want to use PostgreSQL, and also set `Database__UseInMemory=false` to force the app off the in-memory store. In Development the app will also load secrets from the configured local user-secrets store, so you can keep live credentials out of source control. When the in-memory flag is unset, startup defaults to the in-memory store in local/demo mode and only uses PostgreSQL when the app is not in demo mode and the configured connection is reachable.

The app falls back to demo data automatically when `Trading212:UseDemoData` is `true` or when the API key/secret are left blank. This keeps the dashboard working on a clean machine without a live Trading 212 subscription.

For test-only runs without a PostgreSQL instance, set the app to use EF Core's in-memory database:

```bash
export Database__UseInMemory=true
# or on Windows PowerShell:
$env:Database__UseInMemory = 'true'
```

You can also override the in-memory database name with `Database__InMemoryDatabaseName` if needed.

## Run the backend app

```bash
dotnet restore
dotnet run --project src/AutoTraderV4/AutoTraderV4.csproj --urls http://localhost:5065
```

## Build the integrated React dashboard

```bash
cd ui
npm install
npm run build
```

The dashboard lives in [ui/](./ui) and builds independently with Vite.

For iterative UI work you can also run the Vite dev server:

```bash
cd ui
npm run dev -- --host 0.0.0.0 --port 5173
```

## Test the app

```bash
dotnet test AutoTraderV4.slnx --nologo
cd ui && npm test && npm run build
```

The automated suite now includes:

- backend integration tests for health, account summary, positions, order submission, strategy evaluation, risk gating, and the dashboard aggregate endpoint
- strategy regression tests for order normalization and moving-average signal behavior
- dashboard specification tests that verify the AGENTS and agent definitions still capture the required dashboard views, charts, and alerts
- React dashboard rendering tests that validate the core portfolio, positions, and watchlist views
- the React production build to validate the dashboard bundle and static asset generation

## Main endpoints

- `GET /health`
- `GET /api/dashboard`
- `GET /api/dashboard/summary`
- `GET /api/watchlist`
- `GET /api/risk/summary`
- `GET /api/portfolio`
- `PUT /api/portfolio/state`
- `PUT /api/portfolio/positions/{ticker}`
- `GET /api/audit-logs`
- `GET /api/account/summary`
- `GET /api/positions`
- `POST /api/orders`
- `POST /api/strategies/weighted-score`
- `POST /api/risk/validate`
- `GET /api/risk/policy`

## Specialized agents

The repository now includes a set of focused Copilot agents in [.github/agents](./.github/agents) to drive the phased implementation described in [plan.md](./plan.md): market data, strategy engine, risk governance, dashboard analytics, and QA validation.

## Notes

- The API uses the Trading 212 demo environment by default.
- Sell order quantities are represented as negative values, as required by the Trading 212 API contract.
- Keep secrets out of source control; prefer environment variables or user secrets in production.
- See [plan.md](./plan.md) for the implementation roadmap derived from [AGENTS.md](./AGENTS.md).
