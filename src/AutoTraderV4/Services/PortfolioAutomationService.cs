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
    private readonly MarketDataService _marketDataService;

    public PortfolioReviewService(IPortfolioRepository repository, MarketDataService marketDataService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
    }

    public async Task<IReadOnlyList<PortfolioReviewDecision>> ReviewOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        var decisions = new List<PortfolioReviewDecision>();

        foreach (var position in positions)
        {
            var averagePrice = position.AveragePrice > 0m ? position.AveragePrice : position.CurrentPrice;
            var baselinePrice = position.CurrentPrice > 0m ? position.CurrentPrice : averagePrice;
            var freshMarketQuote = await TryGetFreshQuoteAsync(position.Ticker, cancellationToken);
            if (freshMarketQuote is null)
            {
                continue;
            }

            var currentPrice = freshMarketQuote.Price > 0m ? freshMarketQuote.Price : baselinePrice;
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

    private async Task<MarketDataContract?> TryGetFreshQuoteAsync(string ticker, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return null;
        }

        var result = await _marketDataService.IngestAsync(ticker, cancellationToken);
        if (result.Success && result.FreshnessStatus == DataFreshnessStatus.Fresh && result.Data is not null)
        {
            return result.Data;
        }

        return null;
    }
}

public sealed class BuyOpportunityService
{
    private readonly IPortfolioRepository _repository;
    private readonly StrategyEngineService _strategyEngineService;
    private readonly RiskGovernanceService _riskGovernanceService;
    private readonly ITrading212Client _trading212Client;
    private readonly MarketDataService _marketDataService;

    public BuyOpportunityService(
        IPortfolioRepository repository,
        StrategyEngineService strategyEngineService,
        RiskGovernanceService riskGovernanceService,
        ITrading212Client trading212Client,
        MarketDataService marketDataService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _strategyEngineService = strategyEngineService ?? throw new ArgumentNullException(nameof(strategyEngineService));
        _riskGovernanceService = riskGovernanceService ?? throw new ArgumentNullException(nameof(riskGovernanceService));
        _trading212Client = trading212Client ?? throw new ArgumentNullException(nameof(trading212Client));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
    }

    public async Task<IReadOnlyList<BuyOpportunityDecision>> ScanForBuysAsync(CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetPortfolioStateAsync(cancellationToken);
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);

        Trading212AccountSummary accountSummary;
        try
        {
            accountSummary = await _trading212Client.GetAccountSummaryAsync(cancellationToken);
        }
        catch (Exception)
        {
            return [];
        }

        var availableCash = Math.Max(0m, accountSummary.CashDetails.AvailableToTrade);
        if (availableCash <= 0m)
        {
            return [];
        }

        var portfolioValue = Math.Max(state?.TotalValue ?? 0m, accountSummary.TotalValue);
        if (portfolioValue <= 0m)
        {
            return [];
        }

        var maxPositionValue = portfolioValue * 0.05m;
        var buyBudget = Math.Min(availableCash, maxPositionValue);
        if (buyBudget <= 0m)
        {
            return [];
        }

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

            var quote = await TryGetFreshQuoteAsync(opportunity.Symbol, cancellationToken);
            if (quote is null)
            {
                continue;
            }

            var price = quote.Price > 0m ? quote.Price : opportunity.Price;
            if (price <= 0m)
            {
                continue;
            }

            var candidateBudget = Math.Min(remainingBudget, maxPositionValue);
            var quantity = Math.Floor(candidateBudget / price);
            if (quantity <= 0m)
            {
                continue;
            }

            var estimatedCost = quantity * price;
            var stopLoss = price * 0.94m;
            var takeProfit = price * 1.18m;
            var riskReward = CalculateRiskReward(price, stopLoss, takeProfit);
            if (riskReward < 2.5m)
            {
                continue;
            }

            var trade = new TradeDecision
            {
                Ticker = opportunity.Symbol,
                Side = OrderSide.Buy,
                Quantity = quantity,
                EntryPrice = price,
                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                ConfidenceScore = opportunity.Confidence,
                Rating = opportunity.Rating,
                RiskReward = riskReward,
                TriggeringStrategy = "Portfolio automation",
                EligibleForExecution = true
            };

            var riskContext = new RiskContextSnapshot
            {
                Symbol = opportunity.Symbol,
                Sector = opportunity.Sector,
                LiquidityScore = BuildLiquidityScore(quote, opportunity),
                SpreadPercent = BuildSpreadPercent(quote),
                ObservedAtUtc = quote.TimestampUtc
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

    private async Task<MarketDataContract?> TryGetFreshQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var result = await _marketDataService.IngestAsync(symbol, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            return result.Data;
        }

        return null;
    }

    private static decimal CalculateRiskReward(decimal entryPrice, decimal stopLoss, decimal takeProfit)
    {
        if (entryPrice <= 0m || stopLoss >= entryPrice || takeProfit <= entryPrice)
        {
            return 0m;
        }

        var upside = takeProfit - entryPrice;
        var downside = entryPrice - stopLoss;
        if (downside <= 0m)
        {
            return 0m;
        }

        return Math.Round(upside / downside, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal BuildLiquidityScore(MarketDataContract quote, WatchlistOpportunity opportunity)
    {
        var volumeScore = quote.Volume is > 0m ? Math.Clamp((decimal)quote.Volume / 10_000_000m * 40m, 0m, 40m) : 0m;
        var confidenceScore = Math.Clamp(opportunity.Confidence * 0.5m, 0m, 30m);
        var liquidityScore = 50m + volumeScore + confidenceScore;
        return Math.Clamp(liquidityScore, 0m, 100m);
    }

    private static decimal BuildSpreadPercent(MarketDataContract quote)
    {
        if (quote.Price <= 0m)
        {
            return 0.10m;
        }

        if (quote.High is null or <= 0m || quote.Low is null or <= 0m)
        {
            return 0.10m;
        }

        var spreadPercent = Math.Abs((decimal)quote.High - (decimal)quote.Low) / quote.Price * 100m;
        return Math.Clamp(spreadPercent, 0.05m, 5m);
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
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        break;
                    }

                    await RunAutomationCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "The portfolio automation cycle failed and will retry on the next interval.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The portfolio automation loop failed fatally.");
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
