using F1App.Domain;

namespace F1App.Telemetry.Tests;

public sealed class TelemetrySnapshotStoreTests
{
    private static TelemetryFrame Frame(int driver, long sequence, short speed = 100) =>
        new(driver, sequence, DateTimeOffset.UnixEpoch.AddSeconds(sequence), speed, 7, 100, false, 12000, DrsState.Unavailable, 0f, 0f);

    [Fact]
    public void TryApply_RejectsStaleAndDuplicateSequences()
    {
        var store = new TelemetrySnapshotStore();

        Assert.True(store.TryApply(Frame(44, 200)));
        Assert.False(store.TryApply(Frame(44, 199)));
        Assert.False(store.TryApply(Frame(44, 200)));
        Assert.True(store.TryApply(Frame(44, 201)));

        Assert.Equal(2, store.AppliedCount);
        Assert.Equal(2, store.RejectedCount);
    }

    [Fact]
    public void TryApply_RejectsOutOfRangeDriverNumbers()
    {
        var store = new TelemetrySnapshotStore();

        Assert.False(store.TryApply(Frame(0, 1)));
        Assert.False(store.TryApply(Frame(TelemetrySnapshotStore.MaxDriverNumber, 1)));
        Assert.Equal(0, store.AppliedCount);
    }

    [Fact]
    public void ReadInto_ReturnsLatestSamplePerDriverOrderedByNumber()
    {
        var store = new TelemetrySnapshotStore();
        store.TryApply(Frame(16, 5, speed: 280));
        store.TryApply(Frame(1, 9, speed: 310));
        store.TryApply(Frame(16, 6, speed: 290));

        Span<TelemetryFrame> buffer = stackalloc TelemetryFrame[4];
        var count = store.ReadInto(buffer);

        Assert.Equal(2, count);
        Assert.Equal(1, buffer[0].DriverNumber);
        Assert.Equal(310, buffer[0].SpeedKph);
        Assert.Equal(16, buffer[1].DriverNumber);
        Assert.Equal(290, buffer[1].SpeedKph);
    }

    [Fact]
    public void ReadInto_RespectsDestinationLength()
    {
        var store = new TelemetrySnapshotStore();
        store.TryApply(Frame(1, 1));
        store.TryApply(Frame(2, 1));
        store.TryApply(Frame(3, 1));

        Span<TelemetryFrame> buffer = stackalloc TelemetryFrame[2];

        Assert.Equal(2, store.ReadInto(buffer));
    }

    [Fact]
    public void Reset_ForgetsEverySample()
    {
        var store = new TelemetrySnapshotStore();
        store.TryApply(Frame(11, 1));

        store.Reset();

        Span<TelemetryFrame> buffer = stackalloc TelemetryFrame[4];
        Assert.Equal(0, store.ReadInto(buffer));
    }
}
