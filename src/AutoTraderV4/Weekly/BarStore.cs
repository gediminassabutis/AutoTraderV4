using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4.Weekly;

public sealed class StoredDailyBar
{
    public string Symbol { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

/// <summary>Local SQLite store for downloaded daily bars, separate from the application database.</summary>
public sealed class BarStoreDbContext : DbContext
{
    public BarStoreDbContext(DbContextOptions<BarStoreDbContext> options)
        : base(options)
    {
    }

    public DbSet<StoredDailyBar> DailyBars => Set<StoredDailyBar>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredDailyBar>(entity =>
        {
            entity.ToTable("DailyBars");
            entity.HasKey(bar => new { bar.Symbol, bar.Date });
            entity.Property(bar => bar.Symbol).HasMaxLength(16);
        });
    }
}

public sealed class BarStore
{
    private const int InsertChunkSize = 5_000;
    private readonly IDbContextFactory<BarStoreDbContext> _contextFactory;
    private readonly SemaphoreSlim _initialisation = new(1, 1);
    private bool _initialised;

    public BarStore(IDbContextFactory<BarStoreDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <summary>The most recent stored date for each symbol that has any bars.</summary>
    public async Task<Dictionary<string, DateOnly>> GetLastDatesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        var rows = await context.DailyBars
            .AsNoTracking()
            .GroupBy(bar => bar.Symbol)
            .Select(group => new { Symbol = group.Key, Last = group.Max(bar => bar.Date) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Symbol, row => row.Last, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Adds bars newer than what is already stored for the symbol. Returns how many were added.</summary>
    public async Task<int> AppendAsync(
        string symbol,
        IEnumerable<DailyBar> bars,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(bars);
        var normalized = symbol.Trim().ToUpperInvariant();

        await using var context = await CreateContextAsync(cancellationToken);
        var lastStored = await context.DailyBars
            .Where(bar => bar.Symbol == normalized)
            .Select(bar => (DateOnly?)bar.Date)
            .MaxAsync(cancellationToken);
        var newBars = bars
            .Where(bar => lastStored is null || bar.Date > lastStored)
            .GroupBy(bar => bar.Date)
            .Select(group => group.Last())
            .OrderBy(bar => bar.Date)
            .ToArray();

        foreach (var chunk in newBars.Chunk(InsertChunkSize))
        {
            context.DailyBars.AddRange(chunk.Select(bar => new StoredDailyBar
            {
                Symbol = normalized,
                Date = bar.Date,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume
            }));
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }

        return newBars.Length;
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        return await context.DailyBars.ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<MarketDataSet> LoadAsync(string benchmarkSymbol, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);
        var rows = await context.DailyBars.AsNoTracking().ToListAsync(cancellationToken);
        var series = rows
            .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PriceSeries(
                group.Key,
                group.Select(row => new DailyBar(row.Date, row.Open, row.High, row.Low, row.Close, row.Volume))));
        return new MarketDataSet(series, benchmarkSymbol);
    }

    private async Task<BarStoreDbContext> CreateContextAsync(CancellationToken cancellationToken)
    {
        var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        if (_initialised)
        {
            return context;
        }

        await _initialisation.WaitAsync(cancellationToken);
        try
        {
            if (!_initialised)
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
                _initialised = true;
            }
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
        finally
        {
            _initialisation.Release();
        }

        return context;
    }
}

/// <summary>Downloads completed daily bars for many symbols at once.</summary>
public interface IDailyBarSource
{
    /// <summary>
    /// Bars dated from <paramref name="start"/> to <paramref name="end"/> inclusive, by symbol.
    /// Symbols the source has no data for are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>> GetDailyBarsAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);
}

public sealed class BarSyncResult
{
    public int SymbolsRequested { get; init; }
    public int SymbolsUpdated { get; init; }
    public int BarsAdded { get; init; }
    public DateOnly? UpToDate { get; init; }
    public IReadOnlyList<string> SymbolsWithoutData { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>Downloads history once for the configured universe, then appends new sessions on later runs.</summary>
public sealed class BarSyncService
{
    private readonly BarStore _store;
    private readonly IDailyBarSource _source;
    private readonly WeeklyOptions _options;
    private readonly TimeProvider _timeProvider;

    public BarSyncService(BarStore store, IDailyBarSource source, WeeklyOptions options, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<BarSyncResult> SyncAsync(bool fullRefresh = false, CancellationToken cancellationToken = default)
    {
        var symbols = _options.Universe.Symbols
            .Append(_options.BenchmarkSymbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Today's session may still be open, so only sessions before today (UTC) are stored.
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var end = today.AddDays(-1);
        var historyStart = today.AddYears(-_options.HistoryYears);
        var notes = new List<string>();
        if (fullRefresh)
        {
            var removed = await _store.DeleteAllAsync(cancellationToken);
            notes.Add($"Full refresh: removed {removed} stored bars before downloading.");
        }
        else
        {
            notes.Add("Bars are split- and dividend-adjusted when downloaded. After a stock split, run a full refresh so older bars are re-adjusted.");
        }

        var lastDates = await _store.GetLastDatesAsync(cancellationToken);
        var barsAdded = 0;
        var updated = 0;
        var withData = new HashSet<string>(lastDates.Keys, StringComparer.OrdinalIgnoreCase);

        // Symbols that share a start date are downloaded together.
        var groups = symbols
            .Select(symbol => (Symbol: symbol, Start: lastDates.TryGetValue(symbol, out var last) ? last.AddDays(1) : historyStart))
            .Where(item => item.Start <= end)
            .GroupBy(item => item.Start);
        foreach (var group in groups)
        {
            foreach (var batch in group.Select(item => item.Symbol).Chunk(_options.SyncBatchSize))
            {
                var downloaded = await _source.GetDailyBarsAsync(batch, group.Key, end, cancellationToken);
                foreach (var (symbol, bars) in downloaded)
                {
                    var added = await _store.AppendAsync(
                        symbol,
                        bars.Where(bar => bar.Date <= end),
                        cancellationToken);
                    if (added > 0)
                    {
                        barsAdded += added;
                        updated++;
                        withData.Add(symbol);
                    }
                }
            }
        }

        return new BarSyncResult
        {
            SymbolsRequested = symbols.Length,
            SymbolsUpdated = updated,
            BarsAdded = barsAdded,
            UpToDate = end,
            SymbolsWithoutData = symbols.Where(symbol => !withData.Contains(symbol)).ToArray(),
            Notes = notes
        };
    }
}
