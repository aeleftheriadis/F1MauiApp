using F1App.Features.Compare;
using F1App.Features.Facts;
using F1App.Features.Strategy;
using F1App.Features.News;
using F1App.Features.PitWall;
using F1App.Features.Paddock;
using F1App.Features.Season;
using F1App.Features.Results;
using F1App.Features.Settings;
using F1App.Features.TimingTower;
using F1App.Features.TrackMap;
using F1App.Features.Weekend;
using F1App.Services;
using F1App.Theme;

namespace F1App;

/// <summary>Application entry: composition is the only place that knows the page graph.</summary>
public sealed class App : Application
{
    private readonly WeekendPage _weekend;
    private readonly TimingTowerPage _timing;
    private readonly PitWallPage _pitWall;
    private readonly SeasonHubPage _season;
    private readonly TrackMapPage _track;
    private readonly ResultsPage _results;
    private readonly FactsPage _facts;
    private readonly StrategyPage _strategy;
    private readonly ComparePage _compare;
    private readonly NewsPage _news;
    private readonly SettingsPage _settings;
    private readonly SessionRunner _runner;
    private readonly Func<PaddockPage> _paddockFactory;

    public App(
        WeekendPage weekend,
        TimingTowerPage timing,
        PitWallPage pitWall,
        Func<PaddockPage> paddockFactory,
        SeasonHubPage season,
        TrackMapPage track,
        ResultsPage results,
        FactsPage facts,
        StrategyPage strategy,
        ComparePage compare,
        NewsPage news,
        SettingsPage settings,
        SessionRunner runner)
    {
        _weekend = weekend;
        _timing = timing;
        _pitWall = pitWall;
        _season = season;
        _track = track;
        _results = results;
        _facts = facts;
        _strategy = strategy;
        _compare = compare;
        _news = news;
        _settings = settings;
        _runner = runner;
        _paddockFactory = paddockFactory;

        // Application.Current exists from here on, which UserAppTheme needs. Doing it before the shell is
        // built means every page is constructed against the palette the user chose.
        Themes.Initialize();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell(_season, _weekend, _pitWall, _paddockFactory, _timing, _track, _results, _strategy, _compare, _facts, _news, _settings, _runner))
        {
            Title = "F1 Telemetry",
        };
}
