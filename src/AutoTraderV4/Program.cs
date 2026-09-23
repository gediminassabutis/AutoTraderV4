using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var trading212Options = builder.Configuration.GetSection("Trading212").Get<Trading212Options>() ?? new Trading212Options();
        builder.Services.AddSingleton(trading212Options);

        builder.Services.AddHttpClient<ITrading212Client, Trading212Client>((sp, client) =>
        {
            var options = sp.GetRequiredService<Trading212Options>();
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/api/v0/");
        });

        builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        builder.Services.AddScoped<StrategyExecutionService>();
        builder.Services.AddSingleton<MovingAverageStrategyService>();

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/api/account/summary", async (ITrading212Client client, CancellationToken cancellationToken) =>
        {
            var summary = await client.GetAccountSummaryAsync(cancellationToken);
            return Results.Ok(summary);
        });

        app.MapGet("/api/positions", async (IPortfolioRepository repository, CancellationToken cancellationToken) =>
        {
            var positions = await repository.GetAllPositionsAsync(cancellationToken);
            return Results.Ok(positions);
        });

        app.MapPost("/api/orders", async (Trading212OrderRequest request, ITrading212Client client, CancellationToken cancellationToken) =>
        {
            var result = await client.PlaceOrderAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        app.MapPost("/api/trades/decision", (TradeDecision decision) =>
        {
            decision.Validate();
            var request = OrderExecutionService.CreateRequest(decision);
            return Results.Ok(request);
        });

        app.MapPost("/api/strategies/evaluate", async (StrategySignal signal, StrategyExecutionService strategyExecutionService, CancellationToken cancellationToken) =>
        {
            var decision = await strategyExecutionService.ExecuteAsync(signal, cancellationToken);
            var request = OrderExecutionService.CreateRequest(decision);
            return Results.Ok(new { decision, request });
        });

        app.MapPost("/api/strategies/moving-average", (MovingAverageSignalRequest request, MovingAverageStrategyService strategyService) =>
        {
            var signal = strategyService.Evaluate(request);
            var decision = new StrategyEvaluator().Evaluate(signal);
            var requestBody = OrderExecutionService.CreateRequest(decision);
            return Results.Ok(new { signal, decision, request = requestBody });
        });

        app.Run();
    }
}
