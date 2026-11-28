#if DEBUG
using Ailoha.Agent;
#endif
using F1App.Domain;
using F1App.Features.Archive;
using F1App.Features.Facts;
using F1App.Features.More;
using F1App.Features.News;
using F1App.Features.PitWall;
using F1App.Features.Races;
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
            .UseSkiaSharp()

            // Barlow Semi Condensed, SIL OFL 1.1 - the closest open relative of F1's own display face.
            // The OFL text ships beside the files in Resources/Fonts, as the licence requires.
            .ConfigureFonts(static fonts =>
            {
                fonts.AddFont("BarlowSemiCondensed-Regular.ttf", "BarlowRegular");
                fonts.AddFont("BarlowSemiCondensed-SemiBold.ttf", "BarlowSemiBold");
                fonts.AddFont("BarlowSemiCondensed-Bold.ttf", "BarlowBold");
            });

        // Dev-only, and after UseMauiApp: the agent hooks MAUI's own inspection providers, which do not exist
        // yet if this runs first. Debug builds only - a release build has no agent and no listening port.
#if DEBUG
        builder.AddMauiDevFlowAgent(options => options.EnableProfiler = true);
#endif

        // One gate per provider: the free tiers are per-credential budgets, not per-call.
        // Burst 3: a session load issues three requests back to back (outline, positions, telemetry).
        // Pacing them instead would stall the first screen for seconds for no benefit.
        var openF1Gate = RequestGate.ForRequestsPerMinute(
            ProviderLimits.OpenF1PerMinute, TimeProvider.System, burst: ProviderLimits.OpenF1Burst);
        // One Jolpica gate for every Jolpica client: the hourly cap is per IP, so separate buckets for the
        // calendar, the champions and the vault would together send several times the published budget.
        var jolpicaGate = RequestGate.ForRequestsPerMinute(
            ProviderLimits.JolpicaPerMinute, TimeProvider.System, burst: ProviderLimits.JolpicaBurst);

        // Pacing is the gate's job; this pipeline retries what pacing cannot fix (timeouts, 5xx) and stops
        // calling a provider that is failing outright. It never retries a 429 - that is a throttle, not a fault.
        var openF1Resilience = OpenF1Resilience.Create();

        var newsGate = RequestGate.ForRequestsPerMinute(20, TimeProvider.System, burst: 10);

        // F1's artwork is hotlinked and held in memory only: no disk cache, nothing to redistribute.
        builder.Services.AddSingleton(_ => new F1App.Telemetry.Media.ImageMemoryCache(capacity: 96));
        builder.Services.AddSingleton(sp => new F1App.Telemetry.Media.F1ImageResolver(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<F1App.Telemetry.Media.ImageMemoryCache>()));
        builder.Services.AddSingleton<F1Images>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // Some publishers 403 a non-browser agent; the rest do not care. One header, applied once,
            // for all of them.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) f1telemetry/0.1");
            return http;
        });

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "f1app.db");

        // Every provider response is kept in SQLite, so a restart re-uses what the free tier already paid for.
        builder.Services.AddSingleton(_ => new SqlitePayloadCache(databasePath));

        builder.Services.AddSingleton(sp => new OpenF1Client(
            sp.GetRequiredService<HttpClient>(),
            openF1Gate,
            openF1Resilience,
            sp.GetRequiredService<SqlitePayloadCache>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(sp => new JolpicaClient(sp.GetRequiredService<HttpClient>(), jolpicaGate));
        builder.Services.AddSingleton(sp => new JolpicaFactsClient(sp.GetRequiredService<HttpClient>(), jolpicaGate));
        // The vault costs one request per season since 1950 and syncs once, resuming where it stopped: at
        // the published 500/hour it takes about ten minutes the first time, and is then cached for good.
        builder.Services.AddSingleton(sp => new JolpicaRaceVaultClient(sp.GetRequiredService<HttpClient>(), jolpicaGate));
        builder.Services.AddSingleton(sp => new JolpicaSeasonResultsClient(sp.GetRequiredService<HttpClient>(), jolpicaGate));
        builder.Services.AddSingleton<ChampionsService>();
        builder.Services.AddSingleton<RaceVaultService>();
        builder.Services.AddSingleton<TeammateDuelService>();

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
        builder.Services.AddSingleton<ArchiveViewModel>();
        builder.Services.AddSingleton<ArchivePage>();
        builder.Services.AddSingleton(sp => new JolpicaRaceDetailClient(
            sp.GetRequiredService<HttpClient>(),
            jolpicaGate,
            sp.GetRequiredService<SqlitePayloadCache>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddTransient<RaceDetailViewModel>();
        builder.Services.AddTransient<RaceDetailPage>();
        builder.Services.AddSingleton<MorePage>();
        // Commons asks clients to be conservative: its own bucket, two at a time, not the F1 providers' gates.
        var commonsGate = RequestGate.ForRequestsPerMinute(30, TimeProvider.System, burst: 2);
        builder.Services.AddSingleton(sp => new WikimediaImageClient(
            sp.GetRequiredService<HttpClient>(),
            commonsGate,
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(_ => new SqliteMediaCache(databasePath));
        builder.Services.AddTransient<PaddockViewModel>();
        builder.Services.AddTransient<PaddockPage>();



        // File logging first: an exception the user sees on screen must leave a trace behind.
        builder.Logging.AddProvider(Diagnostics.Initialize(FileSystem.AppDataDirectory));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
