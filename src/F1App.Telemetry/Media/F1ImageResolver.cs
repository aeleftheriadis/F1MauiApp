namespace F1App.Telemetry.Media;

/// <summary>
/// Picks the first candidate image URL that actually answers, and remembers it for the session.
/// </summary>
/// <remarks>
/// <para>
/// The candidates are handed in by the caller in preference order — F1 media cutout, then the provider's own
/// headshot, then Commons — because only the screen knows which sources it has. What lives here is the part
/// that must not vary: a <see langword="null"/> candidate is never requested, the first success wins and is
/// cached, and nothing in this path throws. A missing picture is a picture the screen draws differently, not
/// an error a person is shown.
/// </para>
/// <para>
/// The probe fetches with <see cref="HttpCompletionOption.ResponseHeadersRead"/> and disposes the response: the
/// point is to learn whether the URL is good, and the bytes are cached by the image control itself when it
/// renders from the URL.
/// </para>
/// </remarks>
public sealed class F1ImageResolver
{
    private readonly HttpClient _http;
    private readonly ImageMemoryCache _cache;

    /// <summary>Creates the resolver over one HTTP client and one session cache.</summary>
    public F1ImageResolver(HttpClient http, ImageMemoryCache cache)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(cache);

        _http = http;
        _cache = cache;
    }

    /// <summary>Returns the first candidate that answers with an image, or <see langword="null"/>.</summary>
    public async Task<Uri?> ResolveAsync(
        IReadOnlyList<Uri?> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                continue;
            }

            var key = candidate.ToString();

            if (_cache.TryGet(key, out _))
            {
                return candidate;
            }

            try
            {
                using var response = await _http
                    .GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                if (response.Content.Headers.ContentType?.MediaType is { } mediaType &&
                    !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                _cache.Set(key, bytes);
                return candidate;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                // A CDN that is slow, blocked or gone is not an error the screen should show: try the next one.
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }
        }

        return null;
    }
}
