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

CREATE TABLE IF NOT EXISTS "OrderExecutionRecords" (
    "Id" uuid PRIMARY KEY,
    "Ticker" varchar(64) NOT NULL,
    "Side" varchar(16) NOT NULL,
    "Status" varchar(32) NOT NULL,
    "StrategyName" varchar(256),
    "Quantity" numeric(18,6) NOT NULL,
    "EntryPrice" numeric(18,6) NOT NULL,
    "StopLoss" numeric(18,6) NOT NULL,
    "TakeProfit" numeric(18,6) NOT NULL,
    "FinalScore" numeric(8,2) NOT NULL,
    "ConfidenceScore" numeric(8,2) NOT NULL,
    "SignalScoresJson" text NOT NULL,
    "ForecastJson" text NOT NULL,
    "RiskAssessmentJson" text NOT NULL,
    "RecommendationJson" text NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    "UpdatedUtc" timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_OrderExecutionRecords_Ticker_CreatedUtc"
    ON "OrderExecutionRecords" ("Ticker", "CreatedUtc");
