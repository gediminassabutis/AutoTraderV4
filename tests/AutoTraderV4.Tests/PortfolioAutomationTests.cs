using AutoTraderV4.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoTraderV4.Tests;

public sealed class PortfolioAutomationTests
{
    [Fact]
    public async Task BackgroundService_ContinuesAfterTransientCycleFailure()
    {
        var repository = new FailOncePortfolioRepository();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton<IPortfolioRepository>(repository);
        services.AddSingleton<ITrading212Client, DemoTrading212Client>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<StrategyEngineService>();
        services.AddSingleton<IMarketDataProvider, DemoMarketDataProvider>();
        services.AddScoped<MarketDataService>();
        services.AddScoped(provider => new PortfolioStateSyncService(
            provider.GetRequiredService<IPortfolioRepository>(),
            provider.GetRequiredService<ITrading212Client>(),
            provider.GetRequiredService<TimeProvider>(),
            useDemoData: true));
        services.AddScoped(provider => new RiskGovernanceService(
            provider.GetRequiredService<IPortfolioRepository>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<PortfolioStateSyncService>(),
            useDemoData: true));
        services.AddScoped<IPortfolioDashboardService, PortfolioDashboardService>();
        services.AddScoped<PortfolioReviewService>();
        services.AddScoped<BuyOpportunityService>();
        services.AddSingleton<StockTrackingCycleGate>();
        services.AddScoped<StockTrackingService>();

        await using var provider = services.BuildServiceProvider();
        var worker = new PortfolioAutomationBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<PortfolioAutomationBackgroundService>>(),
            TimeSpan.FromMilliseconds(20));

        await worker.StartAsync(CancellationToken.None);
        await repository.SecondCycleReached.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(repository.GetAllPositionsCalls >= 2);
        Assert.False(worker.ExecuteTask?.IsCompleted ?? true);

        await worker.StopAsync(CancellationToken.None);
    }

    private sealed class FailOncePortfolioRepository : IPortfolioRepository
    {
        private int _getAllPositionsCalls;

        public TaskCompletionSource<bool> SecondCycleReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int GetAllPositionsCalls => Volatile.Read(ref _getAllPositionsCalls);

        public Task<List<PortfolioPosition>> GetAllPositionsAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _getAllPositionsCalls);
            if (call == 1)
            {
                throw new InvalidOperationException("Simulated transient repository failure.");
            }

            SecondCycleReached.TrySetResult(true);
            return Task.FromResult(new List<PortfolioPosition>());
        }

        public Task<PortfolioStateRecord?> GetPortfolioStateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PortfolioStateRecord?>(null);
        }

        public Task UpsertPositionAsync(PortfolioPosition position, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpsertPortfolioStateAsync(PortfolioStateRecord state, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ReplaceBrokerSnapshotAsync(
            PortfolioStateRecord state,
            IReadOnlyCollection<PortfolioPosition> positions,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
