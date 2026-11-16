using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace F1App.Telemetry.LiveFeed;

/// <summary>What a probe of the official live-timing hub actually saw.</summary>
public sealed record LiveFeedObservation(
    bool Negotiated,
    bool Connected,
    bool Subscribed,
    int FramesObserved,
    ImmutableArray<string> FrameTargets,
    int StandingsParsed,
    int FramesCaptured,
    string? CapturePath,
    string? Error);

/// <summary>
/// Connects to Formula 1's own live-timing hub and reports what it receives.
/// </summary>
/// <remarks>
/// <para>
/// Verified against the live endpoint: the negotiate request answers 200 with a connection token, the
/// WebSocket accepts the connection, and the JSON protocol handshake is acknowledged. What has
/// <b>not</b> been verified is the payload, because the hub is silent outside a session - so this type
/// deliberately stops at observation and does not feed the telemetry store. A parser will land only
/// once a real session confirms the shapes.
/// </para>
/// <para>
/// Caveats that matter more than the code: the endpoint is undocumented, it can change without notice,
/// and Formula 1's terms of use are unlikely to bless a third-party client. It is not paid, but it is
/// not "open" either, so it stays off unless the user turns it on.
/// </para>
/// </remarks>
public static class OfficialLiveTimingClient
{
    private const string NegotiateUrl = "https://livetiming.formula1.com/signalrcore/negotiate?negotiateVersion=1";
    private const string SocketUrl = "wss://livetiming.formula1.com/signalrcore";
    private const char RecordSeparator = '\u001e';

    /// <summary>Topics the community clients subscribe to; the hub accepts this list silently.</summary>
    private static readonly string[] Topics =
    [
        "Heartbeat",
        "DriverList",
        "TimingData",
        "TimingAppData",
        "SessionInfo",
        "SessionStatus",
        "LapCount",
        "Position.z",
        "CarData.z",
        "RaceControlMessages",
    ];

    /// <summary>
    /// Opens a connection, subscribes, and reports what arrives within <paramref name="duration"/>.
    /// </summary>
    /// <param name="duration">How long to listen.</param>
    /// <param name="capturePath">
    /// When set, every received frame is appended to that file, one per line. This is how the parser stops
    /// being theoretical: the first live session produces a fixture that can be replayed offline.
    /// </param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static async Task<LiveFeedObservation> ObserveAsync(
        TimeSpan duration,
        string? capturePath,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            CookieContainer = new System.Net.CookieContainer(),
            UseCookies = true,
        };

        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

        string? token;
        try
        {
            using var negotiate = await http
                .PostAsync(NegotiateUrl, new StringContent(string.Empty), cancellationToken)
                .ConfigureAwait(false);

            if (!negotiate.IsSuccessStatusCode)
            {
                return Failed($"negotiate returned {(int)negotiate.StatusCode}");
            }

            using var document = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            token = document.RootElement.TryGetProperty("connectionToken", out var value) ? value.GetString() : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return Failed(exception.Message);
        }

        if (string.IsNullOrEmpty(token))
        {
            return Failed("negotiate response carried no connection token");
        }

        var cookies = string.Join(
            "; ",
            handler.CookieContainer
                .GetCookies(new Uri("https://livetiming.formula1.com"))
                .Cast<System.Net.Cookie>()
                .Select(static cookie => $"{cookie.Name}={cookie.Value}"));

        using var socket = new ClientWebSocket();

        if (cookies.Length > 0)
        {
            socket.Options.SetRequestHeader("Cookie", cookies);
        }

        try
        {
            await socket
                .ConnectAsync(new Uri($"{SocketUrl}?id={Uri.EscapeDataString(token)}"), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WebSocketException or TaskCanceledException)
        {
            return new LiveFeedObservation(true, false, false, 0, [], 0, 0, null, exception.Message);
        }

        try
        {
            await SendAsync(socket, """{"protocol":"json","version":1}""", cancellationToken).ConfigureAwait(false);

            var handshake = await ReceiveAsync(socket, duration, cancellationToken).ConfigureAwait(false);
            var negotiated = handshake.Contains("{}", StringComparison.Ordinal);

            await SendAsync(socket, BuildSubscribe(), cancellationToken).ConfigureAwait(false);

            var targets = ImmutableArray.CreateBuilder<string>();
            var frames = 0;
            var standings = 0;
            var captured = 0;
            StreamWriter? capture = null;

            if (!string.IsNullOrWhiteSpace(capturePath))
            {
                var directory = Path.GetDirectoryName(capturePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                capture = new StreamWriter(capturePath, append: true);
            }

            await using var captureScope = capture as IAsyncDisposable ?? new NoopAsyncDisposable();
            var deadline = DateTimeOffset.UtcNow + duration;

            while (DateTimeOffset.UtcNow < deadline)
            {
                var raw = await ReceiveAsync(socket, deadline - DateTimeOffset.UtcNow, cancellationToken)
                    .ConfigureAwait(false);

                if (raw.Length == 0)
                {
                    break;
                }

                foreach (var frame in raw.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    frames++;

                    if (TryReadTarget(frame, out var target) && !targets.Contains(target))
                    {
                        targets.Add(target);
                    }

                    if (capture is not null && frame != "{}")
                    {
                        await capture.WriteLineAsync(frame).ConfigureAwait(false);
                        captured++;
                    }

                    // Parsed for diagnostics only: nothing here reaches the screens until a live session
                    // confirms the payload shape.
                    var parsed = TimingDataParser.Parse(frame);
                    if (parsed.Length > standings)
                    {
                        standings = parsed.Length;
                    }
                }
            }

            return new LiveFeedObservation(
                true,
                true,
                negotiated,
                frames,
                targets.ToImmutable(),
                standings,
                captured,
                captured > 0 ? capturePath : null,
                null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiveFeedObservation(true, true, true, 0, [], 0, 0, null, "no frames within the observation window");
        }
        catch (Exception exception) when (exception is WebSocketException or JsonException)
        {
            return new LiveFeedObservation(true, true, false, 0, [], 0, 0, null, exception.Message);
        }
    }

    private static string BuildSubscribe()
    {
        // Built by hand rather than with JsonSerializer: the AOT analyzers reject the reflection
        // overload, and a source-generated context for one constant frame is not worth the ceremony.
        var topics = string.Join(",", Topics.Select(static topic => $"\"{topic}\""));
        return $"{{\"arguments\":[[{topics}]],\"target\":\"Subscribe\",\"type\":1}}";
    }

    private static bool TryReadTarget(string frame, out string target)
    {
        target = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(frame);

            if (document.RootElement.TryGetProperty("target", out var value) && value.GetString() is { Length: > 0 } name)
            {
                target = name;
                return true;
            }

            target = document.RootElement.TryGetProperty("type", out var type)
                ? $"type={type.GetInt32()}"
                : "unknown";

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, string json, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(json + RecordSeparator);
        await socket
            .SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string> ReceiveAsync(
        ClientWebSocket socket,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        if (budget <= TimeSpan.Zero || socket.State != WebSocketState.Open)
        {
            return string.Empty;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);

        var buffer = new byte[64 * 1024];
        var builder = new StringBuilder();

        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return string.Empty;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    return builder.ToString();
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return string.Empty;
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static LiveFeedObservation Failed(string error) =>
        new(false, false, false, 0, [], 0, 0, null, error);
}
