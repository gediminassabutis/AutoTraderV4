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
    private readonly IPortfolioDashboardService _dashboardService;
    private readonly RiskGovernanceService _riskGovernanceService;
    private readonly PortfolioStateSyncService _portfolioStateSyncService;
    private readonly MarketDataService _marketDataService;
    private readonly ILogger<BuyOpportunityService> _logger;

    public BuyOpportunityService(
        IPortfolioRepository repository,
        IPortfolioDashboardService dashboardService,
        RiskGovernanceService riskGovernanceService,
        PortfolioStateSyncService portfolioStateSyncService,
        MarketDataService marketDataService,
        ILogger<BuyOpportunityService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
        _riskGovernanceService = riskGovernanceService ?? throw new ArgumentNullException(nameof(riskGovernanceService));
        _portfolioStateSyncService = portfolioStateSyncService ?? throw new ArgumentNullException(nameof(portfolioStateSyncService));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<BuyOpportunityDecision>> ScanForBuysAsync(CancellationToken cancellationToken = default)
    {
        await _portfolioStateSyncService.EnsureFreshAsync(cancellationToken);
        var state = await _repository.GetPortfolioStateAsync(cancellationToken);
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        var availableCash = state?.Cash ?? 0m;

        if (availableCash <= 0m || state is null || state.TotalValue <= 0m)
        {
            return [];
        }

        var dashboard = await _dashboardService.GetDashboardAsync(cancellationToken);
        var buyBudget = Math.Min(availableCash, availableCash * 0.5m);
        var openTickers = new HashSet<string>(positions.Select(x => x.Ticker), StringComparer.OrdinalIgnoreCase);
        var decisions = new List<BuyOpportunityDecision>();
        var remainingBudget = buyBudget;

        foreach (var opportunity in dashboard.Watchlist
                     .Where(opportunity => !opportunity.IsSynthetic)
                     .Where(x => !openTickers.Contains(x.Symbol))
                     .OrderByDescending(x => x.Confidence))
        {
            if (remainingBudget <= 0m)
            {
                break;
            }

            var ingestion = await _marketDataService.IngestAsync(opportunity.Symbol, cancellationToken);
            if (!ingestion.Success || ingestion.Data is null)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; no fresh market data is available. Provider failures: {Failures}",
                    opportunity.Symbol,
                    string.Join("; ", ingestion.Failures));
                continue;
            }

            var marketData = ingestion.Data;
            if (marketData.IsSynthetic)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; the only available market data is synthetic from {Provider}.",
                    opportunity.Symbol,
                    marketData.ProviderName);
                continue;
            }

            if (opportunity.IsSynthetic)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; strategy scores are synthetic and cannot be used to approve an entry.",
                    opportunity.Symbol);
                continue;
            }

            if (marketData.Price <= 0m
                || !marketData.LiquidityScore.HasValue
                || !marketData.SpreadPercent.HasValue)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; market data is missing a positive price, measured liquidity, or measured spread.",
                    opportunity.Symbol);
                continue;
            }

            var price = marketData.Price;
            var maxPositionBudget = Math.Min(remainingBudget, state.TotalValue * 0.05m);
            var maxQuantity = Math.Floor(maxPositionBudget / price);
            if (maxQuantity <= 0m)
            {
                continue;
            }

            var quantity = maxQuantity;
            var estimatedCost = quantity * price;
            var stopLoss = price * 0.94m;
            var takeProfit = price * 1.18m;
            var riskReward = (takeProfit - price) / (price - stopLoss);
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
                FinalScore = opportunity.Confidence,
                RiskReward = Math.Round(riskReward, 2, MidpointRounding.AwayFromZero),
                TriggeringStrategy = "Portfolio automation",
                EligibleForExecution = true
            };

            var riskContext = new RiskContextSnapshot
            {
                Symbol = opportunity.Symbol,
                Sector = opportunity.Sector,
                LiquidityScore = marketData.LiquidityScore.Value,
                SpreadPercent = marketData.SpreadPercent.Value,
                ObservedAtUtc = marketData.TimestampUtc
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
    private readonly TimeSpan _reviewInterval;

    public PortfolioAutomationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PortfolioAutomationBackgroundService> logger,
        TimeSpan? reviewInterval = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reviewInterval = reviewInterval ?? ReviewInterval;
        if (_reviewInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reviewInterval), "Review interval must be positive.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_reviewInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAutomationCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The portfolio automation cycle failed; the worker will retry after the next interval.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
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
