#:project C:/projects/F1MauiApp/src/F1App.Telemetry/F1App.Telemetry.csproj

using F1App.Telemetry.LiveFeed;

// Records what Formula 1's own live-timing hub actually sends, without the UI.
//
// Run it during a session - that is the whole point. Outside one the hub answers with a single
// {"type":6} keepalive and nothing else (measured 2026-11-28, between Qatar practice sessions), so an
// empty capture is the correct answer then and not a failure.
//
//   dotnet run tools/live-probe/probe.cs -- <capture-file> <seconds>
//   dotnet run tools/live-probe/probe.cs -- parse <capture-file>    # no network, just report on a capture
//
// The capture is the fixture TimingDataParser needs; ADR-009 keeps the hub off and observe-only until
// one exists, and the app's Settings screen writes the same file from the same call.
var capture = args.Length > 0 ? args[0] : null;

// "parse <file>" reads an existing capture and skips the network, so the reporting can be checked against a
// known-good frame instead of trusting that a silent run means the hub was silent.
var parseOnly = capture == "parse";
var file = parseOnly ? args.ElementAtOrDefault(1) : capture;
var seconds = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 20;

if (!parseOnly)
{
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
}
else
{
    capture = file;
}

// The point of the capture is the parser, so answer that question here instead of leaving it for later.
// Until a real session publishes TimingData, this prints nothing and the parser stays a shape we guessed.
if (capture is not null && File.Exists(capture))
{
    var lines = File.ReadAllLines(capture);
    var frames = 0;
    var malformed = 0;
    var parsed = 0;
    var drivers = new SortedSet<int>();

    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        frames++;

        // A real frame is one JSON object per line. Counting the lines that are not catches a pretty-printed
        // file being mistaken for a silent hub - the mistake this check was written after making.
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(line);
        }
        catch (System.Text.Json.JsonException)
        {
            malformed++;
            continue;
        }

        foreach (var standing in TimingDataParser.Parse(line))
        {
            parsed++;
            drivers.Add(standing.DriverNumber);
        }
    }

    Console.WriteLine($"capture lines  : {frames}" + (malformed > 0 ? $" ({malformed} not valid JSON)" : string.Empty));
    Console.WriteLine($"parser output  : {parsed} standings across {drivers.Count} drivers");
    Console.WriteLine(
        malformed > 0 && parsed == 0
            ? "verdict        : capture is not one JSON object per line - this is a file problem, not a silent hub"
            : parsed == 0
                ? "verdict        : the hub sent no TimingData - the parser's shape is still unverified"
                : "verdict        : real TimingData arrived; the parser reads it (check the numbers above)");
}
