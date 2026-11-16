using System.Collections.Immutable;
using System.Xml.Linq;

namespace F1App.Telemetry.News;

/// <summary>What kind of feed an item came from.</summary>
public enum NewsKind
{
    /// <summary>Written article.</summary>
    Article,

    /// <summary>Audio show.</summary>
    Podcast,

    /// <summary>Video channel entry.</summary>
    Video,
}

/// <summary>A headline from a public feed. The app shows a link, never the article body.</summary>
public sealed record NewsItem(string Title, string Source, Uri Link, DateTimeOffset? Published, NewsKind Kind);

/// <summary>One public, keyless feed.</summary>
public sealed record NewsSource(string Name, string Url, NewsKind Kind);

/// <summary>
/// The feeds this app reads, each verified to answer 200 without a key.
/// </summary>
/// <remarks>
/// Headlines and links only: article bodies stay on the publisher's site, which is both the copyright
/// position and the reason no scraping is needed. A podcast feed was left out because its payload is
/// 3.5 MB per poll - the YouTube channel feed gives the same "audio/video" affordance at 24 KB.
/// </remarks>
public static class NewsSources
{
    /// <summary>Every configured feed.</summary>
    public static readonly ImmutableArray<NewsSource> All =
    [
        new("Formula 1", "https://www.formula1.com/en/latest/all.xml", NewsKind.Article),
        new("Autosport", "https://www.autosport.com/rss/f1/news/", NewsKind.Article),
        new("Motorsport.com", "https://www.motorsport.com/rss/f1/news/", NewsKind.Article),
        new("The Race", "https://the-race.com/feed/", NewsKind.Article),
        new("RaceFans", "https://www.racefans.net/feed/", NewsKind.Article),
        new("BBC Sport", "https://feeds.bbci.co.uk/sport/formula1/rss.xml", NewsKind.Article),
        new("Crash.net", "https://www.crash.net/rss/f1", NewsKind.Article),
        new("Motorsport Week", "https://www.motorsportweek.com/feed/", NewsKind.Article),
        new("r/formula1", "https://www.reddit.com/r/formula1/.rss", NewsKind.Article),
        new("F1 on YouTube", "https://www.youtube.com/feeds/videos.xml?channel_id=UCB_qr75-ydFVKSF9Dmo6izg", NewsKind.Video),
    ];
}

/// <summary>Parses RSS 2.0 and Atom feeds into <see cref="NewsItem"/>s.</summary>
public static class NewsFeedReader
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// Reads as many items as the document contains.
    /// </summary>
    /// <exception cref="System.Xml.XmlException">The document is not well-formed XML.</exception>
    public static ImmutableArray<NewsItem> Read(Stream stream, NewsSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var document = XDocument.Load(stream);
        var root = document.Root ?? throw new System.Xml.XmlException("feed has no root element");

        return root.Name.LocalName switch
        {
            "rss" => ReadRss(document, source),
            "feed" => ReadAtom(document, source),
            _ => [],
        };
    }

    private static ImmutableArray<NewsItem> ReadRss(XDocument document, NewsSource source)
    {
        var items = document.Descendants("item");
        var builder = ImmutableArray.CreateBuilder<NewsItem>();

        foreach (var item in items)
        {
            var title = item.Element("title")?.Value.Trim();
            var link = item.Element("link")?.Value.Trim();

            if (!TryLink(link, out var uri) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            builder.Add(new NewsItem(
                Clean(title),
                source.Name,
                uri,
                ParseDate(item.Element("pubDate")?.Value),
                source.Kind));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<NewsItem> ReadAtom(XDocument document, NewsSource source)
    {
        var entries = document.Descendants(Atom + "entry");
        var builder = ImmutableArray.CreateBuilder<NewsItem>();

        foreach (var entry in entries)
        {
            var title = entry.Element(Atom + "title")?.Value.Trim();

            var link = entry.Elements(Atom + "link")
                .FirstOrDefault(element => element.Attribute("rel")?.Value is "alternate" or null)
                ?.Attribute("href")?.Value;

            if (!TryLink(link, out var uri) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var published = entry.Element(Atom + "published")?.Value ?? entry.Element(Atom + "updated")?.Value;

            builder.Add(new NewsItem(
                Clean(title),
                source.Name,
                uri,
                ParseDate(published),
                source.Kind));
        }

        return builder.ToImmutable();
    }

    private static bool TryLink(string? value, out Uri uri)
    {
        uri = null!;

        return !string.IsNullOrWhiteSpace(value)
               && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
               && parsed.Scheme is "http" or "https"
               && (uri = parsed) is not null;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static string Clean(string value) =>
        System.Net.WebUtility.HtmlDecode(value).ReplaceLineEndings(" ").Trim();
}
