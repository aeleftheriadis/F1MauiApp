using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Services;
using F1App.Telemetry.LiveFeed;
using F1App.Theme;

namespace F1App.Features.Settings;

/// <summary>App settings, including the opt-in for the unofficial live feed.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly TimeProvider _timeProvider;

    public SettingsViewModel(AppSettings settings, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _settings = settings;
        _timeProvider = timeProvider;

        Observation = "not tested";
    }

    /// <summary>
    /// Enables the unofficial Formula 1 live-timing hub. Off by default: undocumented endpoint, outside
    /// the project's open-API rule, and not something to turn on without deciding to.
    /// </summary>
    public bool LiveFeedEnabled
    {
        get => _settings.LiveFeedEnabled;
        set
        {
            if (_settings.LiveFeedEnabled == value)
            {
                return;
            }

            _settings.LiveFeedEnabled = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial string Observation { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    /// <summary>Labels for the theme picker, in <see cref="ThemeMode"/> order.</summary>
    public IList ThemeModes { get; } = new List<string> { "Follow system", "Light", "Dark" };

    /// <summary>Selected index into <see cref="ThemeModes"/>.</summary>
    public int ThemeIndex
    {
        get => (int)Themes.Mode;
        set
        {
            if (value < 0 || value > 2 || (int)Themes.Mode == value)
            {
                return;
            }

            Themes.Apply((ThemeMode)value);
            OnPropertyChanged();
            Observation = Themes.Mode == ThemeMode.System
                ? "theme follows the system"
                : $"theme fixed to {Themes.Mode}";
        }
    }

    /// <summary>False while a probe is running, so the button cannot fire twice.</summary>
    public bool CanTest => !IsTesting;

    /// <summary>Opens the live-timing hub for a few seconds and reports exactly what happened.</summary>
    public async Task TestLiveFeedAsync(CancellationToken cancellationToken)
    {
        if (IsTesting)
        {
            return;
        }

        IsTesting = true;
        OnPropertyChanged(nameof(CanTest));
        Observation = "negotiating…";

        try
        {
            var started = _timeProvider.GetUtcNow();

            // Frames are captured so the next live session yields an offline fixture for the parser.
            var capturePath = Path.Combine(FileSystem.AppDataDirectory, "live-frames.jsonl");

            var observation = await OfficialLiveTimingClient
                .ObserveAsync(TimeSpan.FromSeconds(8), capturePath, cancellationToken)
                .ConfigureAwait(true);

            var elapsed = _timeProvider.GetUtcNow() - started;

            Observation = observation switch
            {
                { Error: { Length: > 0 } error } => $"failed: {error}",
                { FramesObserved: 0 } =>
                    $"connected and subscribed in {elapsed.TotalSeconds:F1}s, no frames " +
                    "(expected outside a live session)",
                _ =>
                    $"{observation.FramesObserved} frames, standings parsed: {observation.StandingsParsed}, " +
                    $"captured {observation.FramesCaptured} to {observation.CapturePath} " +
                    $"· targets: {string.Join(", ", observation.FrameTargets)}",
            };
        }
        catch (OperationCanceledException)
        {
            Observation = "cancelled";
        }
        catch (Exception exception)
        {
            Observation = $"failed: {exception.GetType().Name}";
        }
        finally
        {
            IsTesting = false;
            OnPropertyChanged(nameof(CanTest));
        }
    }
}
