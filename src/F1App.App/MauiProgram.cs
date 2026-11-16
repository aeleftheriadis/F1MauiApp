using F1App.Domain;
using F1App.Features.Facts;
using F1App.Features.News;
using F1App.Features.PitWall;
using F1App.Features.Paddock;
using F1App.Features.Season;
using F1App.Features.Compare;
using F1App.Features.Results;
using F1App.Features.Strategy;
using F1App.Features.Settings;
using F1App.Features.TimingTower;
using F1App.Features.TrackMap;
using F1App.Features.Weekend;
using F1App.Services;
using F1App.Telemetry;
using F1App.Telemetry.Providers;
using F1App.Telemetry.News;
using F1App.Telemetry.Replay;
using F1App.Telemetry.Circuits;
using F1App.Telemetry.Facts;
using F1App.Telemetry.Media;
using F1App.Telemetry.Strategy;
using F1App.Telemetry.Timing;
using F1App.Telemetry.Weekend;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace F1App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp();

        // One gate per provider: the free tiers are per-credential budgets, not per-call.
        // Burst 3: a session load issues three requests back to back (outline, positions, telemetry).
        // Pacing them instead would stall the first screen for seconds for no benefit.
        var openF1Gate = RequestGate.ForRequestsPerMinute(30, TimeProvider.System, burst: 3);
        var jolpicaGate = RequestGate.ForRequestsPerMinute(10, TimeProvider.System);

        // The champions wall costs one request per season, so it needs its own bucket: pacing it at the
        // calendar's 6 s interval turned twenty seasons into two minutes. The 24 h cache, not this gate,
        // is what keeps the hourly total tiny.
        var factsGate = RequestGate.ForRequestsPerMinute(120, TimeProvider.System, burst: 4);
        var newsGate = RequestGate.ForRequestsPerMinute(20, TimeProvider.System, burst: 10);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // Reddit's feed answers 429 to anything that is not browser-shaped; the rest of the feeds
            // do not care. One header, applied once, for all of them.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) f1telemetry/0.1");
            return http;
        });

        builder.Services.AddSingleton(sp => new OpenF1Client(sp.GetRequiredService<HttpClient>(), openF1Gate));
        builder.Services.AddSingleton(sp => new JolpicaClient(sp.GetRequiredService<HttpClient>(), jolpicaGate));
        builder.Services.AddSingleton(sp => new JolpicaFactsClient(sp.GetRequiredService<HttpClient>(), factsGate));
        builder.Services.AddSingleton<ChampionsService>();

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "f1app.db");

        builder.Services.AddSingleton(sp => new SqliteRaceCalendarStore(
            databasePath,
            sp.GetRequiredService<JolpicaClient>()));

        builder.Services.AddSingleton(_ => new SqliteFactsCache(databasePath));
        builder.Services.AddSingleton<IRaceCalendarStore>(sp => sp.GetRequiredService<SqliteRaceCalendarStore>());

        builder.Services.AddSingleton<TelemetrySnapshotStore>();
        builder.Services.AddSingleton<OpenF1TrackOutlineLoader>();
        builder.Services.AddSingleton<OpenF1ReplayLoader>();
        builder.Services.AddSingleton<OpenF1CircuitCatalogue>();
        builder.Services.AddSingleton<OpenF1StrategyLoader>();
        builder.Services.AddSingleton(sp => new JolpicaCircuitWinners(
            sp.GetRequiredService<HttpClient>(),
            jolpicaGate,
            sp.GetRequiredService<JolpicaClient>(),
            sp.GetRequiredService<SqliteFactsCache>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<OpenF1SessionTimingLoader>();
        builder.Services.AddSingleton<DefaultSessionResolver>();
        builder.Services.AddSingleton<SessionRunner>();
        builder.Services.AddSingleton(sp => new OpenF1WeekendLoader(
            sp.GetRequiredService<OpenF1Client>(),
            sp.GetRequiredService<TimeProvider>()));

        builder.Services.AddSingleton(sp => new NewsService(
            sp.GetRequiredService<HttpClient>(),
            newsGate,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<NewsService>>()));

        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<TimingTowerViewModel>();
        builder.Services.AddSingleton<TimingTowerPage>();
        builder.Services.AddSingleton<CircuitMapViewModel>();
        builder.Services.AddSingleton<TrackMapPage>();
        builder.Services.AddSingleton<WeekendViewModel>();
        builder.Services.AddSingleton<WeekendPage>();
        builder.Services.AddSingleton<NewsViewModel>();
        builder.Services.AddSingleton<NewsPage>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<ResultsViewModel>();
        builder.Services.AddSingleton<ResultsPage>();
        builder.Services.AddSingleton<FactsViewModel>();
        builder.Services.AddSingleton<FactsPage>();
        builder.Services.AddSingleton<StrategyViewModel>();
        builder.Services.AddSingleton<StrategyPage>();
        builder.Services.AddSingleton<CompareViewModel>();
        builder.Services.AddSingleton<ComparePage>();
        builder.Services.AddSingleton<PitWallViewModel>();
        builder.Services.AddSingleton<PitWallPage>();
        builder.Services.AddSingleton<SeasonHubViewModel>();
        builder.Services.AddSingleton<SeasonHubPage>();
        // Commons asks clients to be conservative: its own bucket, two at a time, not the F1 providers' gates.
        var commonsGate = RequestGate.ForRequestsPerMinute(30, TimeProvider.System, burst: 2);
        builder.Services.AddSingleton(sp => new WikimediaImageClient(
            sp.GetRequiredService<HttpClient>(),
            commonsGate,
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(_ => new SqliteMediaCache(databasePath));
        builder.Services.AddTransient<PaddockViewModel>();
        builder.Services.AddTransient<PaddockPage>();

        // The shell needs a factory so a tab builds its page on first navigation, not at start-up.
        builder.Services.AddSingleton<Func<PaddockPage>>(sp => () => sp.GetRequiredService<PaddockPage>());

        // File logging first: an exception the user sees on screen must leave a trace behind.
        builder.Logging.AddProvider(Diagnostics.Initialize(FileSystem.AppDataDirectory));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
