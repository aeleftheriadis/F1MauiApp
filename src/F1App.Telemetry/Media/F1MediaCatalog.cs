using System.Collections.Frozen;

namespace F1App.Telemetry.Media;

/// <summary>
/// The F1 media CDN's asset URLs, as verified by request on 2026-11-22.
/// </summary>
/// <remarks>
/// <para>
/// The provider ships these images on <c>media.formula1.com</c> with no key. They are F1's own artwork, so
/// the app treats them as published: hotlinked from the CDN, credited on screen, never downloaded, resized
/// and stored, or redistributed. See ADR-019.
/// </para>
/// <para>
/// Every identifier here was checked with a real request when it was added. That matters because the ids
/// cannot be derived — <c>georus01</c>, <c>lewham01</c> and <c>chalec01</c> follow no rule — so a guessed
/// path would be a 404 dressed as code. **An identifier that is not in a table returns <see langword="null"/>
/// and the caller must then request nothing at all.** A new season means extending these tables and
/// re-verifying them; the fallbacks cover the gap while that has not happened.
/// </para>
/// <para>
/// Known gap, verified: the 2026 Abu Dhabi card returns 404 at the slug pattern the other 22 races use, which
/// matches the calendar page publishing no card for that round. It is deliberately absent rather than assumed.
/// </para>
/// </remarks>
public static class F1MediaCatalog
{
    private const string Cdn = "https://media.formula1.com/image/upload";
    private const string Version = "v1740000001";
    private const string Season = "2026";

    /// <summary>Driver cutouts, transparent and facing right, keyed by F1's three-letter code.</summary>
    private static readonly FrozenDictionary<string, (string Team, string Driver)> Drivers =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ANT"] = ("mercedes", "andant01"),
            ["VER"] = ("redbullracing", "maxver01"),
            ["HAM"] = ("ferrari", "lewham01"),
            ["RUS"] = ("mercedes", "georus01"),
            ["NOR"] = ("mclaren", "lannor01"),
            ["LEC"] = ("ferrari", "chalec01"),
            ["PIA"] = ("mclaren", "oscpia01"),
            ["HAD"] = ("redbullracing", "isahad01"),
            ["LAW"] = ("racingbulls", "lialaw01"),
            ["GAS"] = ("alpine", "piegas01"),
            ["LIN"] = ("racingbulls", "arvlin01"),
            ["COL"] = ("alpine", "fracol01"),
            ["BEA"] = ("haasf1team", "olibea01"),
            ["BOR"] = ("audi", "gabbor01"),
            ["HUL"] = ("audi", "nichul01"),
            ["SAI"] = ("williams", "carsai01"),
            ["OCO"] = ("haasf1team", "estoco01"),
            ["ALO"] = ("astonmartin", "feralo01"),
            ["ALB"] = ("williams", "alealb01"),
            ["TSU"] = ("racingbulls", "yuktsu01"),
            ["HIR"] = ("haasf1team", "ryohir01"),
            ["STR"] = ("astonmartin", "lanstr01"),
            ["BOT"] = ("cadillac", "valbot01"),
            ["PER"] = ("cadillac", "serper01"),
            ["CAM"] = ("haasf1team", "rafcam01"),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Team slugs, keyed by the team name the feeds use, plus the short forms they also use.</summary>
    private static readonly FrozenDictionary<string, string> Teams =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mercedes"] = "mercedes",
            ["Red Bull Racing"] = "redbullracing",
            ["Red Bull"] = "redbullracing",
            ["Ferrari"] = "ferrari",
            ["McLaren"] = "mclaren",
            ["Racing Bulls"] = "racingbulls",
            ["RB"] = "racingbulls",
            ["Alpine"] = "alpine",
            ["Haas F1 Team"] = "haasf1team",
            ["Haas"] = "haasf1team",
            ["Audi"] = "audi",
            ["Williams"] = "williams",
            ["Aston Martin"] = "astonmartin",
            ["Cadillac"] = "cadillac",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Race slugs whose card answered 200 (22 of the 23 rounds; Abu Dhabi has no card).</summary>
    private static readonly FrozenSet<string> RaceSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "australia", "china", "japan", "miami", "canada", "monaco", "barcelona-catalunya",
        "austria", "great-britain", "belgium", "hungary", "netherlands", "italy", "spain",
        "azerbaijan", "bahrain", "singapore", "united-states", "mexico", "brazil", "las-vegas",
        "qatar",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A driver's full-body cutout, or <see langword="null"/> when the code is not in the catalogue.</summary>
    /// <param name="driverAcronym">F1's three-letter code, e.g. <c>ANT</c>.</param>
    /// <param name="width">Rendered width: 720 for a dossier, 128 for a row in a list.</param>
    public static Uri? DriverCutout(string? driverAcronym, int width = 720)
    {
        if (string.IsNullOrWhiteSpace(driverAcronym) ||
            !Drivers.TryGetValue(driverAcronym.Trim(), out var entry))
        {
            return null;
        }

        var file = $"{Season}{entry.Team}{entry.Driver}right.webp";

        return new Uri($"{Cdn}/c_lfill,w_{width}/q_auto/{Version}/common/f1/{Season}/{entry.Team}/{entry.Driver}/{file}");
    }

