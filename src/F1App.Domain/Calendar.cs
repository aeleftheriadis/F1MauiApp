namespace F1App.Domain;

/// <summary>
/// A permanent racing venue.
/// </summary>
/// <param name="Id">Provider-agnostic slug, e.g. <c>bahrain</c>. Stable across the two data sources.</param>
/// <param name="ShortName">Three-letter timing-tower code, e.g. <c>BHR</c>.</param>
/// <param name="Name">Full venue name as published by the provider.</param>
/// <param name="Country">Country of the venue, as published by the provider.</param>
public sealed record Circuit(string Id, string ShortName, string Name, string Country)
{
    /// <summary>Builds a circuit from the provider's free-text fields.</summary>
    public static Circuit FromProvider(string shortName, string? location, string? country)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortName);

        Span<char> buffer = stackalloc char[shortName.Length];
        var length = 0;

        foreach (var character in shortName)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }

        return new Circuit(
            length == 0 ? "unknown" : new string(buffer[..length]),
            shortName,
            string.IsNullOrWhiteSpace(location) ? shortName : location,
            string.IsNullOrWhiteSpace(country) ? "Unknown" : country);
    }
}

/// <summary>
/// One race in the calendar. Modelled at race granularity: qualifying and practice windows are
/// session data, not calendar data, and are fetched per meeting from the telemetry provider.
/// </summary>
public sealed record RaceWeekend(
    int MeetingKey,
    int Season,
    int Round,
    Circuit Circuit,
    string Name,
    DateTimeOffset RaceStart);

/// <summary>A driver and the constructor they currently race for.</summary>
public sealed record Driver(
    int Number,
    string Code,
    string FullName,
    string TeamName,
    string TeamColorHex);
