using Microsoft.EntityFrameworkCore;

namespace AutoTraderV4.Tests;

public sealed class ApplicationDbContextTests
{
    [Fact]
    public void SaveChanges_NormalizesDateTimeOffsetsToUtc()
    {
        var timestamp = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(2));
        using var context = CreateContext();
        var state = new PortfolioStateRecord { UpdatedAtUtc = timestamp };
        var stock = new TrackedStock { Ticker = "AAPL", AddedOn = timestamp };
        context.AddRange(state, stock);

        context.SaveChanges();

        AssertUtc(timestamp, state.UpdatedAtUtc);
        Assert.NotNull(stock.AddedOn);
        AssertUtc(timestamp, stock.AddedOn!.Value);
    }

    [Fact]
    public async Task SaveChangesAsync_NormalizesDateTimeOffsetsToUtc()
    {
        var timestamp = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(2));
        await using var context = CreateContext();
        var state = new PortfolioStateRecord { UpdatedAtUtc = timestamp };
        var stock = new TrackedStock { Ticker = "AAPL", AddedOn = timestamp };
        context.AddRange(state, stock);

        await context.SaveChangesAsync();

        AssertUtc(timestamp, state.UpdatedAtUtc);
        Assert.NotNull(stock.AddedOn);
        AssertUtc(timestamp, stock.AddedOn!.Value);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void AssertUtc(DateTimeOffset original, DateTimeOffset normalized)
    {
        Assert.Equal(original.ToUniversalTime(), normalized);
        Assert.Equal(TimeSpan.Zero, normalized.Offset);
    }
}
