using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using F1App.Domain;
using F1App.Theme;

namespace F1App.Features.TimingTower;

/// <summary>One row of the timing tower.</summary>
/// <remarks>
/// Every displayed value is a pre-formatted string or a primitive, so a cell binding never runs a
/// converter and never allocates during a frame. <c>[ObservableProperty]</c> raises
/// <c>PropertyChanged</c> only when the value actually changed, which is what keeps the
/// CollectionView from invalidating 20 rows thirty times a second.
/// </remarks>
public sealed partial class TimingRow : ObservableObject
{
    private static readonly Color[] TeamPalette =
    [
        Color.FromArgb("#3671C6"), Color.FromArgb("#6692FF"), Color.FromArgb("#64C4FF"),
        Color.FromArgb("#FF8000"), Color.FromArgb("#E8002D"), Color.FromArgb("#27F4D2"),
        Color.FromArgb("#B6BABD"), Color.FromArgb("#FF87BC"), Color.FromArgb("#52E252"),
        Color.FromArgb("#FF6BB5"),
    ];

    public TimingRow(int driverNumber, string code, Color? teamColor = null)
    {
        DriverNumber = driverNumber;
        Code = code;
        TeamColor = teamColor ?? TeamPalette[driverNumber % TeamPalette.Length];

        Speed = "—";
        Gear = "—";
        BrakeColor = Colors.Transparent;
        Drs = string.Empty;
        AccessibleText = string.Empty;
    }

    public int DriverNumber { get; }

    public string Code { get; }

    public Color TeamColor { get; }

    // Partial properties rather than fields: on WinUI the field form is not AOT/CsWinRT safe
    // (MVVMTK0045), and these rows are exactly what the Windows target renders during development.
    [ObservableProperty]
    public partial int Position { get; set; }

    [ObservableProperty]
    public partial string Speed { get; set; }

    [ObservableProperty]
    public partial string Gear { get; set; }

    [ObservableProperty]
    public partial double Throttle { get; set; }

    [ObservableProperty]
    public partial Color BrakeColor { get; set; }

    [ObservableProperty]
    public partial string Drs { get; set; }

    [ObservableProperty]
    public partial string AccessibleText { get; set; }

    /// <summary>Copies the newest sample into the row's bindable properties.</summary>
    public void Apply(in TelemetryFrame frame)
    {
        Speed = frame.SpeedKph.ToString(CultureInfo.InvariantCulture);
        Gear = frame.Gear.ToString(CultureInfo.InvariantCulture);
        Throttle = frame.ThrottlePercent / 100d;
        BrakeColor = frame.Braking ? Themes.Current.Live : Colors.Transparent;
        Drs = frame.Drs == DrsState.Active ? "DRS" : string.Empty;

        // The screen reader gets a sentence, not a colour. Sector/Drs state is never colour-only.
        AccessibleText =
            $"{Code}, {Speed} kilometres per hour, gear {Gear}, throttle {frame.ThrottlePercent} percent" +
            (frame.Braking ? ", braking" : string.Empty) +
            (frame.Drs == DrsState.Active ? ", DRS open" : string.Empty);
    }
}
