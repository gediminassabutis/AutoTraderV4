using AutoTraderV4;
using AutoTraderV4.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoTraderV4.Tests;

public class Trading212ApplicationTests
{
    [Fact]
    public void Trading212Credentials_WhenCreated_HasBasicAuthorizationHeader()
    {
        var header = Trading212Credentials.CreateAuthorizationHeader("demo-key", "demo-secret");

        Assert.StartsWith("Basic ", header);
        Assert.Contains("ZGVtby1rZXk6ZGVtby1zZWNyZXQ=", header);
    }

    [Fact]
    public void Trading212OrderRequest_CreateSell_UsesNegativeQuantity()
    {
        var order = Trading212OrderRequest.CreateSell("AAPL_US_EQ", 10.5m, Trading212OrderType.Market);

        Assert.Equal("AAPL_US_EQ", order.Ticker);
        Assert.Equal(-10.5m, order.Quantity);
        Assert.Equal(Trading212OrderType.Market, order.Type);
    }

    [Fact]
    public void Program_ConfigureServices_UsesInMemoryDatabase_WhenConfigured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:UseInMemory"] = "true",
            ["Database:InMemoryDatabaseName"] = "Program_ConfigureServices_Test"
        });

        Program.ConfigureServices(builder);

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(context.Database.IsInMemory());
    }

    [Fact]
    public void Program_ConfigureServices_UsesInMemoryDatabase_WhenDemoDataIsEnabledAndDatabaseFlagIsUnset()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Trading212:UseDemoData"] = "true",
            ["Trading212:ApiKey"] = string.Empty,
            ["Trading212:ApiSecret"] = string.Empty,
            ["Database:UseInMemory"] = null,
            ["Database:InMemoryDatabaseName"] = "Program_ConfigureServices_DemoDefault_Test"
        });

        Program.ConfigureServices(builder);

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(context.Database.IsInMemory());
    }

    [Fact]
    public void Program_ConfigureServices_UsesDemoTrading212Client_WhenDemoDataEnabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Trading212:UseDemoData"] = "true",
            ["Trading212:ApiKey"] = string.Empty,
            ["Trading212:ApiSecret"] = string.Empty
        });

        Program.ConfigureServices(builder);

        using var provider = builder.Services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITrading212Client>();

        Assert.IsType<DemoTrading212Client>(client);
    }

    [Fact]
    public async Task PortfolioRepository_UpsertPosition_PersistsTickerAndQuantity()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var repository = new PortfolioRepository(context);

        await repository.UpsertPositionAsync(new PortfolioPosition
        {
            Ticker = "AAPL_US_EQ",
            Quantity = 15m,
            AveragePrice = 185.00m,
            Currency = "USD",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        var positions = await repository.GetAllPositionsAsync();

        Assert.Single(positions);
        Assert.Equal("AAPL_US_EQ", positions[0].Ticker);
        Assert.Equal(15m, positions[0].Quantity);
    }

    [Fact]
    public void TradeDecision_Validate_RejectsEmptyTicker()
    {
        var decision = new TradeDecision
        {
            Ticker = string.Empty,
            Side = OrderSide.Buy,
            Quantity = 10m,
            OrderType = Trading212OrderType.Market
        };

        var exception = Assert.Throws<ArgumentException>(() => decision.Validate());

        Assert.Equal("Ticker", exception.ParamName);
    }

    [Fact]
    public void OrderExecutionService_CreateRequest_FromTradeDecision_UsesCorrectDirection()
    {
        var decision = new TradeDecision
        {
            Ticker = "MSFT_US_EQ",
            Side = OrderSide.Sell,
            Quantity = 7.5m,
            OrderType = Trading212OrderType.Market
        };

        var request = OrderExecutionService.CreateRequest(decision);

        Assert.Equal("MSFT_US_EQ", request.Ticker);
        Assert.Equal(-7.5m, request.Quantity);
        Assert.Equal(Trading212OrderType.Market, request.Type);
    }

    [Fact]
    public void StrategyEvaluator_EvaluatesSignal_ProducesBuyDecision_WhenSignalIsPositive()
    {
        var evaluator = new StrategyEvaluator();

        var decision = evaluator.Evaluate(new StrategySignal
        {
            Ticker = "AAPL_US_EQ",
            Signal = 1.25m,
            Quantity = 12m,
            OrderType = Trading212OrderType.Market
        });

        Assert.Equal("AAPL_US_EQ", decision.Ticker);
        Assert.Equal(OrderSide.Buy, decision.Side);
        Assert.Equal(12m, decision.Quantity);
    }

    [Fact]
    public async Task StrategyExecutionService_ExecuteAsync_PersistsSignalAndDecision()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var service = new StrategyExecutionService(context);

        var result = await service.ExecuteAsync(new StrategySignal
        {
            Ticker = "NVDA_US_EQ",
            Signal = 0.8m,
            Quantity = 4m,
            OrderType = Trading212OrderType.Market
        });

        Assert.Equal("NVDA_US_EQ", result.Ticker);
        Assert.Equal(OrderSide.Buy, result.Side);
        Assert.Equal(4m, result.Quantity);
        Assert.Equal(1, await context.StrategySignals.CountAsync());
    }

    [Fact]
    public void MovingAverageStrategy_Evaluate_ReturnsPositiveSignal_WhenShortAverageExceedsLongAverage()
    {
        var strategy = new MovingAverageStrategy();

        var signal = strategy.Evaluate(
            "MSFT_US_EQ",
            new[] { 101m, 103m, 105m, 108m, 112m },
            quantity: 9m,
            orderType: Trading212OrderType.Market);

        Assert.Equal("MSFT_US_EQ", signal.Ticker);
        Assert.True(signal.Signal > 0m);
        Assert.Equal(9m, signal.Quantity);
        Assert.Equal(Trading212OrderType.Market, signal.OrderType);
    }

    [Fact]
    public void MovingAverageStrategyService_UsesRequestToGenerateSellSignal_WhenRecentTrendIsDown()
    {
        var service = new MovingAverageStrategyService();

        var signal = service.Evaluate(new MovingAverageSignalRequest
        {
            Ticker = "AAPL_US_EQ",
            Prices = new[] { 120m, 119m, 118m, 117m, 116m },
            Quantity = 3m,
            OrderType = Trading212OrderType.Market
        });

        Assert.Equal("AAPL_US_EQ", signal.Ticker);
        Assert.True(signal.Signal < 0m);
        Assert.Equal(3m, signal.Quantity);
        Assert.Equal(Trading212OrderType.Market, signal.OrderType);
    }

    [Fact]
    public void WeightedStrategyScoringService_Evaluate_ProducesStrongBuy_WhenMetricsAreStrong()
    {
        var service = new WeightedStrategyScoringService();

        var score = service.Evaluate("NVDA_US_EQ", new WeightedStrategyMetrics
        {
            TrendScore = 92m,
            MomentumScore = 88m,
            MeanReversionScore = 65m,
            EarningsSurpriseScore = 80m,
            SentimentScore = 78m
        });

        Assert.Equal("Strong Buy", score.Rating);
        Assert.True(score.TradeEligible);
        Assert.True(score.FinalScore >= 75m);
        Assert.Contains("Strong trend confirmation", score.TopFactors);
        Assert.Equal(WeightedStrategyScoringService.MinimumExecutionScore, 75m);
        Assert.NotEmpty(score.StrategySignals);
        Assert.All(score.StrategySignals, signal => Assert.True(signal.Confidence >= 0));
        Assert.Contains("minimum execution threshold", score.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WeightedStrategyScoringService_GetRating_MapsBandsToExpectedLabels()
    {
        Assert.Equal("Strong Sell", WeightedStrategyScoringService.GetRating(39m));
        Assert.Equal("Sell", WeightedStrategyScoringService.GetRating(54m));
        Assert.Equal("Hold", WeightedStrategyScoringService.GetRating(64m));
        Assert.Equal("Buy", WeightedStrategyScoringService.GetRating(79m));
        Assert.Equal("Strong Buy", WeightedStrategyScoringService.GetRating(80m));
    }

    [Fact]
    public void WeightedStrategyScoringService_RejectsExecutionBelowMinimumThreshold()
    {
        var service = new WeightedStrategyScoringService();

        var score = service.Evaluate("AAPL_US_EQ", new WeightedStrategyMetrics
        {
            TrendScore = 48m,
            MomentumScore = 42m,
            MeanReversionScore = 60m,
            EarningsSurpriseScore = 40m,
            SentimentScore = 32m
        });

        Assert.False(score.TradeEligible);
        Assert.True(score.FinalScore < WeightedStrategyScoringService.MinimumExecutionScore);
        Assert.Equal("Sell", score.Rating);
    }
    [Fact]
    public void PortfolioRiskService_EvaluateTrade_RejectsTrade_WhenExposureExceedsPolicy()
    {
        var service = new PortfolioRiskService();

        var assessment = service.EvaluateTrade(
            portfolioValue: 10000m,
            availableCash: 300m,
            proposedPositionValue: 900m,
            existingExposureValue: 9000m,
            sectorExposureValue: 600m,
            dailyPortfolioLoss: -350m,
            portfolioDrawdownPct: 5.5m);

        Assert.False(assessment.IsAllowed);
        Assert.Contains(assessment.Violations, violation => violation.Contains("exceed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceCollection_RegistersTrading212Client()
    {
        var services = new ServiceCollection();

        services.AddSingleton(new Trading212Options
        {
            BaseUrl = "https://demo.trading212.com",
            ApiKey = "demo-key",
            ApiSecret = "demo-secret"
        });

        services.AddHttpClient<ITrading212Client, Trading212Client>((sp, client) =>
        {
            var options = sp.GetRequiredService<Trading212Options>();
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/api/v0/");
        });

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ITrading212Client>());
    }

    [Fact]
    public void PortfolioRiskService_RejectsRiskyTrade_WhenExposurePassesLimit()
    {
        var dashboard = new PortfolioDashboard
        {
            Summary = new PortfolioSummary
            {
                TotalPortfolioValue = 100000m,
                AvailableCash = 10000m,
                TotalExposurePercent = 90m,
                DailyPnL = 1200m
            },
            Positions =
            [
                new PortfolioPositionView
                {
                    Symbol = "AAPL",
                    Quantity = 25m,
                    CurrentPrice = 200m
                }
            ]
        };
        var riskService = new PortfolioRiskService();

        var result = riskService.Evaluate(dashboard, new TradeDecision
        {
            Ticker = "AAPL",
            Side = OrderSide.Buy,
            Quantity = 5000m,
            EntryPrice = 800m,
            OrderType = Trading212OrderType.Market,
            Confidence = 82
        });

        Assert.False(result.Allowed);
        Assert.Contains(result.Warnings, warning => warning.Contains("5% max position size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StrategyEngineService_BuildRecommendation_ReturnsHighConfidenceResult()
    {
        var service = new StrategyEngineService();

        var recommendation = service.BuildRecommendation("NVDA", 132.40m);

        Assert.Equal("NVDA", recommendation.Symbol);
        Assert.True(recommendation.Confidence >= 75);
        Assert.True(recommendation.RiskReward >= 2.5m);
        Assert.Contains(recommendation.TopFactors, factor => factor.Contains("Strong", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuditLogService_Record_SavesDecisionMetadata()
    {
        var service = new AuditLogService();
        var decision = new TradeDecision
        {
            Ticker = "MSFT",
            Quantity = 10m,
            EntryPrice = 420m,
            StopLoss = 396m,
            TakeProfit = 460m,
            Confidence = 88,
            Rating = "Buy",
            TriggeringStrategy = "weighted-strategy",
            CreatedUtc = DateTimeOffset.UtcNow
        };

        service.Record(decision, decision.TriggeringStrategy, new Dictionary<string, decimal> { ["Trend"] = 86m }, "Buy", "Within limits");
        var entries = service.GetRecent();

        Assert.Single(entries);
        Assert.Equal("MSFT", entries[0].Symbol);
        Assert.Equal("weighted-strategy", entries[0].TriggeringStrategy);
    }
}
