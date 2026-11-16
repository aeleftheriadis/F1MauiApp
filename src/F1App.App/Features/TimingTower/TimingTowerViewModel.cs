using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Services;
using F1App.Telemetry;

namespace F1App.Features.TimingTower;

/// <summary>
/// Projects the newest telemetry snapshot onto the timing tower rows.
/// </summary>
/// <remarks>
/// <see cref="Render"/> is driven by a UI-thread timer, not by the ingest loop. Reading the store is
/// therefore the only shared state, and repeated samples between two ticks collapse into one update
/// by construction.
/// </remarks>
public sealed partial class TimingTowerViewModel : ObservableObject
{
    private readonly TelemetrySnapshotStore _store;
    private readonly SessionRunner _runner;
    private readonly IClock _clock;
    private readonly Dictionary<int, TimingRow> _rowsByDriver = [];

    public TimingTowerViewModel(TelemetrySnapshotStore store, SessionRunner runner, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _runner = runner;
        _clock = clock;

        SessionLabel = "loading session";
        StatusLine = "contacting OpenF1";
        Diagnostics = string.Empty;
    }

    /// <summary>Rows in the order they are displayed.</summary>
    public ObservableCollection<TimingRow> Rows { get; } = [];

    /// <summary>Which session the numbers belong to. Never omitted, never implied.</summary>
    [ObservableProperty]
    public partial string SessionLabel { get; set; }

    [ObservableProperty]
    public partial string StatusLine { get; set; }

    [ObservableProperty]
    public partial string Diagnostics { get; set; }

    /// <summary>True when the load failed, so the page can style the banner as a problem.</summary>
    [ObservableProperty]
    public partial bool HasFailed { get; set; }

    /// <summary>Pulls one snapshot and pushes it into the rows. Must be called on the UI thread.</summary>
    public void Render()
    {
        SessionLabel = _runner.FailureReason is null
            ? _runner.SessionLabel
            : $"no data · {_runner.SessionLabel}";

        HasFailed = _runner.FailureReason is not null;

        Span<TelemetryFrame> frames = stackalloc TelemetryFrame[TelemetrySnapshotStore.MaxDriverNumber];
        var count = _store.ReadInto(frames);

        for (var i = 0; i < count; i++)
        {
            ref readonly var frame = ref frames[i];

            if (!_rowsByDriver.TryGetValue(frame.DriverNumber, out var row))
            {
                var identity = _runner.Loaded?.DriverFor(frame.DriverNumber);
                var colour = identity is null ? (Color?)null : Color.FromArgb(identity.TeamColorHex);

                row = new TimingRow(frame.DriverNumber, identity?.Code ?? $"CAR {frame.DriverNumber}", colour);
                _rowsByDriver.Add(frame.DriverNumber, row);
                Rows.Add(row);
            }

            row.Apply(frame);
        }

        for (var i = 0; i < count; i++)
        {
            if (_rowsByDriver.TryGetValue(frames[i].DriverNumber, out var row))
            {
                row.Position = i + 1;
            }
        }

        StatusLine = _runner.FailureReason
            ?? (count == 0
                ? "loading recorded session"
                : $"{count} cars · replay {_clock.UtcNow.ToLocalTime():HH:mm:ss}");

        // Measured counters, not estimates: these are the numbers the perf claims rest on.
        Diagnostics = $"applied {_store.AppliedCount} · rejected {_store.RejectedCount}";
    }
}
