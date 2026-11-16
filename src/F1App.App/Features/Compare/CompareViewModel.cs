using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Services;
using F1App.Telemetry.Replay;

namespace F1App.Features.Compare;

/// <summary>A driver's speed trace over the replay window.</summary>
public sealed record SpeedTrace(string Label, double[] SecondsFromStart, double[] SpeedKph);

/// <summary>Head-to-head speed comparison for the session being replayed.</summary>
public sealed partial class CompareViewModel : ObservableObject
{
    private readonly SessionRunner _runner;

    public CompareViewModel(SessionRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;

        Headline = "compare";
        Status = "waiting for a replay";
        LeftIndex = -1;
        RightIndex = -1;
    }

    /// <summary>Driver labels for the two pickers.</summary>
    public ObservableCollection<string> Drivers { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial int LeftIndex { get; set; }

    [ObservableProperty]
    public partial int RightIndex { get; set; }

    [ObservableProperty]
    public partial SpeedTrace? Left { get; set; }

    [ObservableProperty]
    public partial SpeedTrace? Right { get; set; }

    /// <summary>Rebuilds the driver list from the loaded replay, keeping the current picks when possible.</summary>
    /// <returns><see langword="true"/> when the list changed and the pickers need reselecting.</returns>
    public bool RefreshDriverList()
    {
        var session = _runner.Loaded;

        if (session is null || session.Samples.IsEmpty)
        {
            Status = _runner.FailureReason ?? "no replay loaded yet";
            return false;
        }

        var numbers = session.Samples
            .Select(static sample => sample.DriverNumber)
            .Distinct()
            .OrderBy(static number => number)
            .ToArray();

        var labels = numbers
            .Select(number => session.DriverFor(number).Code)
            .ToArray();

        if (Drivers.Count == labels.Length && Drivers.SequenceEqual(labels, StringComparer.Ordinal))
        {
            return false;
        }

        Drivers.Clear();

        foreach (var label in labels)
        {
            Drivers.Add(label);
        }

        Headline = $"compare · {session.Label}";
        Status = $"{numbers.Length} cars · {session.Samples.Length} samples · {session.From.ToLocalTime():HH:mm:ss}";

        return true;
    }

    /// <summary>Builds the two traces for the current picks.</summary>
    public void Build()
    {
        var session = _runner.Loaded;

        if (session is null)
        {
            return;
        }

        var numbers = session.Samples
            .Select(static sample => sample.DriverNumber)
            .Distinct()
            .OrderBy(static number => number)
            .ToArray();

        Left = TraceFor(session, numbers, LeftIndex);
        Right = TraceFor(session, numbers, RightIndex);
    }

    private static SpeedTrace? TraceFor(ReplaySession session, int[] numbers, int index)
    {
        if (index < 0 || index >= numbers.Length)
        {
            return null;
        }

        var driver = numbers[index];
        var samples = session.Samples
            .Where(sample => sample.DriverNumber == driver)
            .OrderBy(static sample => sample.Time)
            .ToArray();

        if (samples.Length == 0)
        {
            return null;
        }

        var origin = samples[0].Time;
        var seconds = new double[samples.Length];
        var speeds = new double[samples.Length];

        for (var i = 0; i < samples.Length; i++)
        {
            seconds[i] = (samples[i].Time - origin).TotalSeconds;
            speeds[i] = samples[i].SpeedKph;
        }

        return new SpeedTrace(session.DriverFor(driver).Code, seconds, speeds);
    }
}