    /// <summary>A team's car cutout, or <see langword="null"/> for a team the catalogue does not know.</summary>
    public static Uri? TeamCar(string? teamName) =>
        TeamAsset(teamName, "carright");

    /// <summary>A team's white logo, or <see langword="null"/> for a team the catalogue does not know.</summary>
    public static Uri? TeamLogo(string? teamName) =>
        TeamAsset(teamName, "logowhite");

    /// <summary>Meeting names as the feeds write them, mapped to verified slugs.</summary>
    private static readonly FrozenDictionary<string, string> Meetings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Australian Grand Prix"] = "australia",
            ["Chinese Grand Prix"] = "china",
            ["Japanese Grand Prix"] = "japan",
            ["Miami Grand Prix"] = "miami",
            ["Canadian Grand Prix"] = "canada",
            ["Monaco Grand Prix"] = "monaco",
            ["Spanish Grand Prix"] = "spain",
            ["Austrian Grand Prix"] = "austria",
            ["British Grand Prix"] = "great-britain",
            ["Belgian Grand Prix"] = "belgium",
            ["Hungarian Grand Prix"] = "hungary",
            ["Dutch Grand Prix"] = "netherlands",
            ["Italian Grand Prix"] = "italy",
            ["Azerbaijan Grand Prix"] = "azerbaijan",
            ["Bahrain Grand Prix in Malaysia"] = "bahrain",
            ["Singapore Grand Prix"] = "singapore",
            ["United States Grand Prix"] = "united-states",
            ["Mexico City Grand Prix"] = "mexico",
            ["Brazilian Grand Prix"] = "brazil",
            // OpenF1 publishes the same race under a different name than Jolpica; both are real, so both map.
            ["São Paulo Grand Prix"] = "brazil",
            ["Sao Paulo Grand Prix"] = "brazil",

            ["Las Vegas Grand Prix"] = "las-vegas",
            ["Qatar Grand Prix"] = "qatar",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>A race's circuit card, looked up by the meeting name the feed uses.</summary>
    public static Uri? CircuitCardForMeeting(string? meetingName) =>
        string.IsNullOrWhiteSpace(meetingName) || !Meetings.TryGetValue(meetingName.Trim(), out var slug)
            ? null
            : CircuitCard(slug);

    /// <summary>A race's 16:9 circuit card, or <see langword="null"/> when the slug is unknown.</summary>
    public static Uri? CircuitCard(string? raceSlug)
    {
        if (string.IsNullOrWhiteSpace(raceSlug) || !RaceSlugs.Contains(raceSlug.Trim()))
        {
            return null;
        }

        return new Uri(
            $"{Cdn}/c_lfill,w_720/q_auto/{Version}/fom-website/static-assets/{Season}/races/card/{raceSlug.Trim().ToLowerInvariant()}.webp");
    }

    private static Uri? TeamAsset(string? teamName, string suffix)
    {
        if (string.IsNullOrWhiteSpace(teamName) || !Teams.TryGetValue(teamName.Trim(), out var slug))
        {
            return null;
        }

        return new Uri($"{Cdn}/c_lfill,w_720/q_auto/{Version}/common/f1/{Season}/{slug}/{Season}{slug}{suffix}.webp");
    }
}
