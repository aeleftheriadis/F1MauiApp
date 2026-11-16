namespace F1App.Theme;

/// <summary>Which palette the app follows.</summary>
public enum ThemeMode
{
    /// <summary>Follow the operating system.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
}

/// <summary>
/// The app's current palette, and the switch that changes it.
/// </summary>
/// <remarks>
/// <para>
/// Every screen here is built in C#, so there is no XAML resource dictionary to swap. The C# equivalent is
/// a palette swap plus a rebuild: <see cref="Current"/> answers with the palette for the effective theme,
/// screens are constructed from it, and <see cref="Changed"/> tells the open screens to build themselves
/// again. Nothing is cached per element, so there is no stale colour to chase.
/// </para>
/// <para>
/// The theme itself is applied through <c>UserAppTheme</c>, which is the supported way to override the OS;
/// <see cref="ThemeMode.System"/> sets <c>AppTheme.Unspecified</c> to follow it again. Android declares
/// <c>ConfigChanges.UiMode</c>, so an OS switch does not restart the activity.
/// </para>
/// </remarks>
public static class Themes
{
    private const string PreferenceKey = "theme_mode";

    private static ThemeMode _mode = ThemeMode.System;

    /// <summary>Raised after the palette changes, so open screens can rebuild.</summary>
    public static event EventHandler? Changed;

    /// <summary>The palette for the effective theme.</summary>
    public static Palette Current =>
        EffectiveTheme() == AppTheme.Light ? Palettes.Light : Palettes.Dark;

    /// <summary>The user's choice.</summary>
    public static ThemeMode Mode => _mode;

    /// <summary>True when the user explicitly picked light or dark.</summary>
    public static bool IsOverridden => _mode != ThemeMode.System;

    /// <summary>Loads the saved choice, applies it, and hooks the OS theme change. Call once at start-up.</summary>
    public static void Initialize()
    {
        _mode = Parse(Preferences.Get(PreferenceKey, nameof(ThemeMode.System)));

        // A dev override so both palettes can be exercised without touching settings.
        _mode = Environment.GetEnvironmentVariable("F1APP_THEME")?.ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => _mode,
        };

        Apply(_mode, persist: false);

        if (Application.Current is { } application)
        {
            application.RequestedThemeChanged += (_, _) =>
            {
                if (!IsOverridden)
                {
                    Changed?.Invoke(null, EventArgs.Empty);
                }
            };
        }
    }

    /// <summary>Applies a mode, persists it, and asks the open screens to rebuild.</summary>
    public static void Apply(ThemeMode mode, bool persist = true)
    {
        _mode = mode;

        if (persist)
        {
            Preferences.Set(PreferenceKey, mode.ToString());
        }

        if (Application.Current is { } application)
        {
            application.UserAppTheme = mode switch
            {
                ThemeMode.Light => AppTheme.Light,
                ThemeMode.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified,
            };
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static AppTheme EffectiveTheme() => _mode switch
    {
        ThemeMode.Light => AppTheme.Light,
        ThemeMode.Dark => AppTheme.Dark,
        _ => Application.Current?.RequestedTheme ?? AppTheme.Dark,
    };

    private static ThemeMode Parse(string value) => value switch
    {
        nameof(ThemeMode.Light) => ThemeMode.Light,
        nameof(ThemeMode.Dark) => ThemeMode.Dark,
        _ => ThemeMode.System,
    };
}
