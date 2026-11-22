using F1App.Features.Archive;
using F1App.Features.Compare;
using F1App.Features.Facts;
using F1App.Features.More;
using F1App.Features.News;
using F1App.Features.Paddock;
using F1App.Features.PitWall;
using F1App.Features.Results;
using F1App.Features.Season;
using F1App.Features.Settings;
using F1App.Features.Strategy;
using F1App.Features.TimingTower;
using F1App.Features.TrackMap;
using F1App.Features.Weekend;
using F1App.Services;
using F1App.Theme;

namespace F1App;

/// <summary>The application shell: a bottom tab bar on phones, a flyout on desktop.</summary>
/// <remarks>
/// <para>
/// Each platform gets the navigation it is known for: a phone shows five tabs a thumb can reach and pushes
/// everything else from <see cref="MorePage"/>; Windows and Mac keep the flyout, which is how desktop apps
/// expose a dozen screens. The same pages serve both, so nothing is duplicated but the wiring.
/// </para>
/// <para>
/// Every destination is a <see cref="DataTemplate"/> over the service provider, not a page instance. That is
/// what makes start-up cheap: assigning <c>Content</c> directly would construct every page, its view model
/// and its timers before the first frame. It is also what makes a broken screen survivable - only the screen
/// being opened can fail.
/// </para>
/// <para>
/// Every <see cref="ShellContent"/> carries an explicit route. Without one MAUI invents
/// <c>D_FAULT_*</c> names from a shared counter, which shift whenever a destination is added or reordered and
/// cannot be navigated to reliably.
/// </para>
/// </remarks>
public sealed class AppShell : Shell
{
    /// <summary>Screens a phone reaches from the More tab, in the order they are listed there.</summary>
    internal static readonly (string Route, string Title, Type Page)[] Secondary =
    [
        ("weekend", "Weekend", typeof(WeekendPage)),
        ("results", "Results", typeof(ResultsPage)),
        ("strategy", "Strategy", typeof(StrategyPage)),
        ("track", "Track", typeof(TrackMapPage)),
        ("compare", "Compare", typeof(ComparePage)),
        ("timing", "Timing", typeof(TimingTowerPage)),
        ("facts", "Facts", typeof(FactsPage)),
        ("news", "News", typeof(NewsPage)),
        ("settings", "Settings", typeof(SettingsPage)),
    ];

    private readonly IServiceProvider _services;

    public AppShell(IServiceProvider services, SessionRunner runner)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(runner);

        _services = services;

        FlyoutBackgroundColor = Themes.Current.Surface;
        ApplyChrome();

        // Every secondary screen is registered wherever it is not a tab, so the same route string works
        // from the More list on a phone and from the flyout on desktop.
        foreach (var (route, _, page) in Secondary)
        {
            Routing.RegisterRoute(route, new ServicesRouteFactory(_services, page));
        }

        if (IsPhone)
        {
            Items.Add(BottomTabs());
            FlyoutBehavior = FlyoutBehavior.Disabled;
        }
        else
        {
            AddDesktopFlyout();
        }

        Themes.Changed += (_, _) => ApplyChrome();

        runner.Start();

