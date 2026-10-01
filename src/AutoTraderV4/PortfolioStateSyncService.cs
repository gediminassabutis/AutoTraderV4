namespace AutoTraderV4;

public sealed class PortfolioStateSyncService
{
    private static readonly TimeSpan MaximumSyncAge = TimeSpan.FromSeconds(15);

    private readonly IPortfolioRepository _repository;
    private readonly ITrading212Client _trading212Client;
    private readonly TimeProvider _timeProvider;
    private readonly bool _useDemoData;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private DateTimeOffset? _lastSuccessfulSyncUtc;
    private Trading212AccountSummary? _lastAccountSummary;

    public PortfolioStateSyncService(
        IPortfolioRepository repository,
        ITrading212Client trading212Client,
        TimeProvider timeProvider,
        bool useDemoData)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _trading212Client = trading212Client ?? throw new ArgumentNullException(nameof(trading212Client));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _useDemoData = useDemoData;
    }

    public async Task EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        if (_useDemoData)
        {
            return;
        }

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_lastSuccessfulSyncUtc is { } lastSync
                && now - lastSync is var age
                && age >= TimeSpan.Zero
                && age <= MaximumSyncAge)
            {
                return;
            }

            var account = await _trading212Client.GetAccountSummaryAsync(cancellationToken);
            var brokerPositions = await _trading212Client.GetPositionsAsync(cancellationToken);
            if (account.TotalValue <= 0m || string.IsNullOrWhiteSpace(account.Currency))
            {
                throw new InvalidOperationException("Trading 212 returned an invalid account summary; portfolio risk checks are blocked.");
            }

            var existingPositions = await _repository.GetAllPositionsAsync(cancellationToken);
            var existingByTicker = existingPositions.ToDictionary(
                position => position.Ticker,
                StringComparer.OrdinalIgnoreCase);
            var positions = brokerPositions.Select(position => MapPosition(position, existingByTicker, now)).ToList();
            var previousState = await _repository.GetPortfolioStateAsync(cancellationToken);
            var state = new PortfolioStateRecord
            {
                Id = previousState?.Id ?? Guid.NewGuid(),
                TotalValue = account.TotalValue,
                Cash = account.Cash,
                DailyProfitLoss = previousState?.DailyProfitLoss ?? 0m,
                WeeklyProfitLoss = previousState?.WeeklyProfitLoss ?? 0m,
                MonthlyProfitLoss = previousState?.MonthlyProfitLoss ?? 0m,
                PeakPortfolioValue = Math.Max(previousState?.PeakPortfolioValue ?? 0m, account.TotalValue),
                DefensiveModeActive = previousState?.DefensiveModeActive ?? false,
                UpdatedAtUtc = now
            };

            await _repository.ReplaceBrokerSnapshotAsync(state, positions, cancellationToken);
            _lastAccountSummary = account;
            _lastSuccessfulSyncUtc = now;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_useDemoData)
        {
            return await _trading212Client.GetAccountSummaryAsync(cancellationToken);
        }

        await EnsureFreshAsync(cancellationToken);
        return _lastAccountSummary
            ?? throw new InvalidOperationException("Trading 212 account summary is unavailable after portfolio synchronization.");
    }

    private static PortfolioPosition MapPosition(
        Trading212Position brokerPosition,
        IReadOnlyDictionary<string, PortfolioPosition> existingByTicker,
        DateTimeOffset updatedAtUtc)
    {
        var ticker = brokerPosition.Instrument?.Ticker?.Trim().ToUpperInvariant() ?? string.Empty;
        var walletImpact = brokerPosition.WalletImpact;
        if (string.IsNullOrWhiteSpace(ticker)
            || brokerPosition.Quantity <= 0m
            || walletImpact is null
            || string.IsNullOrWhiteSpace(walletImpact.Currency)
            || walletImpact.CurrentValue <= 0m
            || walletImpact.TotalCost <= 0m)
        {
            throw new InvalidOperationException("Trading 212 returned an incomplete position; portfolio risk checks are blocked.");
        }

        existingByTicker.TryGetValue(ticker, out var existing);
        return new PortfolioPosition
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Ticker = ticker,
            Sector = existing?.Sector ?? "Unknown",
            Quantity = brokerPosition.Quantity,
            AveragePrice = walletImpact.TotalCost / brokerPosition.Quantity,
            CurrentPrice = walletImpact.CurrentValue / brokerPosition.Quantity,
            Currency = walletImpact.Currency.Trim().ToUpperInvariant(),
            StopLoss = existing?.StopLoss ?? 0m,
            TakeProfit = existing?.TakeProfit ?? 0m,
            ConfidenceScore = existing?.ConfidenceScore ?? 0m,
            UpdatedAtUtc = updatedAtUtc
        };
    }
}
