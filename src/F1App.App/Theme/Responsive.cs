namespace F1App.Theme;

/// <summary>
/// The one place that decides whether a screen is drawn for a phone or for a wide window.
/// </summary>
/// <remarks>
/// <para>
/// The layouts were designed against a phone-width prototype, so on a desktop they read as a stretched phone
/// app: one narrow column of rows across a metre of screen. Rather than maintaining two UIs, each screen asks
/// this type which arrangement to build, and the content, bindings and view models stay shared.
/// </para>
/// <para>
/// <c>F1APP_PHONE=1</c> forces the phone arrangement on a desktop build. That is not a toy: both arrangements
/// are only verified because the switch exists, and an unexercised branch is an unverified one.
/// </para>
/// </remarks>
public static class Responsive
{
    /// <summary>Widest a content column gets on a desktop, so a table does not stretch to 3000 pt.</summary>
    public const double MaxContentWidth = 1400;

    /// <summary>Below this width (device-independent units) a screen is laid out as one column.</summary>
    public const double Breakpoint = 900;

    /// <summary>True when the screen is a phone shape and therefore gets the single-column arrangement.</summary>
    public static bool IsPhone =>
        Environment.GetEnvironmentVariable("F1APP_PHONE") == "1"
        || DeviceInfo.Idiom == DeviceIdiom.Phone;

    /// <summary>
    /// True when there is room for two panes, judged from the idiom alone.
    /// </summary>
    /// <remarks>
    /// Pages that watch their own width should use <see cref="IsWideAt"/> instead; this is the value to start
    /// from before the first layout pass reports a width.
    /// </remarks>
    public static bool IsWide => !IsPhone && DeviceInfo.Idiom != DeviceIdiom.Watch;

    /// <summary>
    /// True when a window of <paramref name="width"/> has room for two panes.
    /// </summary>
    /// <remarks>
    /// A desktop window can be dragged narrow - a split-screen window is 700 pt - and a two-pane layout at that
    /// width leaves two columns of clipped text. The breakpoint is what stops that happening.
    /// </remarks>
    public static bool IsWideAt(double width) => !IsPhone && width >= Breakpoint;

    /// <summary>
    /// Two panes when wide, stacked when narrow.
    /// </summary>
    /// <param name="primary">The pane a user reads first: the list, the map, the table, the roster.</param>
    /// <param name="secondary">The detail pane: facts, dossier, records, hero card.</param>
    /// <param name="primaryShare">
    /// Width of <paramref name="primary"/> as a fraction of the row on a wide screen. Clamped to 0.3-0.8 so a
    /// pane can never collapse to nothing.
    /// </param>
    /// <param name="secondaryFirstOnPhone">
    /// Put the detail above the list on a phone. The Paddock wants its dossier on top; a race list does not.
    /// </param>
    /// <param name="wide">
    /// The arrangement to build. Pass a <see cref="WidthSwitch.IsWide"/> so a resized window re-arranges;
    /// omit it to use the idiom.
    /// </param>
    public static View Panes(
        View primary,
        View secondary,
        double primaryShare = 0.62,
        bool secondaryFirstOnPhone = false,
        bool? wide = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);

        if (!(wide ?? IsWide))
        {
            // Narrow: one column. The flexible pane takes the remaining space, the fixed one sizes to content.
            var stacked = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                },
                BackgroundColor = Themes.Current.Background,
            };

            if (secondaryFirstOnPhone)
            {
                stacked.Add(secondary, 0, 0);
                stacked.Add(primary, 0, 1);
            }
            else
            {
                stacked.RowDefinitions[0].Height = GridLength.Star;
                stacked.Add(primary, 0, 0);
                stacked.Add(secondary, 0, 1);
            }

            return stacked;
        }

        var share = Math.Clamp(primaryShare, 0.3, 0.8);

        var panes = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(share, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1 - share, GridUnitType.Star)),
            },
            ColumnSpacing = Tokens.Space3,
            Padding = new Thickness(Tokens.Space3, 0),
            BackgroundColor = Themes.Current.Background,
        };

        panes.Add(primary, 0);
        panes.Add(secondary, 1);
        return panes;
    }

    /// <summary>Centres content and caps its width, so a wide window does not stretch a table edge to edge.</summary>
    public static View Centred(View content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new Grid
        {
            BackgroundColor = Themes.Current.Background,
            Children =
            {
                new Grid
                {
                    MaximumWidthRequest = MaxContentWidth,
                    HorizontalOptions = LayoutOptions.Fill,
                    Children = { content },
                },
            },
        };
    }
}
