using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4.Tests;

public sealed class PortfolioStateSyncTests
{
    [Fact]
    public async Task EnsureFreshAsync_ReplacesLocalPositionsWithBrokerSnapshot()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options);
        var repository = new PortfolioRepository(context);
        await repository.UpsertPortfolioStateAsync(new PortfolioStateRecord
        {
            TotalValue = 50000m,
            Cash = 10000m,
            DailyProfitLoss = -100m,
            PeakPortfolioValue = 51000m,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await repository.UpsertPositionAsync(new PortfolioPosition
        {
            Ticker = "NVDA_US_EQ",
            Sector = "Technology",
            Quantity = 10m,
            AveragePrice = 700m,
            CurrentPrice = 750m,
            Currency = "USD",
            StopLoss = 700m,
            TakeProfit = 1000m,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await repository.UpsertPositionAsync(new PortfolioPosition
        {
            Ticker = "STALE_US_EQ",
            Sector = "Unknown",
            Quantity = 2m,
            AveragePrice = 100m,
            CurrentPrice = 100m,
            Currency = "USD",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        var broker = new FakeTrading212Client();
        var synchronizer = new PortfolioStateSyncService(repository, broker, TimeProvider.System, useDemoData: false);

        await synchronizer.EnsureFreshAsync();

        var positions = await repository.GetAllPositionsAsync();
        var position = Assert.Single(positions);
        Assert.Equal("NVDA_US_EQ", position.Ticker);
        Assert.Equal(100m, position.Quantity);
        Assert.Equal(800m, position.AveragePrice);
        Assert.Equal(900m, position.CurrentPrice);
        Assert.Equal("Technology", position.Sector);
        Assert.Equal(700m, position.StopLoss);
        Assert.Equal(1000m, position.TakeProfit);

        var state = await repository.GetPortfolioStateAsync();
        Assert.NotNull(state);
        Assert.Equal(100000m, state.TotalValue);
        Assert.Equal(10000m, state.Cash);
        Assert.Equal(-100m, state.DailyProfitLoss);
    }

    private sealed class FakeTrading212Client : ITrading212Client
    {
        public Task<Trading212AccountSummary> GetAccountSummaryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Trading212AccountSummary
            {
                Currency = "USD",
                TotalValue = 100000m,
                Cash = 10000m
            });
        }

        public Task<IReadOnlyList<Trading212Position>> GetPositionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Trading212Position>>(
            [
                new Trading212Position
                {
                    Instrument = new Trading212Instrument { Ticker = "NVDA_US_EQ" },
                    Quantity = 100m,
                    AveragePricePaid = 800m,
                    CurrentPrice = 900m,
                    WalletImpact = new Trading212PositionWalletImpact
                    {
                        Currency = "USD",
                        TotalCost = 80000m,
                        CurrentValue = 90000m
                    }
                }
            ]);
        }

        public Task<Trading212OrderResult> PlaceOrderAsync(Trading212OrderRequest order, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
