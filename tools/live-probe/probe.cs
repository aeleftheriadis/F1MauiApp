#:project C:/projects/F1MauiApp/src/F1App.Telemetry/F1App.Telemetry.csproj

using F1App.Telemetry.LiveFeed;

// Records what Formula 1's own live-timing hub actually sends, without the UI.
//
// Run it during a session - that is the whole point. Outside one the hub answers with a single
// {"type":6} keepalive and nothing else (measured 2026-11-28, between Qatar practice sessions), so an
// empty capture is the correct answer then and not a failure.
//
//   dotnet run tools/live-probe/probe.cs -- <capture-file> <seconds>
//
// The capture is the fixture TimingDataParser needs; ADR-009 keeps the hub off and observe-only until
// one exists, and the app's Settings screen writes the same file from the same call.
var capture = args.Length > 0 ? args[0] : null;
var seconds = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 20;

Console.WriteLine($"probing {seconds}s, capture={capture ?? "(none)"}");
var started = DateTimeOffset.UtcNow;
var observation = await OfficialLiveTimingClient.ObserveAsync(
    TimeSpan.FromSeconds(seconds), capture, CancellationToken.None);

var elapsed = DateTimeOffset.UtcNow - started;
Console.WriteLine($"elapsed {elapsed.TotalSeconds:F1}s");
Console.WriteLine($"error          : {observation.Error ?? "(none)"}");
Console.WriteLine($"frames         : {observation.FramesObserved}");
Console.WriteLine($"captured       : {observation.FramesCaptured} -> {observation.CapturePath ?? "(none)"}");
Console.WriteLine($"targets        : {string.Join(", ", observation.FrameTargets)}");
Console.WriteLine($"standings      : {observation.StandingsParsed}");
