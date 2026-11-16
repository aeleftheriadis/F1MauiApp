using F1App.Features.Compare;
using F1App.Features.Facts;
using F1App.Features.Strategy;
using F1App.Features.News;
using F1App.Features.Paddock;
using F1App.Features.PitWall;
using F1App.Features.Season;
using F1App.Features.Results;
using F1App.Features.Settings;
using F1App.Features.TimingTower;
using F1App.Features.TrackMap;
using F1App.Features.Weekend;
using F1App.Services;
using F1App.Theme;

namespace F1App;

/// <summary>The application shell: a flyout menu over five destinations.</summary>
/// <remarks>
/// Flyout rather than tabs: the menu has to fit Weekend, Timing, Track, News and Settings on a phone, and
/// a five-tab bar is where labels start truncating. Pages are long-lived singletons handed to
/// <see cref="ShellContent.Content"/> directly, so switching destinations never rebuilds the timing tower.
/// </remarks>
public sealed class AppShell : Shell
{
    public AppShell(
        SeasonHubPage season,
        WeekendPage weekend,
        PitWallPage pitWall,
        Func<PaddockPage> paddockFactory,
        TimingTowerPage timing,
        TrackMapPage track,
        ResultsPage results,
        StrategyPage strategy,
        ComparePage compare,
        FactsPage facts,
        NewsPage news,
        SettingsPage settings,
        SessionRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        FlyoutBehavior = FlyoutBehavior.Flyout;
        FlyoutBackgroundColor = Themes.Current.Surface;

        // Shell chrome follows the app's own surface so the navigation bar is not a lighter rectangle
        // dropped on top of a dark app.
        ApplyChrome();

        Themes.Changed += (_, _) => ApplyChrome();

        Items.Add(Item("Season", "ic_weekend.png", season));
        Items.Add(Item("Weekend", "ic_weekend.png", weekend));
        Items.Add(Item("Pit wall", "ic_pitwall.png", pitWall));
        Items.Add(Item("Timing", "ic_timing.png", timing));
        Items.Add(Item("Track", "ic_track.png", track));
        Items.Add(LazyItem("Paddock", "ic_facts.png", paddockFactory));
        Items.Add(Item("Results", "ic_results.png", results));
        Items.Add(Item("Strategy", "ic_strategy.png", strategy));
        Items.Add(Item("Compare", "ic_compare.png", compare));
        Items.Add(Item("Facts", "ic_facts.png", facts));
        Items.Add(Item("News", "ic_news.png", news));
        Items.Add(Item("Settings", "ic_settings.png", settings));

        runner.Start();

        // F1APP_SMOKE=1 walks every destination once, logging any page that throws. It is how a crash
        // on a screen nobody opened on startup gets found without a click.
        if (Environment.GetEnvironmentVariable("F1APP_SMOKE") == "1")
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), () => _ = WalkAsync());
        }
    }

    /// <summary>Keeps the navigation bar and flyout on the current palette.</summary>
    private void ApplyChrome()
    {
        SetBackgroundColor(this, Themes.Current.Background);
        SetForegroundColor(this, Themes.Current.TextPrimary);
        SetTitleColor(this, Themes.Current.TextPrimary);
        SetUnselectedColor(this, Themes.Current.TextSecondary);
        SetNavBarHasShadow(this, false);
        FlyoutBackgroundColor = Themes.Current.Chrome;
    }

    private async Task WalkAsync()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];

            try
            {
                Services.Diagnostics.Write("INFO", null, $"walk -> {item.Title}");
                CurrentItem = item;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception)
            {
                Services.Diagnostics.Write("PAGE", exception, $"destination '{item.Title}' failed");
            }
        }

        Services.Diagnostics.Write("INFO", null, "page walk complete");
    }

    /// <summary>
    /// A destination whose page is built on first navigation.
    /// </summary>
    /// <remarks>
    /// The paddock tab builds a dossier view and touches the media cache; there is no reason for that work to
    /// happen at start-up for a tab nobody opened. Constructing it lazily also keeps a failure in one screen
    /// from taking down the shell.
    /// </remarks>
    private static FlyoutItem LazyItem(string title, string iconFile, Func<Page> factory) => new()
    {
        Title = title,
        Items =
        {
            new ShellContent
            {
                Title = title,
                Icon = ImageSource.FromFile(iconFile),
                ContentTemplate = new DataTemplate(factory),
            },
        },
    };

    private static FlyoutItem Item(string title, string iconFile, Page page) => new()
    {
        Title = title,
        Items =
        {
            new ShellContent
            {
                Title = title,
                Content = page,

                // Vector assets resized per DPI by the MAUI build: crisp on every density and themable
                // through the SVG's own stroke colour.
                Icon = ImageSource.FromFile(iconFile),
            },
        },
    };
}
