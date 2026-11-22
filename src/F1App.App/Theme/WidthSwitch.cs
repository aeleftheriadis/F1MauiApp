namespace F1App.Theme;

/// <summary>
/// Tracks whether a page currently has room for two panes, and reports only when that flips.
/// </summary>
/// <remarks>
/// <para>
/// A page rebuilds its whole content when the arrangement changes, which is far too heavy to do on every
/// resize event - so this reports a crossing of the breakpoint and nothing else.
/// </para>
/// <para>
/// The state to compare against is the arrangement that was actually <b>built</b>, not the first width seen:
/// a page is constructed before any layout pass, when its width is still unknown, so the first real width is
/// usually the one that disagrees. Taking it as a baseline left the wide layout never applied - the hero card
/// sat under the race list on a 1400 pt window until a screenshot showed it.
/// </para>
/// </remarks>
public sealed class WidthSwitch
{
    private readonly Action _onChange;
    private bool _wide;

    /// <summary>Creates a switch that starts at <paramref name="initialWide"/> and calls <paramref name="onChange"/> when that flips.</summary>
    public WidthSwitch(bool initialWide, Action onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        _wide = initialWide;
        _onChange = onChange;
    }

    /// <summary>True when the last observed width had room for two panes.</summary>
    public bool IsWide => _wide;

    /// <summary>Reports a new width; rebuilds the caller only when the arrangement has to change.</summary>
    public void Observe(double width)
    {
        if (width <= 0)
        {
            // Some platforms report 0 before the first layout pass; that is not a resized window.
            return;
        }

        var wide = Responsive.IsWideAt(width);

        if (wide == _wide)
        {
            return;
        }

        _wide = wide;
        _onChange();
    }
}
