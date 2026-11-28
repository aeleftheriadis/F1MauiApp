namespace F1App.Theme;

/// <summary>Every colour the app uses, named by role rather than by shade.</summary>
public sealed record Palette
{
    public required Color Background { get; init; }

    public required Color Surface { get; init; }

    public required Color SurfaceRaised { get; init; }

    public required Color SurfaceMuted { get; init; }

    public required Color Border { get; init; }

    public required Color TextPrimary { get; init; }

    public required Color TextSecondary { get; init; }

    /// <summary>Saturated red for fills: badges, brake dots, the start/finish marker.</summary>
    public required Color Live { get; init; }

    /// <summary>Red that is legible as text on <see cref="Background"/>.</summary>
    public required Color LiveText { get; init; }

    public required Color Destructive { get; init; }

    public required Color SectorBest { get; init; }

    public required Color SectorPersonal { get; init; }

    public required Color SectorSlow { get; init; }

    /// <summary>Chrome for the shell flyout and navigation bar.</summary>
    public required Color Chrome { get; init; }

    /// <summary>Telemetry channel A: speed traces, the focused driver, the selection rail.</summary>
    public required Color TeleCyan { get; init; }

    /// <summary>Telemetry channel B and the "sector improved" block.</summary>
    public required Color TeleLime { get; init; }

    /// <summary>Personal best sector block.</summary>
    public required Color TelePurple { get; init; }

    /// <summary>Slower sector block and the medium tyre badge.</summary>
    public required Color TeleYellow { get; init; }
}

/// <summary>The two palettes. Both were contrast-checked, not eyeballed.</summary>
public static class Palettes
{
    /// <summary>F1's near-black canvas. Every text colour clears 4.5:1 on <see cref="Palette.Background"/>.</summary>
    /// <remarks>
    /// Measured against the F1.com pages this palette follows: white 18.14:1, secondary 6.00:1 on the canvas
    /// and 5.41:1 on an elevated surface. Red stays a fill, never body text - 3.65:1 clears large text only,
    /// which is why <see cref="Palette.LiveText"/> exists for anything smaller.
    /// The border is a hairline at 1.61:1: it separates rows whose content already identifies them, so it is
    /// decoration rather than a boundary a reader has to perceive. Raising it to 3:1 makes it a heavy rule.
    /// </remarks>
    public static readonly Palette Dark = new()
    {
        Background = Color.FromArgb("#15151E"),
        Surface = Color.FromArgb("#1F1F27"),
        SurfaceRaised = Color.FromArgb("#26262F"),
        SurfaceMuted = Color.FromArgb("#24242D"),
        Border = Color.FromArgb("#3A3A44"),
        TextPrimary = Color.FromArgb("#FFFFFF"),
        TextSecondary = Color.FromArgb("#949498"),
        Live = Color.FromArgb("#E10600"),
        LiveText = Color.FromArgb("#FF5C5C"),
        Destructive = Color.FromArgb("#EF4444"),
        SectorBest = Color.FromArgb("#A855F7"),
        SectorPersonal = Color.FromArgb("#22C55E"),
        SectorSlow = Color.FromArgb("#FBBF24"),
        Chrome = Color.FromArgb("#1E1E23"),
        TeleCyan = Color.FromArgb("#00E5F5"),
        TeleLime = Color.FromArgb("#39FF14"),
        TelePurple = Color.FromArgb("#C45CFF"),
        TeleYellow = Color.FromArgb("#FFD600"),
    };

    /// <summary>Daylight palette for a phone used outdoors; same roles, same contrast contract.</summary>
    public static readonly Palette Light = new()
    {
        Background = Color.FromArgb("#F4F6F9"),
        Surface = Color.FromArgb("#FFFFFF"),
        SurfaceRaised = Color.FromArgb("#FFFFFF"),
        SurfaceMuted = Color.FromArgb("#E9EDF2"),
        Border = Color.FromArgb("#CBD5E1"),
        TextPrimary = Color.FromArgb("#0F172A"),
        TextSecondary = Color.FromArgb("#475569"),
        Live = Color.FromArgb("#E10600"),
        LiveText = Color.FromArgb("#B3122A"),
        Destructive = Color.FromArgb("#B91C1C"),
        SectorBest = Color.FromArgb("#6D28D9"),
        SectorPersonal = Color.FromArgb("#15803D"),
        SectorSlow = Color.FromArgb("#B45309"),
        Chrome = Color.FromArgb("#FFFFFF"),
        TeleCyan = Color.FromArgb("#0E7490"),
        TeleLime = Color.FromArgb("#15803D"),
        TelePurple = Color.FromArgb("#6D28D9"),
        TeleYellow = Color.FromArgb("#B45309"),
    };
}
