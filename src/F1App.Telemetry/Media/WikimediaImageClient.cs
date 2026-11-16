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

        var recent = await SearchAsync($"{subject} {year}", cancellationToken).ConfigureAwait(false);
        if (recent is OpenImage image)
        {
            return image;
        }

        return await SearchAsync(subject, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FetchResult<OpenImage>> SearchAsync(string query, CancellationToken cancellationToken)
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

        foreach (var page in pages)
        {
            var info = page.ImageInfo is { Count: > 0 } list ? list[0] : null;
            var preview = info?.ThumbUrl ?? info?.Url;

            if (string.IsNullOrWhiteSpace(preview) || string.IsNullOrWhiteSpace(info?.DescriptionUrl))
            {
                continue;
            }

            return new OpenImage(
                Title: page.Title.Replace("File:", string.Empty, StringComparison.Ordinal),
                PreviewUrl: preview,
                FilePageUrl: info.DescriptionUrl,
                Licence: Clean(info.Metadata?.Licence?.Value) ?? "unknown licence",
                Artist: Clean(info.Metadata?.Artist?.Value) ?? "unknown author");
        }

        return new HttpFailure(204, $"no usable image in the first results for '{query}'");
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
