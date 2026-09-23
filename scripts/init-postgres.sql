CREATE TABLE IF NOT EXISTS "PortfolioPositions" (
    "Id" uuid PRIMARY KEY,
    "Ticker" varchar(64) NOT NULL,
    "Quantity" numeric(18,6) NOT NULL,
    "AveragePrice" numeric(18,6) NOT NULL,
    "Currency" varchar(10) NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PortfolioPositions_Ticker"
    ON "PortfolioPositions" ("Ticker");

CREATE TABLE IF NOT EXISTS "MarketSnapshots" (
    "Id" uuid PRIMARY KEY,
    "Ticker" varchar(64) NOT NULL,
    "Price" numeric(18,6) NOT NULL,
    "LastUpdatedUtc" timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_MarketSnapshots_Ticker_LastUpdatedUtc"
    ON "MarketSnapshots" ("Ticker", "LastUpdatedUtc");
