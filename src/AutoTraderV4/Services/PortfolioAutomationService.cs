using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AutoTraderV4.Services;

public sealed class PortfolioReviewDecision
{
    public string Ticker { get; set; } = string.Empty;
    public string Recommendation { get; set; } = "Hold";
    public decimal Quantity { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal UnrealizedPnlPercent { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class BuyOpportunityDecision
{
    public string Ticker { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal CashBudget { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string Rating { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class PortfolioReviewService
{
    private readonly IPortfolioRepository _repository;

    public PortfolioReviewService(IPortfolioRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<PortfolioReviewDecision>> ReviewOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        var decisions = new List<PortfolioReviewDecision>();

        foreach (var position in positions)
        {
            var currentPrice = position.CurrentPrice > 0m ? position.CurrentPrice : position.AveragePrice;
            var averagePrice = position.AveragePrice > 0m ? position.AveragePrice : currentPrice;
            var pnlPercent = averagePrice > 0m ? ((currentPrice - averagePrice) / averagePrice) * 100m : 0m;
            var action = "Hold";
            var reason = "Position remains within its risk and target range.";

            if (position.StopLoss > 0m && currentPrice <= position.StopLoss)
            {
                action = "Sell";
                reason = $"Current price {currentPrice:F2} is at or below the stop-loss level {position.StopLoss:F2}.";
            }
            else if (position.TakeProfit > 0m && currentPrice >= position.TakeProfit)
            {
                action = "Sell";
                reason = $"Current price {currentPrice:F2} reached the take-profit level {position.TakeProfit:F2}.";
            }
            else if (pnlPercent <= -10m || position.ConfidenceScore < 45m)
            {
                action = "Sell";
                reason = pnlPercent <= -10m
                    ? $"Position is down {pnlPercent:F2}% and no longer meets the hold criteria."
                    : $"Confidence score {position.ConfidenceScore:F2} is below the 45% hold threshold.";
            }

            if (!string.Equals(action, "Sell", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            decisions.Add(new PortfolioReviewDecision
            {
                Ticker = position.Ticker,
                Recommendation = action,
                Quantity = Math.Abs(position.Quantity),
                CurrentPrice = currentPrice,
                AveragePrice = averagePrice,
                UnrealizedPnlPercent = Math.Round(pnlPercent, 2, MidpointRounding.AwayFromZero),
                ConfidenceScore = position.ConfidenceScore,
                Reason = reason
            });
        }

        return decisions;
    }
}

public sealed class BuyOpportunityService
{
    private readonly IPortfolioRepository _repository;
    private readonly StrategyEngineService _strategyEngineService;
    private readonly RiskGovernanceService _riskGovernanceService;

    public BuyOpportunityService(
        IPortfolioRepository repository,
        StrategyEngineService strategyEngineService,
        RiskGovernanceService riskGovernanceService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _strategyEngineService = strategyEngineService ?? throw new ArgumentNullException(nameof(strategyEngineService));
        _riskGovernanceService = riskGovernanceService ?? throw new ArgumentNullException(nameof(riskGovernanceService));
    }

    public async Task<IReadOnlyList<BuyOpportunityDecision>> ScanForBuysAsync(CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetPortfolioStateAsync(cancellationToken);
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        var availableCash = state?.Cash ?? 0m;

        if (availableCash <= 0m)
        {
            return [];
        }

        var buyBudget = Math.Min(availableCash, availableCash * 0.5m);
        var openTickers = new HashSet<string>(positions.Select(x => x.Ticker), StringComparer.OrdinalIgnoreCase);
        var decisions = new List<BuyOpportunityDecision>();
        var remainingBudget = buyBudget;

        foreach (var opportunity in _strategyEngineService.BuildWatchlist()
                     .Where(x => !openTickers.Contains(x.Symbol))
                     .OrderByDescending(x => x.Confidence))
        {
            if (remainingBudget <= 0m)
            {
                break;
            }

            var price = opportunity.Price > 0m ? opportunity.Price : 100m;
            var maxQuantity = Math.Floor(remainingBudget / price);
            if (maxQuantity <= 0m)
            {
                continue;
            }

            var quantity = maxQuantity;
            var estimatedCost = quantity * price;
            var trade = new TradeDecision
            {
                Ticker = opportunity.Symbol,
                Side = OrderSide.Buy,
                Quantity = quantity,
                EntryPrice = price,
                StopLoss = price * 0.94m,
                TakeProfit = price * 1.18m,
                ConfidenceScore = opportunity.Confidence,
                Rating = opportunity.Rating,
                RiskReward = opportunity.ForecastReturn > 0m ? Math.Round(opportunity.ForecastReturn / 9m, 2, MidpointRounding.AwayFromZero) : 2.5m,
                TriggeringStrategy = "Portfolio automation",
                EligibleForExecution = true
            };

            var riskContext = new RiskContextSnapshot
            {
                Symbol = opportunity.Symbol,
                Sector = opportunity.Sector,
                LiquidityScore = 85m,
                SpreadPercent = 0.25m,
                ObservedAtUtc = DateTimeOffset.UtcNow
            };

            var riskAssessment = await _riskGovernanceService.EvaluateAsync(trade, riskContext, cancellationToken);
            if (!riskAssessment.Approved)
            {
                continue;
            }

            decisions.Add(new BuyOpportunityDecision
            {
                Ticker = opportunity.Symbol,
                Price = price,
                Quantity = quantity,
                EstimatedCost = estimatedCost,
                CashBudget = remainingBudget,
                ConfidenceScore = opportunity.Confidence,
                Rating = opportunity.Rating,
                Reason = $"{opportunity.Rating} with {opportunity.ForecastReturn:F2}% expected return and acceptable risk."
            });

            remainingBudget -= estimatedCost;
            if (remainingBudget < 0m)
            {
                remainingBudget = 0m;
            }
        }

        return decisions;
    }
}

public sealed class PortfolioAutomationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PortfolioAutomationBackgroundService> _logger;
    private static readonly TimeSpan ReviewInterval = TimeSpan.FromMinutes(5);

    public PortfolioAutomationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PortfolioAutomationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunAutomationCycleAsync(stoppingToken);

            using var timer = new PeriodicTimer(ReviewInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunAutomationCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The portfolio automation cycle failed.");
        }
    }

    private async Task RunAutomationCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var reviewService = scope.ServiceProvider.GetRequiredService<PortfolioReviewService>();
        var buyService = scope.ServiceProvider.GetRequiredService<BuyOpportunityService>();

        var sellDecisions = await reviewService.ReviewOpenPositionsAsync(cancellationToken);
        foreach (var decision in sellDecisions)
        {
            _logger.LogInformation(
                "Portfolio review: {Recommendation} {Ticker}. {Reason}",
                decision.Recommendation,
                decision.Ticker,
                decision.Reason);
        }

        var buyDecisions = await buyService.ScanForBuysAsync(cancellationToken);
        foreach (var decision in buyDecisions)
        {
            _logger.LogInformation(
                "Portfolio opportunity: buy {Ticker} {Quantity} shares for {EstimatedCost:C} ({Rating}, confidence {ConfidenceScore}).",
                decision.Ticker,
                decision.Quantity,
                decision.EstimatedCost,
                decision.Rating,
                decision.ConfidenceScore);
        }
    }
}
