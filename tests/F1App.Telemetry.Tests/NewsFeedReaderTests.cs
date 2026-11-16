using System.Text;
using F1App.Telemetry.News;

namespace F1App.Telemetry.Tests;

public sealed class NewsFeedReaderTests
{
    private static readonly NewsSource Source = new("Test", "https://example.test/feed", NewsKind.Article);

    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel>
          <title>Example</title>
          <item>
            <title>Verstappen fastest in FP2 &amp; &#8216;confident&#8217;</title>
            <link>https://example.test/a</link>
            <pubDate>Sat, 07 Mar 2026 16:02:11 +0000</pubDate>
          </item>
          <item>
            <title>No date on this one</title>
            <link>https://example.test/b</link>
          </item>
          <item>
            <title>Broken link</title>
            <link>javascript:alert(1)</link>
          </item>
        </channel></rss>
        """;

    private const string Atom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Subreddit</title>
          <entry>
            <title>Race weekend thread</title>
            <link rel="alternate" href="https://example.test/post/1"/>
            <published>2026-03-07T18:00:00+00:00</published>
          </entry>
        </feed>
        """;

    private static Stream Stream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void Read_ParsesRssItemsAndSkipsUnusableOnes()
    {
        var items = NewsFeedReader.Read(Stream(Rss), Source);

        // The javascript: link is dropped; undated items are kept.
        Assert.Equal(2, items.Length);
        // Both entities are decoded: &amp; and the numeric &#8216; / &#8217; pair around "confident".
        Assert.Equal("Verstappen fastest in FP2 & ‘confident’", items[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 3, 7, 16, 2, 11, TimeSpan.Zero), items[0].Published);
        Assert.Equal("Test", items[0].Source);
        Assert.Null(items[1].Published);
    }

    [Fact]
    public void Read_ParsesAtomEntriesWithAlternateLinks()
    {
        var items = NewsFeedReader.Read(Stream(Atom), Source);

        Assert.Single(items);
        Assert.Equal("Race weekend thread", items[0].Title);
        Assert.Equal("https://example.test/post/1", items[0].Link.ToString());
        Assert.NotNull(items[0].Published);
    }

    [Fact]
    public void Read_ReturnsEmptyForAnUnknownRootElement()
    {
        var items = NewsFeedReader.Read(Stream("<html><body/></html>"), Source);

        Assert.Empty(items);
    }

    [Fact]
    public void Read_ThrowsOnMalformedXml()
    {
        Assert.Throws<System.Xml.XmlException>(() => NewsFeedReader.Read(Stream("<rss><channel>"), Source));
    }

    [Fact]
    public void Read_KeepsTheKindOfTheSource()
    {
        var video = new NewsSource("F1 on YouTube", "https://example.test/v", NewsKind.Video);

        var items = NewsFeedReader.Read(Stream(Atom), video);

        Assert.Equal(NewsKind.Video, items[0].Kind);
    }
}
