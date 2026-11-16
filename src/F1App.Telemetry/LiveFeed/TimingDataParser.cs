using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace F1App.Telemetry.LiveFeed;

/// <summary>One car's line from the official timing feed.</summary>
public sealed record LiveStanding(int DriverNumber, int Position, int Laps, string? LastLap);

/// <summary>
/// Reads <c>TimingData</c> out of a live-timing feed frame.
/// </summary>
/// <remarks>
/// <para>
/// The shape below is what the community clients read - <c>{"TimingData":{"Lines":{"44":{"Position":"1",
/// "NumberOfLaps":12,"LastLapTime":{"Value":"1:34.567"}}}}}</c> - and the hub sends nothing outside a
/// session, so it has <b>not</b> been verified end to end. It is written defensively for that reason:
/// numbers may arrive as strings or as numbers, missing fields are skipped, and anything unexpected
/// yields an empty result rather than a guess.
/// </para>
/// <para>
/// That is also why the live feed reports standings counts instead of driving the screens yet: a parser
/// that has never seen a live message must not be allowed to put a car in the wrong place.
/// </para>
/// </remarks>
public static class TimingDataParser
{
    /// <summary>Extracts the standings carried by one frame. Returns empty when the frame has none.</summary>
    public static ImmutableArray<LiveStanding> Parse(string frame)
    {
        if (string.IsNullOrWhiteSpace(frame))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(frame);

            if (!document.RootElement.TryGetProperty("TimingData", out var timing)
                || !timing.TryGetProperty("Lines", out var lines)
                || lines.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var builder = ImmutableArray.CreateBuilder<LiveStanding>();

            foreach (var line in lines.EnumerateObject())
            {
                if (!int.TryParse(line.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var driver))
                {
                    continue;
                }

                var value = line.Value;
                var position = ReadInt(value, "Position");
                if (position is null or <= 0)
                {
                    continue;
                }

                builder.Add(new LiveStanding(
                    DriverNumber: driver,
                    Position: position.Value,
                    Laps: ReadInt(value, "NumberOfLaps") ?? 0,
                    LastLap: ReadString(value, "LastLapTime", "Value")));
            }

            return builder
                .OrderBy(static standing => standing.Position)
                .ToImmutableArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadString(JsonElement element, string property, string nested)
    {
        if (!element.TryGetProperty(property, out var container)
            || container.ValueKind != JsonValueKind.Object
            || !container.TryGetProperty(nested, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
