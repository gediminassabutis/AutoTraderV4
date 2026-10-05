using AutoTraderV4.Services;

namespace AutoTraderV4.Tests;

public sealed class StockTrackingCycleGateTests
{
    [Fact]
    public async Task RunAsync_SerializesConcurrentCycleRequests()
    {
        using var gate = new StockTrackingCycleGate();
        var activeCycles = 0;
        var overlapDetected = 0;

        async Task<StockTrackingCycleResult> RunCycleAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref activeCycles) != 1)
            {
                Interlocked.Exchange(ref overlapDetected, 1);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                return new StockTrackingCycleResult();
            }
            finally
            {
                Interlocked.Decrement(ref activeCycles);
            }
        }

        var firstCycle = gate.RunAsync(RunCycleAsync);
        var secondCycle = gate.RunAsync(RunCycleAsync);

        await Task.WhenAll(firstCycle, secondCycle);

        Assert.Equal(0, overlapDetected);
    }
}