        // F1APP_SMOKE=1 walks every destination once, logging any page that throws.
        if (Environment.GetEnvironmentVariable("F1APP_SMOKE") == "1")
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), () => _ = WalkAsync());
        }
    }

    /// <summary>
    /// True on a phone shape. <c>F1APP_PHONE=1</c> forces it, because the phone shell cannot otherwise be
    /// exercised on a desktop build - and an unexercised branch is an unverified one.
    /// </summary>
    private static bool IsPhone =>
        DeviceInfo.Idiom == DeviceIdiom.Phone
        || Environment.GetEnvironmentVariable("F1APP_PHONE") == "1";

    /// <summary>The phone shell: the five screens a fan opens most, as bottom tabs.</summary>
    private ShellItem BottomTabs() => Tabs(
        Content<SeasonHubPage>("Season", "season", "ic_weekend.png"),
        Content<PitWallPage>("Pit wall", "pitwall", "ic_pitwall.png"),
        Content<ArchivePage>("Archive", "archive", "ic_archive.png"),
        Content<PaddockPage>("Paddock", "paddock", "ic_paddock.png"),
        Content<MorePage>("More", "more", "ic_settings.png"));

    /// <summary>The desktop shell: every destination in the flyout, grouped by the question it answers.</summary>
    private void AddDesktopFlyout()
    {
        // Primary destinations first: the two a fan opens on a race weekend.
        Items.Add(Group("Live", "ic_timing.png",
            Content<PitWallPage>("Pit wall", "pitwall", "ic_pitwall.png"),
            Content<SeasonHubPage>("Season", "season", "ic_weekend.png")));

        Items.Add(Group("Championship", "ic_championship.png",
            Content<WeekendPage>("Weekend", "weekend", "ic_weekend.png"),
            Content<ResultsPage>("Results", "results", "ic_results.png"),
            Content<ArchivePage>("Archive", "archive", "ic_archive.png"),
            Content<StrategyPage>("Strategy", "strategy", "ic_strategy.png")));

        Items.Add(Group("Circuit", "ic_track.png",
            Content<TrackMapPage>("Track", "track", "ic_track.png"),
            Content<ComparePage>("Compare", "compare", "ic_compare.png"),
            Content<TimingTowerPage>("Timing", "timing", "ic_timing.png")));

        Items.Add(Group("People", "ic_facts.png",
            Content<PaddockPage>("Paddock", "paddock", "ic_paddock.png"),
            Content<FactsPage>("Facts", "facts", "ic_facts.png")));

        Items.Add(Group("Media", "ic_news.png",
            Content<NewsPage>("News", "news", "ic_news.png"),
            Content<SettingsPage>("Settings", "settings", "ic_settings.png")));
    }

    /// <summary>Keeps the navigation bar, flyout and tab bar on the current palette.</summary>
    private void ApplyChrome()
    {
        SetBackgroundColor(this, Themes.Current.Background);
        SetForegroundColor(this, Themes.Current.TextPrimary);
        SetTitleColor(this, Themes.Current.TextPrimary);
        SetUnselectedColor(this, Themes.Current.TextSecondary);
        SetTabBarBackgroundColor(this, Themes.Current.Chrome);
        SetTabBarTitleColor(this, Themes.Current.LiveText);
        SetTabBarUnselectedColor(this, Themes.Current.TextSecondary);
        SetNavBarHasShadow(this, false);
        FlyoutBackgroundColor = Themes.Current.Chrome;
    }

    /// <summary>A flyout group with one tab per destination inside it.</summary>
    private static FlyoutItem Group(string title, string icon, params ShellContent[] destinations)
    {
        var item = new FlyoutItem { Title = title, Icon = ImageSource.FromFile(icon) };

        foreach (var destination in destinations)
        {
            // The icon goes on the Tab as well as the shell content: the flyout draws the group, the tab
            // strip draws the tab, and an unset Tab.Icon is a blank slot rather than a fallback.
            item.Items.Add(new Tab { Icon = destination.Icon, Items = { destination } });
        }

        return item;
    }

    /// <summary>A bottom tab bar: one tab per destination, no flyout behind it.</summary>
    private static TabBar Tabs(params ShellContent[] destinations)
    {
        var bar = new TabBar();

        foreach (var destination in destinations)
        {
            bar.Items.Add(new Tab { Icon = destination.Icon, Title = destination.Title, Items = { destination } });
        }

        return bar;
    }

    /// <summary>A destination whose page is created by the container the first time it is opened.</summary>
    private ShellContent Content<TPage>(string title, string route, string icon)
        where TPage : Page => new()
    {
        Title = title,
        Route = route,
        Icon = ImageSource.FromFile(icon),
        ContentTemplate = new DataTemplate(() => _services.GetRequiredService<TPage>()),
    };

    /// <summary>
    /// Opens every destination once.
    /// </summary>
    /// <remarks>
    /// Routes are the shell's contract, so walking them proves both that every page builds and that its
    /// route resolves - the two things a mis-wired shell breaks silently. Screens that are pushed rather
    /// than tabbed are walked as pushes, on whichever shell is active.
    /// </remarks>
    private async Task WalkAsync()
    {
        string[] tabs =
        [
            "pitwall", "season", "weekend", "results", "archive", "strategy",
            "track", "compare", "timing", "paddock", "facts", "news", "settings",
        ];

        if (IsPhone)
        {
            // Five tabs, then the nine secondary screens as pushes - the same pages, reached the other way.
            foreach (var route in new[] { "season", "pitwall", "archive", "paddock", "more" })
            {
                await WalkOneAsync($"//{route}", route);
            }

            foreach (var (route, title, _) in Secondary)
            {
                // A registered route is pushed by its plain name; "more/{route}" would be resolved as a
                // shell-relative path and rejected. Pop back to the tab afterwards so the stack stays flat.
                await WalkOneAsync(route, $"{title} (pushed)");
                await WalkOneAsync("//more", "more");
            }
        }
        else
        {
            foreach (var route in tabs)
            {
                await WalkOneAsync($"//{route}", route);
            }
        }

        Diagnostics.Write("INFO", null, "page walk complete");
    }

    private static async Task WalkOneAsync(string target, string label)
    {
        try
        {
            Diagnostics.Write("INFO", null, $"walk -> {label}");
            await Shell.Current.GoToAsync(target);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception)
        {
            Diagnostics.Write("PAGE", exception, $"route '{label}' failed");
        }
    }
}
