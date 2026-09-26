using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4;

public sealed class AuditLoggingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;

    public AuditLoggingService(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<TradeAuditLogResponse> LogStrategyEvaluationAsync(
        StrategyEvaluationRequest request,
        TradeDecision decision,
        RiskAssessmentResult riskAssessment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(riskAssessment);

        var recommendationPayload = new
        {
            symbol = decision.Ticker,
            sector = request.Sector,
            rating = decision.Rating,
            confidence = decision.Confidence,
            entryPrice = decision.EntryPrice,
            stopLoss = decision.StopLoss,
            takeProfit = decision.TakeProfit,
            riskReward = decision.RiskReward,
            topFactors = decision.TopFactors,
            signalScores = decision.SignalScores,
            forecast = decision.Forecast,
            forecastSummary = decision.ForecastOutput,
            eligibleForExecution = decision.EligibleForExecution,
            finalScore = decision.FinalScore,
            triggeringStrategy = decision.TriggeringStrategy
        };

        var entry = new TradeAuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = "strategy-evaluation",
            Symbol = decision.Ticker,
            StrategyName = string.Join(", ", decision.StrategyBreakdown.Select(x => x.Strategy)),
            FinalScore = decision.FinalScore,
            ConfidenceScore = decision.ConfidenceScore,
            EntryPrice = decision.EntryPrice,
            StopLoss = decision.StopLoss,
            TakeProfit = decision.TakeProfit,
            Quantity = decision.Quantity,
            Side = decision.Side.ToString(),
            OrderType = decision.OrderType.ToString(),
            SignalScoresJson = JsonSerializer.Serialize(decision.SignalScores, JsonOptions),
            ForecastJson = JsonSerializer.Serialize(decision.Forecast, JsonOptions),
            RiskAssessmentJson = JsonSerializer.Serialize(riskAssessment, JsonOptions),
            RecommendationJson = JsonSerializer.Serialize(recommendationPayload, JsonOptions),
            CreatedUtc = decision.GeneratedAtUtc
        };

        _context.TradeAuditEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return Map(entry);
    }

    public async Task<TradeAuditLogResponse> LogRiskEvaluationAsync(
        TradeDecision decision,
        RiskContextSnapshot context,
        RiskAssessmentResult riskAssessment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(riskAssessment);

        var entry = new TradeAuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = "risk-evaluation",
            Symbol = decision.Ticker,
            StrategyName = "Risk Governance",
            FinalScore = decision.FinalScore,
            ConfidenceScore = decision.ConfidenceScore,
            EntryPrice = decision.EntryPrice,
            StopLoss = decision.StopLoss,
            TakeProfit = decision.TakeProfit,
            Quantity = decision.Quantity,
            Side = decision.Side.ToString(),
            OrderType = decision.OrderType.ToString(),
            SignalScoresJson = JsonSerializer.Serialize(context, JsonOptions),
            ForecastJson = JsonSerializer.Serialize(decision.Forecast, JsonOptions),
            RiskAssessmentJson = JsonSerializer.Serialize(riskAssessment, JsonOptions),
            RecommendationJson = JsonSerializer.Serialize(decision, JsonOptions),
            CreatedUtc = DateTimeOffset.UtcNow
        };

        _context.TradeAuditEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return Map(entry);
    }

    public async Task<TradeAuditLogResponse> LogPortfolioStateAsync(
        PortfolioStateRecord state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var entry = new TradeAuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = "portfolio-state-upsert",
            Symbol = "PORTFOLIO",
            StrategyName = "Portfolio API",
            FinalScore = 0m,
            ConfidenceScore = 0m,
            EntryPrice = 0m,
            StopLoss = 0m,
            TakeProfit = 0m,
            Quantity = 0m,
            Side = string.Empty,
            OrderType = string.Empty,
            SignalScoresJson = "{}",
            ForecastJson = "{}",
            RiskAssessmentJson = "{}",
            RecommendationJson = JsonSerializer.Serialize(state, JsonOptions),
            CreatedUtc = state.UpdatedAtUtc
        };

        _context.TradeAuditEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return Map(entry);
    }

    public async Task<TradeAuditLogResponse> LogPortfolioPositionAsync(
        PortfolioPosition position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);

        var entry = new TradeAuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = "portfolio-position-upsert",
            Symbol = position.Ticker,
            StrategyName = "Portfolio API",
            FinalScore = 0m,
            ConfidenceScore = position.ConfidenceScore,
            EntryPrice = position.CurrentPrice,
            StopLoss = position.StopLoss,
            TakeProfit = position.TakeProfit,
            Quantity = position.Quantity,
            Side = position.Quantity >= 0m ? OrderSide.Buy.ToString() : OrderSide.Sell.ToString(),
            OrderType = string.Empty,
            SignalScoresJson = "{}",
            ForecastJson = "{}",
            RiskAssessmentJson = "{}",
            RecommendationJson = JsonSerializer.Serialize(position, JsonOptions),
            CreatedUtc = position.UpdatedAtUtc
        };

        _context.TradeAuditEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return Map(entry);
    }

    public async Task<List<TradeAuditLogResponse>> GetRecentAsync(
        string? symbol,
        int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = _context.TradeAuditEntries
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalizedSymbol = symbol.Trim().ToUpperInvariant();
            query = query.Where(x => x.Symbol == normalizedSymbol);
        }

        var entries = await query
            .Take(take)
            .ToListAsync(cancellationToken);

        return entries
            .Select(Map)
            .ToList();
    }

    private static TradeAuditLogResponse Map(TradeAuditEntry entry)
    {
        return new TradeAuditLogResponse
        {
            Id = entry.Id,
            EventType = entry.EventType,
            Symbol = entry.Symbol,
            FinalScore = entry.FinalScore,
            ConfidenceScore = entry.ConfidenceScore,
            EntryPrice = entry.EntryPrice,
            StopLoss = entry.StopLoss,
            TakeProfit = entry.TakeProfit,
            Quantity = entry.Quantity,
            Side = entry.Side,
            OrderType = entry.OrderType,
            CreatedUtc = entry.CreatedUtc,
            StrategyName = entry.StrategyName,
            SignalScoresJson = entry.SignalScoresJson,
            ForecastJson = entry.ForecastJson,
            RiskAssessmentJson = entry.RiskAssessmentJson,
            RecommendationJson = entry.RecommendationJson
        };
    }
}
