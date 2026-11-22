namespace F1App.Theme;

/// <summary>
/// Design tokens, in code.
/// </summary>
/// <remarks>
/// Semantic roles only - no screen in this app may name a raw hex value. Keeping them in C# rather
/// than a XAML resource dictionary means a typo is a compile error instead of a silent transparent
/// brush, and there are no string keys to mistype at the call site.
/// The palette follows a HUD/FUI direction: near-black surfaces, one saturated red reserved for
/// live state and destructive actions, and per-team accents supplied by the data, never by the theme.
/// </remarks>
public static class Tokens
{
    // Type scale (points). The dataset's floor is 12 pt for any text, 14-16 pt for body.
    public const double FontCaption = 12;
    public const double FontBody = 15;
    public const double FontSubtitle = 17;
    public const double FontTitle = 20;
    public const double FontDisplay = 28;

    /// <summary>
    /// Monospaced family for numbers: the platform's own mono face, so no font asset and no font
    /// licence has to ship while columns still align.
    /// </summary>
    /// <remarks>
    /// Windows gets <see langword="null"/>: MAUI probes <c>Assets/Fonts/Consolas.ttf</c> before letting
    /// WinUI resolve a system family, and that probe logs an error for every label. Numerals there fall
    /// back to the system face - a dev-target compromise, since Windows is not a shipping target.
    /// </remarks>
    public static string? MonoFont =>
        DeviceInfo.Current.Platform == DevicePlatform.Android ? "monospace"
        : DeviceInfo.Current.Platform == DevicePlatform.iOS ? "Menlo"
        : null;

    // Spacing rhythm: 4/8 base
    public const double Space1 = 4;
    public const double Space2 = 8;
    public const double Space3 = 12;
    public const double Space4 = 16;
    public const double Space6 = 24;

    /// <summary>
    /// Minimum touch target, per platform: 44 pt on iOS, 48 dp elsewhere.
    /// </summary>
    /// <remarks>
    /// The two platforms publish different numbers; collapsing them into one value under-serves iOS or
    /// over-pads Android.
    /// </remarks>
    public static double MinTouchTarget =>
        DeviceInfo.Current.Platform == DevicePlatform.iOS ? 44 : 48;

    /// <summary>
    /// Smallest height a picker is allowed to be.
    /// </summary>
    /// <remarks>
    /// Deliberately a minimum and not a request: on WinUI the picker draws its own inner control, and forcing
    /// a height clips the selected text (measured on the Settings theme picker).
    /// </remarks>
    public const double MinPickerHeight = 32;

    /// <summary>Gap kept between adjacent touchables.</summary>
    public const double TouchSpacing = 8;

    // Radii
    public const double RadiusSmall = 6;
    public const double RadiusCard = 14;

    /// <summary>
    /// Elevation for cards, softer in the light palette where a hard shadow reads as a smudge.
    /// </summary>
    /// <remarks>
    /// A soft offset shadow rather than a drawn border: it is what makes a surface read as raised on both
    /// platforms. Deliberately not applied to the 20-row timing tower, where twenty shadows would cost
    /// more than the rows themselves.
    /// </remarks>
    public static Shadow CardShadow => new()
    {
        Brush = Brush.Black,
        Opacity = ReferenceEquals(Themes.Current, Palettes.Light) ? 0.12f : 0.35f,
        Radius = 14,
        Offset = new Point(0, 4),
    };
}

/// <summary>
/// How the app writes a date.
/// </summary>
/// <remarks>
/// One culture for every date in the app. The screens are English-only and every number beside a date is
/// invariant, so letting the calendar follow the device culture produced "Kup 08 Map" next to "Sun 29 Nov" on
/// the same screen - a defect a screenshot caught and no test could. A user on an unusual culture gets English
/// month names, consistently, rather than half the screen in each.
/// </remarks>
public static class Dates
{
    /// <summary>Culture used for every user-facing date.</summary>
    public static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>"Mon 02 Mar · 15:00" in local time.</summary>
    public static string DayTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("ddd dd MMM · HH:mm", Culture);

    /// <summary>"02 Mar 2026".</summary>
    public static string Day(DateOnly value) => value.ToString("dd MMM yyyy", Culture);

    /// <summary>"Sat 02 Mar 2026".</summary>
    public static string Full(DateOnly value) => value.ToString("ddd dd MMM yyyy", Culture);
}
