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
    "Symbol" varchar(64) NOT NULL,
    "Price" numeric(18,6) NOT NULL,
    "Open" numeric(18,6) NOT NULL,
    "High" numeric(18,6) NOT NULL,
    "Low" numeric(18,6) NOT NULL,
    "Close" numeric(18,6) NOT NULL,
    "Volume" numeric(18,6) NOT NULL,
    "Source" varchar(128) NOT NULL,
    "ProviderName" varchar(128) NOT NULL,
    "FetchedAtUtc" timestamp with time zone NOT NULL,
    "ReceivedAtUtc" timestamp with time zone NOT NULL,
    "DataAgeSeconds" numeric(18,2) NOT NULL,
    "FreshnessStatus" varchar(32) NOT NULL,
    "MetadataJson" jsonb NOT NULL,
    "LastUpdatedUtc" timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_MarketSnapshots_Ticker_LastUpdatedUtc"
    ON "MarketSnapshots" ("Ticker", "LastUpdatedUtc");

CREATE TABLE IF NOT EXISTS "SentimentRecords" (
    "Id" uuid PRIMARY KEY,
    "Ticker" varchar(64) NOT NULL,
    "Symbol" varchar(64) NOT NULL,
    "Source" varchar(128) NOT NULL,
    "ProviderName" varchar(128) NOT NULL,
    "FreshnessStatus" varchar(32) NOT NULL,
    "Score" numeric(8,4) NOT NULL,
    "Magnitude" numeric(8,4) NOT NULL,
    "Confidence" numeric(8,4) NOT NULL,
    "MetadataJson" jsonb NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    "RecordedAtUtc" timestamp with time zone NOT NULL
);

CREATE INDEX IF NOT EXISTS "IX_SentimentRecords_Ticker_CreatedUtc"
    ON "SentimentRecords" ("Ticker", "CreatedUtc");
