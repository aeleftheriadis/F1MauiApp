namespace F1App.Services;

/// <summary>
/// User preferences that survive a restart.
/// </summary>
/// <remarks>
/// Only settings that change behaviour belong here. The live-feed switch defaults to off: the source it
/// enables is undocumented and outside the project's open-API rule, so it is opt-in per install.
/// </remarks>
public sealed class AppSettings
{
    private const string LiveFeedKey = "official_live_feed_enabled";

    /// <summary>Whether the unofficial Formula 1 live-timing feed may be used.</summary>
    public bool LiveFeedEnabled
    {
        get => Preferences.Get(LiveFeedKey, false);
        set => Preferences.Set(LiveFeedKey, value);
    }
}
