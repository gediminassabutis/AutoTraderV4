# AutoTraderV4

A .NET 10 C# trading automation starter for the Trading 212 Public API, backed by PostgreSQL.

## Features

- Trading 212 API client with Basic Authentication
- ASP.NET Core Web API endpoints
- PostgreSQL persistence via Entity Framework Core
- Unit tests covering auth and order behavior
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

Update the connection string and Trading 212 keys in `src/AutoTraderV4/appsettings.json`.

Example:

```json
{
	"ConnectionStrings": {
		"DefaultConnection": "Host=localhost;Database=autotrader_v4;Username=postgres;Password=postgres"
	},
	"Trading212": {
		"BaseUrl": "https://demo.trading212.com",
		"ApiKey": "YOUR_API_KEY",
		"ApiSecret": "YOUR_API_SECRET"
	}
}
```

## Run the app

```bash
dotnet restore
dotnet run --project src/AutoTraderV4/AutoTraderV4.csproj
```

## Test the app

```bash
dotnet test AutoTraderV4.slnx --nologo
```

## Main endpoints

- `GET /health`
- `GET /api/account/summary`
- `POST /api/orders`

## Notes

- The API uses the Trading 212 demo environment by default.
- Sell order quantities are represented as negative values, as required by the Trading 212 API contract.
- Keep secrets out of source control; prefer environment variables or user secrets in production.
