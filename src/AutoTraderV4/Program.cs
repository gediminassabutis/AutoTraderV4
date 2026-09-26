using System.Net.Sockets;
using System.Text.Json.Serialization;
using AutoTraderV4.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AutoTraderV4;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddUserSecrets<Program>(optional: true);
        }

        ConfigureServices(builder);

        var app = builder.Build();
        ConfigureApp(app);
        await app.RunAsync();
    }

    public static void ConfigureServices(WebApplicationBuilder builder, bool includeDatabase = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("Default", policy =>
            {
                policy.AllowAnyOrigin();
                policy.AllowAnyHeader();
                policy.AllowAnyMethod();
            });
        });

        var trading212Options = builder.Configuration.GetSection("Trading212").Get<Trading212Options>() ?? new Trading212Options();
        builder.Services.AddSingleton(trading212Options);

        var hasTrading212Credentials = !string.IsNullOrWhiteSpace(trading212Options.ApiKey)
            && !string.IsNullOrWhiteSpace(trading212Options.ApiSecret);
        var useDemoData = trading212Options.UseDemoData || !hasTrading212Credentials;

        if (useDemoData)
        {
            builder.Services.AddSingleton<ITrading212Client, DemoTrading212Client>();
        }
        else
        {
            builder.Services.AddHttpClient<ITrading212Client, Trading212Client>((sp, client) =>
            {
                var options = sp.GetRequiredService<Trading212Options>();
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/api/v0/");
            });
        }

        builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        builder.Services.AddScoped<StrategyExecutionService>();
        builder.Services.AddSingleton<PortfolioRiskPolicy>();
        builder.Services.AddScoped<PortfolioRiskService>();
        builder.Services.AddScoped<global::AutoTraderV4.Services.PortfolioRiskService>();
        builder.Services.AddSingleton<MovingAverageStrategyService>();
        builder.Services.AddSingleton<WeightedStrategyScoringService>();
        builder.Services.AddScoped<IPortfolioDashboardService, PortfolioDashboardService>();
        builder.Services.AddSingleton<StrategyEngineService>();
        builder.Services.AddSingleton<AuditLogService>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<WeightedStrategyEngineService>();
        builder.Services.AddScoped<RiskGovernanceService>();
        builder.Services.AddScoped<AuditLoggingService>();
        builder.Services.AddScoped<PortfolioService>();

        if (includeDatabase)
        {
            ConfigureDatabaseServices(builder, useDemoData);
        }

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    }

    private static void ConfigureDatabaseServices(WebApplicationBuilder builder, bool useDemoData)
    {
        var databaseConfiguration = ResolveDatabaseConfiguration(builder.Configuration, useDemoData);

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (databaseConfiguration.UseInMemoryDatabase)
            {
                options.UseInMemoryDatabase(databaseConfiguration.InMemoryDatabaseName);
                return;
            }

            options.UseNpgsql(databaseConfiguration.ConnectionString);
        });
    }

    private static DatabaseConfiguration ResolveDatabaseConfiguration(IConfiguration configuration, bool useDemoData)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredUseInMemory = configuration.GetValue<bool?>("Database:UseInMemory")
            ?? configuration.GetValue<bool?>("UseInMemoryDatabase");
        var inMemoryDatabaseName = configuration.GetValue<string>("Database:InMemoryDatabaseName") ?? "AutoTraderV4_Test";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (configuredUseInMemory is true)
        {
            return DatabaseConfiguration.InMemory(inMemoryDatabaseName);
        }

        if (configuredUseInMemory is false)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Database:UseInMemory is false, but ConnectionStrings:DefaultConnection is not configured.");
            }

            var postgresAvailability = EvaluatePostgresAvailability(connectionString);
            if (!postgresAvailability.IsReachable)
            {
                throw new InvalidOperationException(
                    $"Database:UseInMemory is false, but the configured PostgreSQL connection is not reachable. {postgresAvailability.FailureReason}");
            }

            return DatabaseConfiguration.Postgres(postgresAvailability.ConnectionString);
        }

        if (useDemoData || string.IsNullOrWhiteSpace(connectionString))
        {
            return DatabaseConfiguration.InMemory(inMemoryDatabaseName);
        }

        var fallbackAvailability = EvaluatePostgresAvailability(connectionString);
        return fallbackAvailability.IsReachable
            ? DatabaseConfiguration.Postgres(fallbackAvailability.ConnectionString)
            : DatabaseConfiguration.InMemory(inMemoryDatabaseName);
    }

    private static PostgresAvailability EvaluatePostgresAvailability(string connectionString)
    {
        try
        {
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(connectionStringBuilder.Host))
            {
                return PostgresAvailability.Unreachable(connectionString, "The PostgreSQL connection string is missing a host.");
            }

            connectionStringBuilder.Timeout = connectionStringBuilder.Timeout is > 0 and <= 2
                ? connectionStringBuilder.Timeout
                : 2;
            connectionStringBuilder.CommandTimeout = connectionStringBuilder.CommandTimeout is > 0 and <= 2
                ? connectionStringBuilder.CommandTimeout
                : 2;
            connectionStringBuilder.Pooling = false;

            using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
            connection.Open();
            return PostgresAvailability.Reachable(connectionStringBuilder.ConnectionString);
        }
        catch (ArgumentException ex)
        {
            return PostgresAvailability.Unreachable(connectionString, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return PostgresAvailability.Unreachable(connectionString, ex.Message);
        }
        catch (SocketException ex)
        {
            return PostgresAvailability.Unreachable(connectionString, ex.Message);
        }
        catch (TimeoutException ex)
        {
            return PostgresAvailability.Unreachable(connectionString, ex.Message);
        }
        catch (NpgsqlException ex)
        {
            return PostgresAvailability.Unreachable(connectionString, ex.Message);
        }
    }

    private sealed record DatabaseConfiguration(bool UseInMemoryDatabase, string InMemoryDatabaseName, string? ConnectionString)
    {
        public static DatabaseConfiguration InMemory(string databaseName) => new(true, databaseName, null);

        public static DatabaseConfiguration Postgres(string connectionString) => new(false, string.Empty, connectionString);
    }

    private sealed record PostgresAvailability(bool IsReachable, string ConnectionString, string? FailureReason)
    {
        public static PostgresAvailability Reachable(string connectionString) => new(true, connectionString, null);

        public static PostgresAvailability Unreachable(string connectionString, string failureReason) => new(false, connectionString, failureReason);
    }

    public static void ConfigureApp(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseCors("Default");

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/api/dashboard", async (IPortfolioDashboardService dashboardService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await dashboardService.GetDashboardAsync(cancellationToken));
        });

        app.MapGet("/api/dashboard/summary", async (IPortfolioDashboardService dashboardService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await dashboardService.GetSummaryAsync(cancellationToken));
        });

        app.MapGet("/api/watchlist", (StrategyEngineService strategyEngineService) =>
        {
            return Results.Ok(strategyEngineService.BuildWatchlist());
        });

        app.MapGet("/api/risk/summary", async ([FromServices] IPortfolioDashboardService dashboardService, [FromServices] global::AutoTraderV4.Services.PortfolioRiskService riskService, CancellationToken cancellationToken) =>
        {
            var dashboard = await dashboardService.GetDashboardAsync(cancellationToken);
            return Results.Ok(riskService.Evaluate(dashboard));
        });

        app.MapGet("/api/audit-log", (AuditLogService auditLogService) =>
        {
            return Results.Ok(auditLogService.GetRecent());
        });

        app.MapGet("/api/audit-logs", async (string? symbol, int? take, AuditLoggingService auditLoggingService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await auditLoggingService.GetRecentAsync(symbol, take ?? 50, cancellationToken));
        });

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

        app.MapGet("/api/portfolio", async (PortfolioService portfolioService, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await portfolioService.GetSnapshotAsync(cancellationToken));
        });

        app.MapPut("/api/portfolio/state", async (UpsertPortfolioStateRequest request, IPortfolioRepository repository, AuditLoggingService auditLoggingService, CancellationToken cancellationToken) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var state = request.ToEntity();
            await repository.UpsertPortfolioStateAsync(state, cancellationToken);
            var persistedState = await repository.GetPortfolioStateAsync(cancellationToken) ?? state;
            var auditEntry = await auditLoggingService.LogPortfolioStateAsync(persistedState, cancellationToken);
            return Results.Ok(new { state = persistedState, auditLogId = auditEntry.Id });
        });

        app.MapPut("/api/portfolio/positions/{ticker}", async (string ticker, UpsertPortfolioPositionRequest request, IPortfolioRepository repository, AuditLoggingService auditLoggingService, CancellationToken cancellationToken) =>
        {
            request.Ticker = ticker;
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var position = request.ToEntity();
            await repository.UpsertPositionAsync(position, cancellationToken);
            var persistedPosition = (await repository.GetAllPositionsAsync(cancellationToken))
                .Single(x => x.Ticker == ticker.Trim().ToUpperInvariant());
            var auditEntry = await auditLoggingService.LogPortfolioPositionAsync(persistedPosition, cancellationToken);
            return Results.Ok(new { position = persistedPosition, auditLogId = auditEntry.Id });
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
            return Results.Ok(new
            {
                ticker = request.Ticker,
                quantity = request.Quantity,
                type = request.Type.ToString()
            });
        });

        app.MapPost("/api/risk/evaluate", async ([FromBody] TradeDecision decision, [FromServices] IPortfolioDashboardService dashboardService, [FromServices] global::AutoTraderV4.Services.PortfolioRiskService riskService, CancellationToken cancellationToken) =>
        {
            var dashboard = await dashboardService.GetDashboardAsync(cancellationToken);
            var result = riskService.Evaluate(dashboard, decision);
            return Results.Ok(result);
        });

        app.MapPost("/api/risk/governance/evaluate", async (RiskEvaluationRequest request, RiskGovernanceService riskGovernanceService, AuditLoggingService auditLoggingService, CancellationToken cancellationToken) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var result = await riskGovernanceService.EvaluateAsync(request.CandidateTrade, request.Context, cancellationToken);
            var auditEntry = await auditLoggingService.LogRiskEvaluationAsync(request.CandidateTrade, request.Context, result, cancellationToken);
            return Results.Ok(new { risk = result, auditLogId = auditEntry.Id });
        });

        app.MapPost("/api/strategies/recommend", (RecommendationRequest request, StrategyEngineService strategyEngineService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Symbol))
            {
                return Results.BadRequest(new { error = "Ticker symbol is required." });
            }

            var recommendation = strategyEngineService.BuildRecommendation(request.Symbol, request.Price > 0m ? request.Price : 100m);
            return Results.Ok(recommendation);
        });

        app.MapPost("/api/strategies/recommendations", async (StrategyEvaluationRequest request, WeightedStrategyEngineService strategyEngineService, RiskGovernanceService riskGovernanceService, AuditLoggingService auditLoggingService, CancellationToken cancellationToken) =>
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var decision = strategyEngineService.Evaluate(request);
            var riskAssessment = await riskGovernanceService.EvaluateAsync(decision, request.ToRiskContext(), cancellationToken);
            var auditEntry = await auditLoggingService.LogStrategyEvaluationAsync(request, decision, riskAssessment, cancellationToken);

            return Results.Ok(new StrategyEvaluationResponse
            {
                Decision = decision,
                RiskAssessment = riskAssessment,
                AuditLogId = auditEntry.Id,
                EvaluatedAtUtc = auditEntry.CreatedUtc
            });
        });

        app.MapPost("/api/trades/audit", (TradeDecision decision, AuditLogService auditLogService, StrategyEngineService strategyEngineService) =>
        {
            decision.Validate();
            var recommendation = strategyEngineService.BuildRecommendation(decision.Ticker, decision.EntryPrice > 0m ? decision.EntryPrice : 100m);
            auditLogService.Record(decision, decision.TriggeringStrategy, recommendation.SignalScores, recommendation.Rating, "Risk gate passed");
            return Results.Ok(new { decision, recommendation });
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

        app.MapPost("/api/strategies/weighted-score", (WeightedStrategyRequest request, WeightedStrategyScoringService scoringService) =>
        {
            var metrics = new WeightedStrategyMetrics
            {
                TrendScore = request.TrendScore,
                MomentumScore = request.MomentumScore,
                MeanReversionScore = request.MeanReversionScore,
                EarningsSurpriseScore = request.EarningsSurpriseScore,
                SentimentScore = request.SentimentScore
            };

            var score = scoringService.Evaluate(request.Ticker, metrics);
            var side = score.TradeEligible ? OrderSide.Buy : OrderSide.Sell;
            var decision = new TradeDecision
            {
                Ticker = request.Ticker,
                Side = side,
                Quantity = request.Quantity,
                OrderType = request.OrderType,
                ConfidenceScore = Math.Round(score.FinalScore, 2),
                Rating = score.Rating,
                EntryPrice = request.EntryPrice,
                StopLoss = request.StopLoss,
                TakeProfit = request.TakeProfit,
                RiskReward = request.EntryPrice > 0m && request.StopLoss > 0m && request.EntryPrice != request.StopLoss
                    ? (request.TakeProfit - request.EntryPrice) / (request.EntryPrice - request.StopLoss)
                    : 0m,
                TopFactors = score.TopFactors.ToList(),
                TriggeringStrategy = "Weighted strategy engine",
                RiskAssessment = score.TradeEligible ? "Approved for execution" : "Below threshold; monitor for re-entry"
            };

            return Results.Ok(new { score, decision });
        });

        app.MapGet("/api/risk/policy", (PortfolioRiskPolicy policy) => Results.Ok(policy));

        app.MapPost("/api/risk/validate", (TradeRiskRequest request, PortfolioRiskService riskService) =>
        {
            var assessment = riskService.EvaluateTrade(request);
            return Results.Ok(assessment);
        });

        app.MapFallbackToFile("index.html");
    }
}
