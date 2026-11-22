using System.Runtime.InteropServices;
using F1App.Features.Archive;
using F1App.Features.Compare;
using F1App.Features.Facts;
using F1App.Features.More;
using F1App.Features.News;
using F1App.Features.Paddock;
using F1App.Features.PitWall;
using F1App.Features.Races;
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
public sealed partial class AppShell : Shell
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

        // Pushed from an archive row with the race already chosen, so it takes no route parameters.
        Routing.RegisterRoute("racedetail", new ServicesRouteFactory(_services, typeof(RaceDetailPage)));

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
            Dispatcher.DispatchDelayed(
                TimeSpan.FromSeconds(3),
                () => _ = Environment.GetEnvironmentVariable("F1APP_STRESS") == "1"
                    ? StressAsync()
                    : WalkAsync());
        }
    }

    /// <summary>Shell shape follows the same switch the pages use, so shell and content never disagree.</summary>
    private static bool IsPhone => Responsive.IsPhone;

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

                if (route == "pitwall" && Shell.Current?.CurrentPage is PitWallPage pit)
                {
                    await PressPitWallTabsAsync(pit);
                }
            }
        }

        // The archive's second level: constructed and pushed, with no race selected so it cannot fetch.
        await WalkOneAsync("racedetail", "race detail (pushed)");
        await WalkOneAsync(IsPhone ? "//more" : "//archive", "back");
        Diagnostics.Write("INFO", null, "page walk complete");
        Diagnostics.Write("INFO", null, $"shots in {ShotsFolder()}");
    }

    /// <summary>
    /// Presses all four pit-wall section tabs, then leaves and returns to press them again.
    /// </summary>
    /// <remarks>
    /// The section switcher is the one interaction the route walk never touched, and the reported crash
    /// was exactly there: a click on NEWS closed the app with RO_E_CLOSED. Pressing them twice, with a
    /// full feed refresh in between, is what a person does by the time the feeds have arrived.
    /// </remarks>
    private static async Task PressPitWallTabsAsync(PitWallPage pit)
    {
        if (Environment.GetEnvironmentVariable("F1APP_CLICK") == "1")
        {
            foreach (var section in new[] { 2, 0, 3, 2 })
            {
                ClickAt(pit.SmokeSectionButton(section));
                await Task.Delay(section == 2 ? 5000 : 1200);

                if (section == 2)
                {
                    await CaptureAsync("pitwall-news");
                }
            }

            Diagnostics.Write("INFO", null, "pitwall tabs clicked");
            return;
        }

        for (var section = 0; section < 4; section++)
        {
            pit.SmokeSelectSection(section);
            await Task.Delay(400);
        }

        // Back to NEWS, and wait long enough for all nine feeds to answer or fail.
        pit.SmokeSelectSection(2);
        await Task.Delay(4000);

        for (var section = 0; section < 4; section++)
        {
            pit.SmokeSelectSection(section);
            await Task.Delay(300);
        }

        Diagnostics.Write("INFO", null, "pitwall tabs pressed");
    }

    /// <summary>Sends a real mouse click to a control's centre, the way a hand would.</summary>
    /// <remarks>
    /// Calling the handler directly is not the same thing: a pointer click reaches XAML's input and focus
    /// machinery first. The reported crash only exists on that path.
    /// </remarks>
    private static void ClickAt(Button button)
    {
        if (button?.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement native)
        {
            Diagnostics.Write("INFO", null, "click skipped: no native element");
            return;
        }

        var origin = native.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var x = origin.X + (native.ActualWidth / 2);
        var y = origin.Y + (native.ActualHeight / 2);

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            Diagnostics.Write("INFO", null, "click skipped: no window");
            return;
        }

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        Microsoft.UI.WindowId id = default;
        var point = new Win32.Point { X = (int)x, Y = (int)y };

        Win32.ClientToScreen(handle, ref point);
        Win32.SetForegroundWindow(handle);
        Win32.SetCursorPos(point.X, point.Y);
        Thread.Sleep(120);
        Win32.mouse_event(Win32.MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        Win32.mouse_event(Win32.MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        _ = id;
    }

    // DllImport, not LibraryImport: the source generator needs /unsafe in the app project for no gain here.
    private static class Win32
    {
        internal const int MouseEventLeftDown = 0x0002;
        internal const int MouseEventLeftUp = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    }

    private static string ShotsFolder() => Path.Combine(FileSystem.CacheDirectory, "shots");

    /// <summary>
    /// Saves a PNG of the current screen, so a layout can be looked at on a machine that cannot be touched.
    /// </summary>
    /// <remarks>
    /// This is the only way "the screen constructs" can become "the screen looks right" without a device in a
    /// hand: every queued visual check in docs/plan.md was blocked on exactly this.
    /// </remarks>
    private static async Task CaptureAsync(string name)
    {
        try
        {
            var folder = ShotsFolder();
            Directory.CreateDirectory(folder);

            var shot = await Screenshot.Default.CaptureAsync();
            await using var stream = File.Create(Path.Combine(folder, name + ".png"));
            await shot.CopyToAsync(stream);
        }
        catch (Exception exception)
        {
            Diagnostics.Write("PAGE", exception, $"screenshot {name}");
        }
    }

    /// <summary>
    /// Repeats what the walk never does: switch the palette and re-enter the same destinations.
    /// </summary>
    /// <remarks>
    /// Every page rebuilds its content on <c>Themes.Changed</c>, and several hold controls in fields that are
    /// re-parented on that rebuild - the exact shape of a native crash that leaves no managed trace. Run with
    /// <c>F1APP_SMOKE=1 F1APP_STRESS=1</c> to exercise it on this machine.
    /// </remarks>
    private async Task StressAsync()
    {
        string[] cycle = ["season", "pitwall", "archive", "results", "track", "timing"];

        for (var round = 0; round < 3; round++)
        {
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            {
                try
                {
                    Themes.Apply(mode, persist: false);
                    Diagnostics.Write("INFO", null, $"stress {round}: theme {mode}");
                }
                catch (Exception exception)
                {
                    Diagnostics.Write("PAGE", exception, $"stress theme {mode}");
                }

                foreach (var route in cycle)
                {
                    await WalkOneAsync($"//{route}", $"stress {round}/{mode} {route}");
                }
            }
        }

        // Resize across the breakpoint: the pages rebuild their arrangement when the width crosses it.
        if (Window is { } window && Environment.GetEnvironmentVariable("F1APP_PHONE") != "1")
        {
            foreach (var width in new[] { 700d, 1500d })
            {
                try
                {
                    window.Width = width;
                    Diagnostics.Write("INFO", null, $"stress: window width {width}");
                }
                catch (Exception exception)
                {
                    Diagnostics.Write("PAGE", exception, $"stress width {width}");
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
                await WalkOneAsync("//season", "stress resize season");
                await WalkOneAsync("//paddock", "stress resize paddock");
            }
        }

        Diagnostics.Write("INFO", null, "stress complete");
    }

    private static async Task WalkOneAsync(string target, string label)
    {
        try
        {
            Diagnostics.Write("INFO", null, $"walk -> {label}");
            await Shell.Current.GoToAsync(target);
            await Task.Delay(TimeSpan.FromSeconds(2));

            if (Environment.GetEnvironmentVariable("F1APP_SHOTS") == "1")
            {
                await CaptureAsync(label.Replace(' ', '-').Replace('/', '-'));
            }
        }
        catch (Exception exception)
        {
            Diagnostics.Write("PAGE", exception, $"route '{label}' failed");
        }
    }
}
