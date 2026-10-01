using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTraderV4.Tests;

public class ApiIntegrationTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsOkPayload()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var response = await app.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", payload.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AccountSummaryEndpoint_ReturnsTradingClientData()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var response = await app.Client.GetAsync("/api/account/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(42, payload.GetProperty("id").GetInt64());
        Assert.Equal("USD", payload.GetProperty("currency").GetString());
        Assert.Equal(12500.50m, payload.GetProperty("cash").GetProperty("availableToTrade").GetDecimal());
        Assert.Equal(48750.25m, payload.GetProperty("totalValue").GetDecimal());
    }

    [Fact]
    public async Task PositionsEndpoint_ReturnsPersistedPositionsInTickerOrder()
    {
        await using var app = await TestWebApplication.CreateAsync(context =>
        {
            context.Positions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = "MSFT_US_EQ",
                Quantity = 2m,
                AveragePrice = 410m,
                Currency = "USD",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            context.Positions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = "AAPL_US_EQ",
                Quantity = 4m,
                AveragePrice = 215m,
                Currency = "USD",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        });

        using var response = await app.Client.GetAsync("/api/positions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, payload.GetArrayLength());
        Assert.Equal("AAPL_US_EQ", payload[0].GetProperty("ticker").GetString());
        Assert.Equal("MSFT_US_EQ", payload[1].GetProperty("ticker").GetString());
    }

    [Fact]
    public async Task OrdersEndpoint_RejectsRawOrderSubmission()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var response = await app.Client.PostAsJsonAsync("/api/orders", new Trading212OrderRequest
        {
            Ticker = "NVDA_US_EQ",
            Quantity = 3m,
            Type = Trading212OrderType.Market
        });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Direct order submission is disabled.", payload.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TradeDecisionEndpoint_NormalizesSellOrdersToNegativeQuantity()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var response = await app.Client.PostAsJsonAsync("/api/trades/decision", new TradeDecision
        {
            Ticker = "TSLA_US_EQ",
            Side = OrderSide.Sell,
            Quantity = 5m,
            OrderType = Trading212OrderType.Market
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TSLA_US_EQ", payload.GetProperty("ticker").GetString());
        Assert.Equal(-5m, payload.GetProperty("quantity").GetDecimal());
        Assert.Equal("Market", payload.GetProperty("type").GetString());
    }

    [Fact]
    public async Task StrategyEvaluationEndpoint_PersistsSignalAndReturnsBuyRequest()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var response = await app.Client.PostAsJsonAsync("/api/strategies/evaluate", new StrategySignal
        {
            Ticker = "META_US_EQ",
            Signal = 1.5m,
            Quantity = 2.5m,
            OrderType = Trading212OrderType.Limit
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("META_US_EQ", payload.GetProperty("decision").GetProperty("ticker").GetString());
        Assert.Equal("Buy", payload.GetProperty("decision").GetProperty("side").GetString());
        Assert.Equal(2.5m, payload.GetProperty("request").GetProperty("quantity").GetDecimal());

        await using var scope = app.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await context.StrategySignals.CountAsync());
    }

    [Fact]
    public async Task StrategyRecommendationEndpoint_DisablesSyntheticScorer()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var response = await app.Client.PostAsJsonAsync("/api/strategies/recommend", new
        {
            symbol = "NVDA",
            price = 132.40m
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task MovingAverageEndpoint_ReturnsSellDecisionForDowntrend()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var response = await app.Client.PostAsJsonAsync("/api/strategies/moving-average", new MovingAverageSignalRequest
        {
            Ticker = "AAPL_US_EQ",
            Prices = new[] { 120m, 118m, 117m, 116m, 115m },
            Quantity = 6m,
            OrderType = Trading212OrderType.Market
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("signal").GetProperty("signal").GetDecimal() < 0m);
        Assert.Equal("Sell", payload.GetProperty("decision").GetProperty("side").GetString());
        Assert.Equal(-6m, payload.GetProperty("request").GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task DashboardEndpoint_ReturnsSummaryWatchlistAndCharts()
    {
        await using var app = await TestWebApplication.CreateAsync(context =>
        {
            var now = DateTimeOffset.UtcNow;
            MarketSnapshot Snapshot(string ticker, decimal price, DateTimeOffset lastUpdatedUtc) => new()
            {
                Id = Guid.NewGuid(),
                Ticker = ticker,
                Symbol = ticker,
                Price = price,
                Source = "test-provider",
                ProviderName = "verified-test-provider",
                FetchedAtUtc = lastUpdatedUtc,
                ReceivedAtUtc = now,
                DataAgeSeconds = (decimal)(now - lastUpdatedUtc).TotalSeconds,
                FreshnessStatus = "Fresh",
                MetadataJson = "{\"isSynthetic\":false,\"freshnessWindowSeconds\":300}",
                LastUpdatedUtc = lastUpdatedUtc
            };

            context.Positions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = "NVDA",
                Quantity = 5m,
                AveragePrice = 120m,
                Currency = "USD",
                UpdatedAtUtc = now.AddMinutes(-30)
            });
            context.Positions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = "MSFT",
                Quantity = 3m,
                AveragePrice = 410m,
                Currency = "USD",
                UpdatedAtUtc = now.AddMinutes(-30)
            });

            context.MarketSnapshots.AddRange(
                Snapshot("NVDA", 125m, now.AddMinutes(-2)),
                Snapshot("NVDA", 128m, now.AddMinutes(-1)),
                Snapshot("MSFT", 418m, now.AddMinutes(-2)),
                Snapshot("MSFT", 421m, now.AddMinutes(-1)),
                Snapshot("SPX", 5400m, now.AddMinutes(-2)),
                Snapshot("SPX", 5435m, now.AddMinutes(-1)));

            context.StrategySignals.Add(new StrategySignalRecord
            {
                Id = Guid.NewGuid(),
                Ticker = "NVDA",
                Signal = 1.2m,
                Quantity = 5m,
                Side = "Buy",
                OrderType = Trading212OrderType.Market.ToString(),
                CreatedUtc = now.AddMinutes(-5)
            });
        });

        using var response = await app.Client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(payload.GetProperty("summary").GetProperty("totalPortfolioValue").GetDecimal() > 0m);
        Assert.Equal(2, payload.GetProperty("positions").GetArrayLength());
        Assert.True(payload.GetProperty("watchlist").GetArrayLength() >= 1);
        Assert.True(payload.GetProperty("growthCurve").GetArrayLength() >= 1);
        Assert.True(payload.GetProperty("allocation").GetArrayLength() >= 1);
        Assert.Equal("S&P 500", payload.GetProperty("marketOverview")[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task PortfolioEndpoints_UpsertAndReturnGovernedSnapshot()
    {
        await using var app = await TestWebApplication.CreateAsync();

        using var stateResponse = await app.Client.PutAsJsonAsync("/api/portfolio/state", new UpsertPortfolioStateRequest
        {
            TotalValue = 100000m,
            Cash = 25000m,
            DailyProfitLoss = 1500m,
            WeeklyProfitLoss = 3200m,
            MonthlyProfitLoss = 6400m,
            PeakPortfolioValue = 102000m,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);

        using var positionResponse = await app.Client.PutAsJsonAsync("/api/portfolio/positions/NVDA", new UpsertPortfolioPositionRequest
        {
            Sector = "Technology",
            Quantity = 20m,
            AveragePrice = 120m,
            CurrentPrice = 125m,
            Currency = "USD",
            StopLoss = 112m,
            TakeProfit = 146m,
            ConfidenceScore = 88m,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.OK, positionResponse.StatusCode);

        using var snapshotResponse = await app.Client.GetAsync("/api/portfolio");
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);

        var payload = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100000m, payload.GetProperty("summary").GetProperty("totalPortfolioValue").GetDecimal());
        Assert.Equal(25000m, payload.GetProperty("summary").GetProperty("availableCash").GetDecimal());
        Assert.Equal(1, payload.GetProperty("positions").GetArrayLength());
        Assert.Equal("NVDA", payload.GetProperty("positions")[0].GetProperty("ticker").GetString());
        Assert.True(payload.GetProperty("risk").GetProperty("currentExposurePercent").GetDecimal() > 0m);

        using var auditResponse = await app.Client.GetAsync("/api/audit-logs?take=5");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var auditPayload = await auditResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(auditPayload.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task WeightedStrategyRecommendationEndpoint_ReturnsApprovedDecisionAndPersistsAudit()
    {
        await using var app = await TestWebApplication.CreateAsync(context =>
        {
            context.PortfolioStates.Add(new PortfolioStateRecord
            {
                Id = Guid.NewGuid(),
                TotalValue = 100000m,
                Cash = 30000m,
                DailyProfitLoss = 500m,
                WeeklyProfitLoss = 1250m,
                MonthlyProfitLoss = 2800m,
                PeakPortfolioValue = 101000m,
                DefensiveModeActive = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        });

        using var response = await app.Client.PostAsJsonAsync("/api/strategies/recommendations", new StrategyEvaluationRequest
        {
            Symbol = "NVDA",
            Sector = "Technology",
            CurrentPrice = 125m,
            SuggestedQuantity = 20m,
            OrderType = Trading212OrderType.Market,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Signals = new MarketSignalSnapshot
            {
                FiftyDayMovingAverage = 119m,
                TwoHundredDayMovingAverage = 100m,
                RsiValue = 54m,
                RsiTrendScore = 90m,
                VolumeTrendScore = 94m,
                RelativeStrengthPercentile = 99m,
                EarningsGrowthPercent = 30m,
                RevenueGrowthPercent = 24m,
                FreeCashFlowMarginPercent = 28m,
                DebtToEquityRatio = 0.8m,
                ReturnOnEquityPercent = 33m,
                PegRatio = 1.1m,
                EarningsSurprisePercent = 18m,
                GuidanceChangeScore = 92m,
                AnalystRevisionScore = 90m,
                SentimentScore = 96m,
                MacroScore = 55m,
                QualityScore = 95m,
                PriceDislocationPercent = 12m,
                LiquidityScore = 96m,
                SpreadPercent = 0.2m,
                AtrPercent = 2m
            },
            Forecast = new ForecastSnapshot
            {
                OneDayReturnPercent = 2.4m,
                FiveDayReturnPercent = 6.8m,
                ThirtyDayReturnPercent = 16m,
                NinetyDayReturnPercent = 34m,
                ModelConfidenceScore = 95m
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("decision").GetProperty("finalScore").GetDecimal() >= 75m);
        Assert.True(payload.GetProperty("decision").GetProperty("eligibleForExecution").GetBoolean());
        Assert.True(payload.GetProperty("riskAssessment").GetProperty("approved").GetBoolean());
        Assert.NotEqual(Guid.Empty, payload.GetProperty("auditLogId").GetGuid());

        await using var scope = app.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await context.TradeAuditEntries.ToListAsync());
    }

    [Fact]
    public async Task RiskGovernanceEndpoint_RejectsTrade_WhenSectorAndCashLimitsWouldBeBreached()
    {
        await using var app = await TestWebApplication.CreateAsync(context =>
        {
            context.PortfolioStates.Add(new PortfolioStateRecord
            {
                Id = Guid.NewGuid(),
                TotalValue = 100000m,
                Cash = 8000m,
                DailyProfitLoss = -500m,
                WeeklyProfitLoss = 500m,
                MonthlyProfitLoss = 1800m,
                PeakPortfolioValue = 102000m,
                DefensiveModeActive = false,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            context.Positions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = "MSFT",
                Sector = "Technology",
                Quantity = 40m,
                AveragePrice = 400m,
                CurrentPrice = 450m,
                Currency = "USD",
                StopLoss = 380m,
                TakeProfit = 480m,
                ConfidenceScore = 80m,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        });

        using var response = await app.Client.PostAsJsonAsync("/api/risk/governance/evaluate", new RiskEvaluationRequest
        {
            CandidateTrade = new TradeDecision
            {
                Ticker = "NVDA",
                Sector = "Technology",
                Side = OrderSide.Buy,
                Quantity = 30m,
                OrderType = Trading212OrderType.Market,
                EntryPrice = 500m,
                StopLoss = 470m,
                TakeProfit = 590m,
                RiskReward = 3m,
                Confidence = 90,
                ConfidenceScore = 90m,
                Rating = "Strong Buy"
            },
            Context = new RiskContextSnapshot
            {
                Symbol = "NVDA",
                Sector = "Technology",
                LiquidityScore = 90m,
                SpreadPercent = 0.2m,
                ObservedAtUtc = DateTimeOffset.UtcNow
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(payload.GetProperty("risk").GetProperty("approved").GetBoolean());
        Assert.True(payload.GetProperty("risk").GetProperty("defensiveModeActivated").GetBoolean());
        Assert.True(payload.GetProperty("risk").GetProperty("violations").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task RiskGovernanceEndpoint_RejectsBuy_WhenClaimedRiskRewardExceedsPriceDerivedValue()
    {
        await using var app = await TestWebApplication.CreateAsync(context =>
        {
            context.PortfolioStates.Add(new PortfolioStateRecord
            {
                Id = Guid.NewGuid(),
                TotalValue = 100000m,
                Cash = 20000m,
                PeakPortfolioValue = 100000m,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            context.Positions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = "JPM",
                Sector = "Financials",
                Quantity = 80m,
                AveragePrice = 1000m,
                CurrentPrice = 1000m,
                Currency = "USD",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        });

        using var response = await app.Client.PostAsJsonAsync("/api/risk/governance/evaluate", new RiskEvaluationRequest
        {
            CandidateTrade = new TradeDecision
            {
                Ticker = "NVDA",
                Sector = "Technology",
                Side = OrderSide.Buy,
                Quantity = 50m,
                OrderType = Trading212OrderType.Market,
                EntryPrice = 100m,
                StopLoss = 95m,
                TakeProfit = 105m,
                RiskReward = 3m,
                FinalScore = 82m,
                EligibleForExecution = true,
                ConfidenceScore = 90m
            },
            Context = new RiskContextSnapshot
            {
                Symbol = "NVDA",
                Sector = "Technology",
                LiquidityScore = 90m,
                SpreadPercent = 0.2m,
                ObservedAtUtc = DateTimeOffset.UtcNow
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var risk = payload.GetProperty("risk");
        Assert.False(risk.GetProperty("approved").GetBoolean());
        Assert.Equal(1m, risk.GetProperty("calculatedRiskReward").GetDecimal());
        Assert.Contains(
            risk.GetProperty("violations").EnumerateArray().Select(item => item.GetString()),
            message => message is not null && message.Contains("Calculated risk/reward", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RiskGovernanceEndpoint_AllowsRiskReducingSellDuringDefensiveMode()
    {
        await using var app = await TestWebApplication.CreateAsync(context =>
        {
            context.PortfolioStates.Add(new PortfolioStateRecord
            {
                Id = Guid.NewGuid(),
                TotalValue = 100000m,
                Cash = 4000m,
                DailyProfitLoss = -2500m,
                PeakPortfolioValue = 100000m,
                DefensiveModeActive = true,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            context.Positions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = "MSFT",
                Sector = "Technology",
                Quantity = 96m,
                AveragePrice = 1000m,
                CurrentPrice = 1000m,
                Currency = "USD",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        });

        using var response = await app.Client.PostAsJsonAsync("/api/risk/governance/evaluate", new RiskEvaluationRequest
        {
            CandidateTrade = new TradeDecision
            {
                Ticker = "MSFT",
                Side = OrderSide.Sell,
                Quantity = 2m,
                OrderType = Trading212OrderType.Market
            },
            Context = new RiskContextSnapshot
            {
                Symbol = "MSFT",
                Sector = "Technology",
                LiquidityScore = 85m,
                SpreadPercent = 0.2m,
                ObservedAtUtc = DateTimeOffset.UtcNow
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var risk = payload.GetProperty("risk");
        Assert.True(risk.GetProperty("approved").GetBoolean());
        Assert.True(risk.GetProperty("defensiveModeActivated").GetBoolean());
        Assert.Equal(94m, risk.GetProperty("projectedExposurePercent").GetDecimal());
    }

    [Fact]
    public async Task RiskGovernanceEndpoint_RejectsBuyWithoutScoreEligibilityAndProtectiveLevels()
    {
        await using var app = await TestWebApplication.CreateAsync(context =>
        {
            context.PortfolioStates.Add(new PortfolioStateRecord
            {
                Id = Guid.NewGuid(),
                TotalValue = 100000m,
                Cash = 30000m,
                PeakPortfolioValue = 100000m,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        });

        using var response = await app.Client.PostAsJsonAsync("/api/risk/governance/evaluate", new RiskEvaluationRequest
        {
            CandidateTrade = new TradeDecision
            {
                Ticker = "NVDA",
                Side = OrderSide.Buy,
                Quantity = 1m,
                OrderType = Trading212OrderType.Market,
                EntryPrice = 100m,
                StopLoss = 0m,
                TakeProfit = 0m,
                RiskReward = 4m,
                FinalScore = 70m,
                ConfidenceScore = 85m,
                EligibleForExecution = false
            },
            Context = new RiskContextSnapshot
            {
                Symbol = "NVDA",
                Sector = "Technology",
                LiquidityScore = 90m,
                SpreadPercent = 0.2m,
                ObservedAtUtc = DateTimeOffset.UtcNow
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var violations = payload.GetProperty("risk").GetProperty("violations")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        Assert.Contains(violations, message => message?.Contains("Final score", StringComparison.Ordinal) == true);
        Assert.Contains(violations, message => message?.Contains("not marked eligible", StringComparison.Ordinal) == true);
        Assert.Contains(violations, message => message?.Contains("stop-loss", StringComparison.Ordinal) == true);
    }
}
