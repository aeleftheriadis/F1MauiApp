using System.Net;
using F1App.Telemetry.Media;

namespace F1App.Telemetry.Tests;

/// <summary>
/// The fallback chain is the difference between a missing picture and a broken box, so each step is pinned.
/// </summary>
public sealed class F1ImageResolverTests
{
    [Fact]
    public async Task ResolveAsync_CutoutSucceeds_ReturnsTheCutoutAndCachesIt()
    {
        var cutout = F1MediaCatalog.DriverCutout("ANT")!;
        var handler = new MappedHandler().Map(cutout, HttpStatusCode.OK);
        var cache = new ImageMemoryCache(4);
        var resolver = new F1ImageResolver(new HttpClient(handler), cache);

        var result = await resolver.ResolveAsync([cutout], CancellationToken.None);

        Assert.Equal(cutout, result);
        Assert.True(cache.TryGet(cutout.ToString(), out _));
    }

    [Fact]
    public async Task ResolveAsync_CutoutReturns404_FallsBackToTheNextCandidate()
    {
        var cutout = F1MediaCatalog.DriverCutout("ANT")!;
        var headshot = new Uri("https://media.formula1.com/driver-fallback.png");
        var handler = new MappedHandler()
            .Map(cutout, HttpStatusCode.NotFound)
            .Map(headshot, HttpStatusCode.OK);

        var resolver = new F1ImageResolver(new HttpClient(handler), new ImageMemoryCache(4));

        Assert.Equal(headshot, await resolver.ResolveAsync([cutout, headshot], CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_EverythingFails_ReturnsNull()
    {
        var cutout = F1MediaCatalog.DriverCutout("ANT")!;
        var handler = new MappedHandler().Map(cutout, HttpStatusCode.NotFound);
        var resolver = new F1ImageResolver(new HttpClient(handler), new ImageMemoryCache(4));

        Assert.Null(await resolver.ResolveAsync([cutout], CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_NullCandidatesAreSkipped_AndNoRequestIsMadeForThem()
    {
        var handler = new MappedHandler();
        var resolver = new F1ImageResolver(new HttpClient(handler), new ImageMemoryCache(4));

        Assert.Null(await resolver.ResolveAsync([null, null], CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ResolveAsync_CachedImageIsNotRequestedAgain()
    {
        var cutout = F1MediaCatalog.DriverCutout("ANT")!;
        var handler = new MappedHandler().Map(cutout, HttpStatusCode.OK);
        var resolver = new F1ImageResolver(new HttpClient(handler), new ImageMemoryCache(4));

        await resolver.ResolveAsync([cutout], CancellationToken.None);
        await resolver.ResolveAsync([cutout], CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ResolveAsync_Cancellation_ReturnsNullInsteadOfThrowing()
    {
        var cutout = F1MediaCatalog.DriverCutout("ANT")!;
        var resolver = new F1ImageResolver(new HttpClient(new MappedHandler().Map(cutout, HttpStatusCode.OK)), new ImageMemoryCache(4));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Null(await resolver.ResolveAsync([cutout], cancelled.Token));
    }

    [Fact]
    public async Task ResolveAsync_AfterSuccess_KeepsTheBytesSoAControlNeedNotFetchTheUrl()
    {
        // Wikimedia's thumbnail host answers 403 to a request with no User-Agent - and a control handed a bare
        // URL sends none - so the screen renders from these bytes instead of letting the control fetch.
        var cutout = F1MediaCatalog.DriverCutout("ANT")!;
        var resolver = new F1ImageResolver(
            new HttpClient(new MappedHandler().Map(cutout, HttpStatusCode.OK)),
            new ImageMemoryCache(4));

        await resolver.ResolveAsync([cutout], CancellationToken.None);

        Assert.True(resolver.TryGetBytes(cutout, out var bytes));
        Assert.Equal(2, bytes.Length);
    }

    private sealed class MappedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpStatusCode> _map = [];

        public int RequestCount { get; private set; }

        public MappedHandler Map(Uri url, HttpStatusCode status)
        {
            _map[url.ToString()] = status;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var status = _map.TryGetValue(request.RequestUri!.ToString(), out var mapped)
                ? mapped
                : HttpStatusCode.NotFound;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent([0x01, 0x02]),
            });
        }
    }
}
