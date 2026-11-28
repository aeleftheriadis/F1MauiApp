using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using F1App.Domain;

namespace F1App.Telemetry.Media;

/// <summary>A freely licensed picture, with the attribution its licence requires.</summary>
public sealed record OpenImage(
    string Title,
    string PreviewUrl,
    string FilePageUrl,
    string Licence,
    string Artist);

/// <summary>
/// Finds a freely licensed photo of a subject on Wikimedia Commons.
/// </summary>
/// <remarks>
/// Commons is queried through its public API with no key and no account, and every result carries the
/// licence and the author, which the app must display. Results are not always recent - a query for
/// "Max Verstappen" can return a 2017 photo - so the file title is shown alongside the attribution and the
/// current year is tried first. When nothing is found the caller gets a 204 and must say so rather than
/// fall back to a placeholder that looks like a real photo.
/// </remarks>
public sealed partial class WikimediaImageClient
{
    private const string Endpoint = "https://commons.wikimedia.org/w/api.php";

    private readonly HttpClient _http;
    private readonly RequestGate _gate;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the client.</summary>
    public WikimediaImageClient(HttpClient http, RequestGate gate, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _http = http;
        _gate = gate;
        _timeProvider = timeProvider;
    }

    /// <summary>Finds a photo for <paramref name="subject"/>, preferring one from the current year.</summary>
    public async Task<FetchResult<OpenImage>> FindAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var year = _timeProvider.GetUtcNow().Year;
        var query = $"{subject} {year}";

        var recent = await SearchAsync(query, cancellationToken).ConfigureAwait(false);

        if (recent is ImmutableArray<OpenImage> { Length: > 0 } candidates)
        {
            return candidates[0];
        }

        if (recent is RateLimited limited)
        {
            return limited;
        }

        // "Nothing this year" is not a failure to retry: the plain search may still answer, as it does for a
        // 2017 portrait of a driver whose 2026 photos do not exist yet.
        if (recent is HttpFailure { StatusCode: not 204 } failure)
        {
            return failure;
        }

        var plain = await SearchAsync(subject, cancellationToken).ConfigureAwait(false);

