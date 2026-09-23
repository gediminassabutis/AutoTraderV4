using AutoTraderV4;
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
}
