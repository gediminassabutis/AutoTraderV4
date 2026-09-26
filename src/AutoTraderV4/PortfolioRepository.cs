using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4;

public interface IPortfolioRepository
{
    Task<List<PortfolioPosition>> GetAllPositionsAsync(CancellationToken cancellationToken = default);
    Task<PortfolioStateRecord?> GetPortfolioStateAsync(CancellationToken cancellationToken = default);
    Task UpsertPositionAsync(PortfolioPosition position, CancellationToken cancellationToken = default);
    Task UpsertPortfolioStateAsync(PortfolioStateRecord state, CancellationToken cancellationToken = default);
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
}