        return plain is ImmutableArray<OpenImage> { Length: > 0 } fallback
            ? fallback[0]
            : NoImage(plain, subject);
    }

    /// <summary>
    /// Finds an aerial photograph of a circuit, and nothing at all when Commons holds only layout diagrams.
    /// </summary>
    /// <remarks>
    /// Measured on 2026-11-28 against the 2026 calendar: Commons ranks hand-drawn layouts above the satellite
    /// photographs of the same venue - searching "Montréal circuit aerial" returns "F1 1978 circuit ile notre
    /// dame.png" first and the 2018 SkySat photograph second, and Jeddah and Miami have no photograph at all.
    /// A drawing is not a picture of the place, so the query is made once and only aerial-titled files are
    /// accepted; a venue with no aerial stays without one, which the screen already handles.
    /// </remarks>
    public async Task<FetchResult<OpenImage>> FindAerialAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var query = $"{subject} circuit aerial";
        var found = await SearchAsync(query, cancellationToken).ConfigureAwait(false);

        if (found is ImmutableArray<OpenImage> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (Aerial().IsMatch(candidate.Title) && NamesVenue(subject, candidate.Title))
                {
                    return candidate;
                }
            }

            return new HttpFailure(204, $"no aerial photograph of '{subject}' on Commons");
        }

        return NoImage(found, query);
    }

    /// <summary>
    /// True when the file's title carries the venue's own name, which is what stops a photograph of a
    /// different circuit being shown on this one's screen.
    /// </summary>
    /// <remarks>
    /// Measured 2026-11-28: "Las Vegas circuit aerial" answers with "Caesars Palace Grand Prix circuit aerial
    /// photo 1981.jpg" - a street circuit abandoned in 1984 - and "São Paulo circuit aerial" with a photograph
    /// titled only "Aerial view". Both are aerial and both are the wrong place, so the venue has to be named.
    /// It costs two correct photographs (COTA is titled "Circuit of the Americas", Mexico City's "Autódromo
    /// Hermanos Rodríguez"), which is the trade this rule makes on purpose.
    /// </remarks>
    private static bool NamesVenue(string subject, string title)
    {
        foreach (var word in subject.Split([' ', '-', '\''], StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 4 && title.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Carries a failed search's own reason, or names the absence when the search found nothing usable.</summary>
    private static FetchResult<OpenImage> NoImage(FetchResult<ImmutableArray<OpenImage>> found, string query) =>
        found switch
        {
            HttpFailure failure => failure,
            RateLimited limited => limited,
            _ => new HttpFailure(204, $"no freely licensed image found for '{query}'"),
        };

    private async Task<FetchResult<ImmutableArray<OpenImage>>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{Endpoint}?action=query&format=json&formatversion=2&generator=search" +
            $"&gsrsearch={Uri.EscapeDataString($"filetype:bitmap {query}")}&gsrnamespace=6&gsrlimit=5" +
            $"&prop=imageinfo&iiprop=url|extmetadata&iiurlwidth=640");

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var backoff = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
            _gate.Penalize(backoff);
            return new RateLimited((int)backoff.TotalSeconds);
        }

        if (!response.IsSuccessStatusCode)
        {
            return new HttpFailure((int)response.StatusCode, response.ReasonPhrase ?? "request failed");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var payload = await JsonSerializer
            .DeserializeAsync(stream, MediaJsonContext.Default.CommonsResponse, cancellationToken)
            .ConfigureAwait(false);

        var pages = payload?.Query?.Pages;

        if (pages is null || pages.Count == 0)
        {
            return new HttpFailure(204, $"no freely licensed image found for '{query}'");
        }

        var candidates = ImmutableArray.CreateBuilder<OpenImage>(pages.Count);

        foreach (var page in pages)
        {
            var info = page.ImageInfo is { Count: > 0 } list ? list[0] : null;
            var preview = info?.ThumbUrl ?? info?.Url;

            if (string.IsNullOrWhiteSpace(preview) || string.IsNullOrWhiteSpace(info?.DescriptionUrl))
            {
                continue;
            }

            candidates.Add(new OpenImage(
                Title: page.Title.Replace("File:", string.Empty, StringComparison.Ordinal),
                PreviewUrl: preview,
                FilePageUrl: info.DescriptionUrl,
                Licence: Clean(info.Metadata?.Licence?.Value) ?? "unknown licence",
                Artist: Clean(info.Metadata?.Artist?.Value) ?? "unknown author"));
        }

        return candidates.Count == 0
            ? new HttpFailure(204, $"no usable image in the first results for '{query}'")
            : candidates.ToImmutable();
    }

    /// <summary>Strips the HTML Commons puts in the artist field and collapses whitespace.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Tags().Replace(value, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>
    /// How Commons titles imagery taken from above: Planet's SkySat, Copernicus' Sentinel, Landsat, SPOT, and
    /// the plain-English cases. A hand-drawn circuit layout carries none of them.
    /// </summary>
    [GeneratedRegex(
        "skysat|satellite|sentinel|landsat|spot-\\d|aerial|air photo|earth observatory|from space",
        RegexOptions.IgnoreCase)]
    private static partial Regex Aerial();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Whitespace();
}

internal sealed record CommonsResponse(
    [property: JsonPropertyName("query")] CommonsQuery? Query);

internal sealed record CommonsQuery(
    [property: JsonPropertyName("pages")] List<CommonsPage>? Pages);

internal sealed record CommonsPage(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("imageinfo")] List<CommonsImageInfo>? ImageInfo);

internal sealed record CommonsImageInfo(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("thumburl")] string? ThumbUrl,
    [property: JsonPropertyName("descriptionurl")] string? DescriptionUrl,
    [property: JsonPropertyName("extmetadata")] CommonsMetadata? Metadata);

internal sealed record CommonsMetadata(
    [property: JsonPropertyName("LicenseShortName")] CommonsValue? Licence,
    [property: JsonPropertyName("Artist")] CommonsValue? Artist);

internal sealed record CommonsValue(
    [property: JsonPropertyName("value")] string? Value);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CommonsResponse))]
internal sealed partial class MediaJsonContext : JsonSerializerContext;
