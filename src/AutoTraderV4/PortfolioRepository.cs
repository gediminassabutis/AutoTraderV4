using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4;

public interface IPortfolioRepository
{
    Task<List<PortfolioPosition>> GetAllPositionsAsync(CancellationToken cancellationToken = default);
    Task UpsertPositionAsync(PortfolioPosition position, CancellationToken cancellationToken = default);
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

    public async Task UpsertPositionAsync(PortfolioPosition position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);

        var existing = await _context.Positions
            .FirstOrDefaultAsync(x => x.Ticker == position.Ticker, cancellationToken);

        if (existing is null)
        {
            position.Id = Guid.NewGuid();
            _context.Positions.Add(position);
        }
        else
        {
            existing.Quantity = position.Quantity;
            existing.AveragePrice = position.AveragePrice;
            existing.Currency = position.Currency;
            existing.UpdatedAtUtc = position.UpdatedAtUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
