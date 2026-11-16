using System.Globalization;

namespace F1App.Telemetry.Timing;

/// <summary>
/// Formatting for timing values, in one place.
/// </summary>
/// <remarks>
/// Screens bind to pre-formatted strings so a cell never runs a converter during a frame, and the same
/// numbers must read identically on the tower, the results table and the charts. An em dash - not a zero -
/// stands for "not in the payload".
/// </remarks>
public static class RaceFormat
{
    /// <summary>Lap time as <c>m:ss.mmm</c>, or a single-second form under a minute.</summary>
    public static string Lap(TimeSpan? value) => value switch
    {
        null => "—",
        { TotalMinutes: >= 1 } time =>
            string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds:000}"),
        var time => time.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
    };

    /// <summary>Sector time in seconds with three decimals.</summary>
    public static string Sector(TimeSpan? value) =>
        value is null ? "—" : value.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>Gap to the leader: <c>LEADER</c>, <c>+s.mmm</c>, or an em dash.</summary>
    public static string Gap(double? seconds) => seconds switch
    {
        null => "—",
        <= 0.0005 => "LEADER",
        var value => $"+{value.Value.ToString("F3", CultureInfo.InvariantCulture)}",
    };

    /// <summary>Speed in kilometres per hour, or an em dash.</summary>
    public static string Speed(short? kph) =>
        kph is null ? "—" : kph.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tyre compound and age, e.g. <c>SOFT 20</c>.</summary>
    public static string Tyre(string compound, int ageLaps) =>
        string.Create(CultureInfo.InvariantCulture, $"{compound} {ageLaps}");
}
