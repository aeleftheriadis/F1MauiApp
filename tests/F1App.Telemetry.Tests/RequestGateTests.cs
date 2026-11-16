using System.Diagnostics;

namespace F1App.Telemetry.Tests;

public sealed class RequestGateTests
{
    [Fact]
    public async Task WaitAsync_PacesConsecutiveCalls()
    {
        var gate = new RequestGate(TimeSpan.FromMilliseconds(30), TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();

        await gate.WaitAsync(CancellationToken.None);
        await gate.WaitAsync(CancellationToken.None);
        await gate.WaitAsync(CancellationToken.None);

        stopwatch.Stop();

        // Three grants with a 30 ms interval must span at least two intervals. Only the lower
        // bound is asserted so a loaded CI machine cannot make the test flaky.
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(55),
            $"expected at least 55 ms of pacing, observed {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task Penalize_PushesTheNextGrantBeyondTheBackoff()
    {
        var gate = new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System);

        await gate.WaitAsync(CancellationToken.None);
        gate.Penalize(TimeSpan.FromMilliseconds(60));

        var stopwatch = Stopwatch.StartNew();
        await gate.WaitAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(50),
            $"expected the backoff to delay the grant, observed {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public void Penalize_IgnoresNonPositiveBackoff()
    {
        var gate = new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System);

        gate.Penalize(TimeSpan.Zero);
        gate.Penalize(TimeSpan.FromSeconds(-5));

        Assert.True(true);
    }

    [Fact]
    public async Task WaitAsync_AllowsTheBurstImmediately()
    {
        var gate = RequestGate.ForRequestsPerMinute(30, TimeProvider.System, burst: 3);
        var stopwatch = Stopwatch.StartNew();

        await gate.WaitAsync(CancellationToken.None);
        await gate.WaitAsync(CancellationToken.None);
        await gate.WaitAsync(CancellationToken.None);

        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
            $"a burst of 3 should not be paced, observed {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task WaitAsync_PacesOnceTheBurstIsSpent()
    {
        var gate = RequestGate.ForRequestsPerMinute(1200, TimeProvider.System, burst: 1);
        await gate.WaitAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await gate.WaitAsync(CancellationToken.None);
        await gate.WaitAsync(CancellationToken.None);
        stopwatch.Stop();

        // 1200/min = 50 ms per token, so two further grants cost at least ~100 ms.
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(85),
            $"expected pacing after the burst, observed {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public void ForRequestsPerMinute_RejectsNonPositiveBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RequestGate.ForRequestsPerMinute(0, TimeProvider.System));
    }
}
