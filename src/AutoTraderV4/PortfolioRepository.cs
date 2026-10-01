using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4;

public interface IPortfolioRepository
{
    Task<List<PortfolioPosition>> GetAllPositionsAsync(CancellationToken cancellationToken = default);
    Task<PortfolioStateRecord?> GetPortfolioStateAsync(CancellationToken cancellationToken = default);
    Task UpsertPositionAsync(PortfolioPosition position, CancellationToken cancellationToken = default);
    Task UpsertPortfolioStateAsync(PortfolioStateRecord state, CancellationToken cancellationToken = default);
    Task ReplaceBrokerSnapshotAsync(
        PortfolioStateRecord state,
        IReadOnlyCollection<PortfolioPosition> positions,
        CancellationToken cancellationToken = default);
}

public sealed class PortfolioRepository : IPortfolioRepository
{
    private readonly ApplicationDbContext _context;

    public PortfolioRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PortfolioPosition>> GetAllPositionsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Positions
            .AsNoTracking()
            .OrderBy(x => x.Ticker)
            .ToListAsync(cancellationToken);
    }

    public async Task<PortfolioStateRecord?> GetPortfolioStateAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PortfolioStates
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertPositionAsync(PortfolioPosition position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);

        var normalizedTicker = position.Ticker.Trim().ToUpperInvariant();
        var existing = await _context.Positions
            .FirstOrDefaultAsync(x => x.Ticker == normalizedTicker, cancellationToken);

        if (existing is null)
        {
            position.Id = Guid.NewGuid();
            position.Ticker = normalizedTicker;
            position.Sector = position.Sector.Trim();
            position.Currency = position.Currency.Trim().ToUpperInvariant();
            _context.Positions.Add(position);
        }
        else
        {
            existing.Quantity = position.Quantity;
            existing.AveragePrice = position.AveragePrice;
            existing.CurrentPrice = position.CurrentPrice;
            existing.Currency = position.Currency.Trim().ToUpperInvariant();
            existing.Sector = position.Sector.Trim();
            existing.StopLoss = position.StopLoss;
            existing.TakeProfit = position.TakeProfit;
            existing.ConfidenceScore = position.ConfidenceScore;
            existing.UpdatedAtUtc = position.UpdatedAtUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertPortfolioStateAsync(PortfolioStateRecord state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var existing = await _context.PortfolioStates
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            state.Id = Guid.NewGuid();
            _context.PortfolioStates.Add(state);
        }
        else
        {
            existing.TotalValue = state.TotalValue;
            existing.Cash = state.Cash;
            existing.DailyProfitLoss = state.DailyProfitLoss;
            existing.WeeklyProfitLoss = state.WeeklyProfitLoss;
            existing.MonthlyProfitLoss = state.MonthlyProfitLoss;
            existing.PeakPortfolioValue = state.PeakPortfolioValue;
            existing.DefensiveModeActive = state.DefensiveModeActive;
            existing.UpdatedAtUtc = state.UpdatedAtUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceBrokerSnapshotAsync(
        PortfolioStateRecord state,
        IReadOnlyCollection<PortfolioPosition> positions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(positions);

        if (positions.Any(position => string.IsNullOrWhiteSpace(position.Ticker)))
        {
            throw new ArgumentException("Every broker position must have a ticker.", nameof(positions));
        }

        if (positions
            .GroupBy(position => position.Ticker.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Broker positions must not contain duplicate tickers.", nameof(positions));
        }

        if (_context.Database.IsRelational())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await ApplyBrokerSnapshotAsync(state, positions, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await ApplyBrokerSnapshotAsync(state, positions, cancellationToken);
    }

    private async Task ApplyBrokerSnapshotAsync(
        PortfolioStateRecord state,
        IReadOnlyCollection<PortfolioPosition> positions,
        CancellationToken cancellationToken)
    {
        var existingPositions = await _context.Positions.ToListAsync(cancellationToken);
        var incomingByTicker = positions.ToDictionary(
            position => position.Ticker.Trim().ToUpperInvariant(),
            StringComparer.OrdinalIgnoreCase);
        var existingByTicker = existingPositions.ToDictionary(
            position => position.Ticker,
            StringComparer.OrdinalIgnoreCase);

        foreach (var existing in existingPositions)
        {
            if (!incomingByTicker.ContainsKey(existing.Ticker))
            {
                _context.Positions.Remove(existing);
            }
        }

        foreach (var (ticker, incoming) in incomingByTicker)
        {
            incoming.Ticker = ticker;
            if (existingByTicker.TryGetValue(ticker, out var existing))
            {
                existing.Sector = incoming.Sector;
                existing.Quantity = incoming.Quantity;
                existing.AveragePrice = incoming.AveragePrice;
                existing.CurrentPrice = incoming.CurrentPrice;
                existing.Currency = incoming.Currency;
                existing.StopLoss = incoming.StopLoss;
                existing.TakeProfit = incoming.TakeProfit;
                existing.ConfidenceScore = incoming.ConfidenceScore;
                existing.UpdatedAtUtc = incoming.UpdatedAtUtc;
            }
            else
            {
                incoming.Id = incoming.Id == Guid.Empty ? Guid.NewGuid() : incoming.Id;
                _context.Positions.Add(incoming);
            }
        }

        var existingState = await _context.PortfolioStates
            .OrderByDescending(record => record.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingState is null)
        {
            state.Id = state.Id == Guid.Empty ? Guid.NewGuid() : state.Id;
            _context.PortfolioStates.Add(state);
        }
        else
        {
            existingState.TotalValue = state.TotalValue;
            existingState.Cash = state.Cash;
            existingState.DailyProfitLoss = state.DailyProfitLoss;
            existingState.WeeklyProfitLoss = state.WeeklyProfitLoss;
            existingState.MonthlyProfitLoss = state.MonthlyProfitLoss;
            existingState.PeakPortfolioValue = state.PeakPortfolioValue;
            existingState.DefensiveModeActive = state.DefensiveModeActive;
            existingState.UpdatedAtUtc = state.UpdatedAtUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
