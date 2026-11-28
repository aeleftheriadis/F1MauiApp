using F1App.Telemetry.Media;

namespace F1App.Services;

/// <summary>
/// Turns driver, team and circuit identifiers into loadable images, with the fallback order the spec fixes.
/// </summary>
/// <remarks>
/// The order is cutout, then the provider's own headshot, then Commons, then nothing. "Nothing" is a real
/// answer: the screen draws a designed fallback rather than an empty frame, and the resolver never requests a
/// URL that the catalogue does not vouch for.
/// </remarks>
public sealed class F1Images
{
    private readonly F1ImageResolver _resolver;

    /// <summary>Creates the service over the shared resolver.</summary>
    public F1Images(F1ImageResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>A driver's picture, or <see langword="null"/> when no source answers.</summary>
    public Task<ImageSource?> DriverAsync(
        string? driverCode,
        Uri? openF1Headshot,
        Uri? commonsImage,
        CancellationToken cancellationToken,
        int width = 720) =>
        FirstAsync(
            new Uri?[] { F1MediaCatalog.DriverCutout(driverCode, width), openF1Headshot, commonsImage },
            cancellationToken);

    /// <summary>A team's car cutout, or <see langword="null"/>.</summary>
    public Task<ImageSource?> CarAsync(string? teamName, CancellationToken cancellationToken) =>
        FirstAsync(
            new Uri?[] { F1MediaCatalog.TeamCar(teamName), F1MediaCatalog.TeamLogo(teamName) },
            cancellationToken);

    /// <summary>A race's circuit card by slug, or <see langword="null"/> when the slug is not known.</summary>
    public Task<ImageSource?> CircuitAsync(string? raceSlug, CancellationToken cancellationToken) =>
        FirstAsync(new Uri?[] { F1MediaCatalog.CircuitCard(raceSlug) }, cancellationToken);

    /// <summary>A race's circuit card by the meeting name the feeds use.</summary>
    public Task<ImageSource?> CircuitForMeetingAsync(string? meetingName, CancellationToken cancellationToken) =>
        FirstAsync(new Uri?[] { F1MediaCatalog.CircuitCardForMeeting(meetingName) }, cancellationToken);

    /// <summary>A picture from any URL, resolved the same way: bytes in memory, never a URL the control fetches.</summary>
    public Task<ImageSource?> RemoteAsync(Uri? url, CancellationToken cancellationToken) =>
        FirstAsync(new[] { url }, cancellationToken);

    private async Task<ImageSource?> FirstAsync(IReadOnlyList<Uri?> candidates, CancellationToken cancellationToken)
    {
        var url = await _resolver.ResolveAsync(candidates, cancellationToken).ConfigureAwait(true);

        if (url is null)
        {
            return null;
        }

        // The control must never fetch the URL itself: it sends no User-Agent, and Wikimedia's thumbnail host
        // answers 403 to an agentless request (measured 2026-11-28), which draws an empty frame. The bytes the
        // resolver already downloaded are handed over instead, and they stay in memory only - a UriImageSource
        // would write them to the app's cache folder, which ADR-019 forbids for F1's artwork.
        if (!_resolver.TryGetBytes(url, out var bytes))
        {
            return null;
        }

        return ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
    }
}
