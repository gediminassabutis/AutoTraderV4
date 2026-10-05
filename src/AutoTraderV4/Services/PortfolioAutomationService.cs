using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    public decimal FinalScore { get; set; }
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
    private const int MaximumRecentSignals = 200;
    private const int MaximumCandidates = 20;
    private const int MinimumFinalScore = 75;
    private const int MinimumConfidenceScore = 80;
    private static readonly TimeSpan MaximumStrategySignalAge = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _context;
    private readonly IPortfolioRepository _repository;
    private readonly RiskGovernanceService _riskGovernanceService;
    private readonly PortfolioStateSyncService _portfolioStateSyncService;
    private readonly MarketDataService _marketDataService;
    private readonly ILogger<BuyOpportunityService> _logger;

    public BuyOpportunityService(
        ApplicationDbContext context,
        IPortfolioRepository repository,
        RiskGovernanceService riskGovernanceService,
        PortfolioStateSyncService portfolioStateSyncService,
        MarketDataService marketDataService,
        ILogger<BuyOpportunityService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _riskGovernanceService = riskGovernanceService ?? throw new ArgumentNullException(nameof(riskGovernanceService));
        _portfolioStateSyncService = portfolioStateSyncService ?? throw new ArgumentNullException(nameof(portfolioStateSyncService));
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<BuyOpportunityDecision>> ScanForBuysAsync(CancellationToken cancellationToken = default)
    {
        var marketDataReadiness = _marketDataService.GetReadiness();
        if (!marketDataReadiness.LiveProviderConfigured)
        {
            _logger.LogWarning(
                "Buy-opportunity scanning is disabled. {MarketDataMessage}",
                marketDataReadiness.Message);
            return [];
        }

        await _portfolioStateSyncService.EnsureFreshAsync(cancellationToken);
        var positions = await _repository.GetAllPositionsAsync(cancellationToken);
        var state = await _repository.GetPortfolioStateAsync(cancellationToken);
        if (state is null || state.Cash <= 0m || state.TotalValue <= 0m)
        {
            return [];
        }

        var openTickers = new HashSet<string>(
            positions.Select(position => position.Ticker.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var recentSignals = await _context.StrategySignals
            .AsNoTracking()
            .OrderByDescending(signal => signal.CreatedUtc)
            .Take(MaximumRecentSignals)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var signalCandidates = recentSignals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Ticker))
            .GroupBy(signal => signal.Ticker.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(signal => string.Equals(signal.Side, OrderSide.Buy.ToString(), StringComparison.OrdinalIgnoreCase))
            .Where(signal => signal.CreatedUtc <= now && now - signal.CreatedUtc <= MaximumStrategySignalAge)
            .Where(signal => !openTickers.Contains(signal.Ticker.Trim()))
            .OrderByDescending(signal => signal.CreatedUtc)
            .Take(MaximumCandidates)
            .Select(signal => new BuySignalCandidate(
                signal.Ticker.Trim().ToUpperInvariant(),
                signal.Signal))
            .ToList();

        var candidates = new List<BuyCandidate>();
        foreach (var signal in signalCandidates)
        {
            var ingestion = await _marketDataService.IngestAsync(signal.Symbol, cancellationToken);
            if (!ingestion.Success
                || ingestion.FreshnessStatus != DataFreshnessStatus.Fresh
                || ingestion.Data is null)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; no fresh market data is available. Provider failures: {Failures}",
                    signal.Symbol,
                    string.Join("; ", ingestion.Failures));
                continue;
            }

            var marketData = ingestion.Data;
            if (marketData.IsSynthetic)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; the only available market data is synthetic from {Provider}.",
                    signal.Symbol,
                    marketData.ProviderName);
                continue;
            }

            if (marketData.Price <= 0m
                || !marketData.LiquidityScore.HasValue
                || !marketData.SpreadPercent.HasValue)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; market data is missing a positive price, measured liquidity, or measured spread.",
                    signal.Symbol);
                continue;
            }

            var sector = ResolveSector(signal.Symbol, marketData);
            if (sector is null)
            {
                _logger.LogWarning(
                    "Skipping {Ticker}; no sector classification is available from the provider or supported symbol mappings.",
                    signal.Symbol);
                continue;
            }

            var normalizedSignal = StrategySignalScoring.NormalizePriceDelta(signal.PriceDelta, marketData.Price);
            var finalScore = StrategySignalScoring.CalculateSignedScore(normalizedSignal);
            var confidenceScore = StrategySignalScoring.CalculateConfidence(normalizedSignal);
            var rating = StrategySignalScoring.GetRating(finalScore);
            if (normalizedSignal <= 0m
                || finalScore < MinimumFinalScore
                || confidenceScore < MinimumConfidenceScore
                || rating is not ("Buy" or "Strong Buy"))
            {
                _logger.LogDebug(
                    "Skipping {Ticker}; normalized signal {SignalPercent:F4}% scores {FinalScore} with confidence {ConfidenceScore}.",
                    signal.Symbol,
                    normalizedSignal,
                    finalScore,
                    confidenceScore);
                continue;
            }

            candidates.Add(new BuyCandidate(
                signal.Symbol,
                marketData.Price,
                sector,
                finalScore,
                confidenceScore,
                rating,
                normalizedSignal * 8m,
                marketData.LiquidityScore.Value,
                marketData.SpreadPercent.Value,
                marketData.TimestampUtc));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        await _portfolioStateSyncService.EnsureFreshAsync(cancellationToken);
        state = await _repository.GetPortfolioStateAsync(cancellationToken);
        positions = await _repository.GetAllPositionsAsync(cancellationToken);
        if (state is null || state.Cash <= 0m || state.TotalValue <= 0m)
        {
            return [];
        }

        var projectedState = CloneState(state);
        var projectedPositions = positions.Select(ClonePositionForRisk).ToList();
        openTickers = new HashSet<string>(
            positions.Select(position => position.Ticker.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var remainingBudget = Math.Min(state.Cash, state.Cash * 0.5m);
        var decisions = new List<BuyOpportunityDecision>();

        foreach (var opportunity in candidates
                     .OrderByDescending(candidate => candidate.ConfidenceScore)
                     .ThenByDescending(candidate => candidate.FinalScore))
        {
            if (remainingBudget <= 0m)
            {
                break;
            }

            if (openTickers.Contains(opportunity.Symbol))
            {
                continue;
            }

            var price = opportunity.Price;
            var maxPositionBudget = Math.Min(remainingBudget, state.TotalValue * 0.05m);
            var quantity = Math.Floor(maxPositionBudget / price);
            if (quantity <= 0m)
            {
                continue;
            }

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
                ConfidenceScore = opportunity.ConfidenceScore,
                Rating = opportunity.Rating,
                FinalScore = opportunity.FinalScore,
                RiskReward = Math.Round(riskReward, 2, MidpointRounding.AwayFromZero),
                TriggeringStrategy = "Portfolio automation",
                EligibleForExecution = true
            };

            var riskContext = new RiskContextSnapshot
            {
                Symbol = opportunity.Symbol,
                Sector = opportunity.Sector,
                LiquidityScore = opportunity.LiquidityScore,
                SpreadPercent = opportunity.SpreadPercent,
                ObservedAtUtc = opportunity.ObservedAtUtc
            };

            var riskAssessment = _riskGovernanceService.EvaluateProjected(
                trade,
                riskContext,
                projectedState,
                projectedPositions);
            if (!riskAssessment.Approved)
            {
                _logger.LogInformation(
                    "Skipping {Ticker}; portfolio risk checks rejected the candidate: {Violations}",
                    opportunity.Symbol,
                    string.Join("; ", riskAssessment.Violations));
                continue;
            }

            decisions.Add(new BuyOpportunityDecision
            {
                Ticker = opportunity.Symbol,
                Price = price,
                Quantity = quantity,
                EstimatedCost = estimatedCost,
                CashBudget = remainingBudget,
                FinalScore = opportunity.FinalScore,
                ConfidenceScore = opportunity.ConfidenceScore,
                Rating = opportunity.Rating,
                Reason = $"{opportunity.Rating} with {opportunity.ForecastReturn:F2}% expected return and acceptable risk."
            });

            projectedState.Cash -= estimatedCost;
            projectedPositions.Add(new PortfolioPosition
            {
                Id = Guid.NewGuid(),
                Ticker = opportunity.Symbol,
                Sector = opportunity.Sector,
                Quantity = quantity,
                AveragePrice = price,
                CurrentPrice = price
            });
            openTickers.Add(opportunity.Symbol);
            remainingBudget -= estimatedCost;
        }

        return decisions;
    }

    private static string? ResolveSector(string symbol, MarketDataContract marketData)
    {
        var providerSector = marketData.Metadata.TryGetValue("sector", out var sector)
            ? sector switch
            {
                string sectorName => sectorName,
                JsonElement { ValueKind: JsonValueKind.String } jsonSector => jsonSector.GetString(),
                _ => null
            }
            : null;

        return SectorClassification.Resolve(symbol, providerSector);
    }

    private static PortfolioStateRecord CloneState(PortfolioStateRecord state)
    {
        return new PortfolioStateRecord
        {
            Id = state.Id,
            TotalValue = state.TotalValue,
            Cash = state.Cash,
            DailyProfitLoss = state.DailyProfitLoss,
            WeeklyProfitLoss = state.WeeklyProfitLoss,
            MonthlyProfitLoss = state.MonthlyProfitLoss,
            PeakPortfolioValue = state.PeakPortfolioValue,
            DefensiveModeActive = state.DefensiveModeActive,
            UpdatedAtUtc = state.UpdatedAtUtc
        };
    }

    private static PortfolioPosition ClonePositionForRisk(PortfolioPosition position)
    {
        return new PortfolioPosition
        {
            Id = position.Id,
            Ticker = position.Ticker,
            Sector = SectorClassification.Resolve(position.Ticker, persistedSector: position.Sector)
                ?? SectorClassification.Unclassified,
            Quantity = position.Quantity,
            AveragePrice = position.AveragePrice,
            CurrentPrice = position.CurrentPrice,
            Currency = position.Currency,
            StopLoss = position.StopLoss,
            TakeProfit = position.TakeProfit,
            ConfidenceScore = position.ConfidenceScore,
            UpdatedAtUtc = position.UpdatedAtUtc
        };
    }

    private sealed record BuySignalCandidate(string Symbol, decimal PriceDelta);

    private sealed record BuyCandidate(
        string Symbol,
        decimal Price,
        string Sector,
        int FinalScore,
        int ConfidenceScore,
        string Rating,
        decimal ForecastReturn,
        decimal LiquidityScore,
        decimal SpreadPercent,
        DateTimeOffset ObservedAtUtc);
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
        var stockTrackingService = scope.ServiceProvider.GetRequiredService<StockTrackingService>();
        var cycleResult = await stockTrackingService.RunCycleAsync(cancellationToken);

        foreach (var ticker in cycleResult.UpdatedTickers)
        {
            _logger.LogDebug("Updated the current market tick for {Ticker}.", ticker);
        }

        foreach (var decision in cycleResult.SellRecommendations)
        {
            _logger.LogInformation(
                "Portfolio review: {Recommendation} {Ticker}. {Reason}",
                decision.Recommendation,
                decision.Ticker,
                decision.Reason);
        }

        foreach (var decision in cycleResult.BuyOpportunities)
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
