using System.Net;
using F1App.Telemetry.Media;

namespace F1App.Telemetry.Tests;

/// <summary>
/// Commons ranks hand-drawn layouts and other venues' photographs above the aerial of the circuit asked for, so
/// which result the circuit screen gets is decided here rather than by whichever file happens to come first.
/// </summary>
/// <remarks>
/// Every payload below is a trimmed copy of a real answer measured on 2026-11-28 against the 2026 calendar:
/// "Montréal circuit aerial" returned a 1978 layout drawing first and the 2018 SkySat photograph second,
/// "Jeddah circuit aerial" returned no photograph, and "Las Vegas circuit aerial" returned a photograph of
/// Caesars Palace, a circuit abandoned in 1984.
/// </remarks>
public sealed class WikimediaImageClientTests
{
    private const string DiagramThenPhotograph = """
        {"query":{"pages":[
          {"title":"File:F1 1978 circuit ile notre dame.png","imageinfo":[{
            "thumburl":"https://upload.wikimedia.org/diagram.png",
            "descriptionurl":"https://commons.wikimedia.org/wiki/File:F1_1978_circuit",
            "extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"Artist":{"value":"<a href='#'>A mapper</a>"}}}]},
          {"title":"File:Circuit de Spa-Francorchamps, April 22, 2018 SkySat.jpg","imageinfo":[{
            "thumburl":"https://upload.wikimedia.org/spa.jpg",
            "descriptionurl":"https://commons.wikimedia.org/wiki/File:Spa_SkySat",
            "extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"Artist":{"value":"Planet Labs"}}}]}]}}
        """;

    private const string OnlyDiagrams = """
        {"query":{"pages":[
          {"title":"File:Jeddah Corniche Circuit 2022.png","imageinfo":[{
            "thumburl":"https://upload.wikimedia.org/jeddah.png",
            "descriptionurl":"https://commons.wikimedia.org/wiki/File:Jeddah_Corniche_Circuit_2022",
            "extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"Artist":{"value":"A mapper"}}}]}]}}
        """;

    private const string RetiredCircuit = """
        {"query":{"pages":[
          {"title":"File:Caesars Palace Grand Prix circuit aerial photo 1981.jpg","imageinfo":[{
            "thumburl":"https://upload.wikimedia.org/vegas1981.jpg",
            "descriptionurl":"https://commons.wikimedia.org/wiki/File:Caesars_Palace_1981",
            "extmetadata":{"LicenseShortName":{"value":"CC BY-SA 3.0"},"Artist":{"value":"A photographer"}}}]}]}}
        """;

    [Fact]
    public async Task FindAerialAsync_DiagramRanksFirst_ReturnsThePhotographOfTheSameVenue()
    {
        var client = Client(DiagramThenPhotograph);

        var found = await client.FindAerialAsync("Spa-Francorchamps", CancellationToken.None);

        var image = UnionAssert.Success(found);
        Assert.Equal("Circuit de Spa-Francorchamps, April 22, 2018 SkySat.jpg", image.Title);
        Assert.Equal("Planet Labs", image.Artist);
    }

    [Fact]
    public async Task FindAerialAsync_CommonsHasOnlyLayouts_ReturnsAnAbsenceNotADrawing()
    {
        var client = Client(OnlyDiagrams);

        var found = await client.FindAerialAsync("Jeddah", CancellationToken.None);

        var failure = UnionAssert.Failure(found);
        Assert.Equal(204, failure.StatusCode);
        Assert.Contains("no aerial photograph", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindAerialAsync_AerialOfARetiredCircuit_IsNotShownForThisOne()
    {
        var client = Client(RetiredCircuit);

        var found = await client.FindAerialAsync("Las Vegas", CancellationToken.None);

        Assert.Equal(204, UnionAssert.Failure(found).StatusCode);
    }

    [Fact]
    public async Task FindAerialAsync_PhotographThatDoesNotNameTheVenue_IsAnAbsence()
    {
        // The price of the rule, measured and accepted: Montréal's aerial is titled with the circuit's official
        // name ("Circuit Gilles-Villeneuve"), so the city search does not recognise it. Costly, but a screen with
        // no photograph is honest and a photograph of another venue is not.
        const string montreal = """
            {"query":{"pages":[
              {"title":"File:Circuit Gilles-Villeneuve, May 29, 2018 SkySat.jpg","imageinfo":[{
                "thumburl":"https://upload.wikimedia.org/montreal.jpg",
                "descriptionurl":"https://commons.wikimedia.org/wiki/File:Gilles_Villeneuve_SkySat",
                "extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"Artist":{"value":"Planet Labs"}}}]}]}}
            """;
        var client = Client(montreal);

        var found = await client.FindAerialAsync("Montréal", CancellationToken.None);

        Assert.Equal(204, UnionAssert.Failure(found).StatusCode);
    }

    [Fact]
    public async Task FindAsync_PortraitSearch_StillTakesTheFirstResult()
    {
        var client = Client(DiagramThenPhotograph);

        // Portraits are unaffected: the aerial filter belongs to the circuit query, not to Commons in general.
        var found = await client.FindAsync("Some Subject", CancellationToken.None);

        Assert.Equal("F1 1978 circuit ile notre dame.png", UnionAssert.Success(found).Title);
    }

    private static WikimediaImageClient Client(string body) =>
        new(
            new HttpClient(new JsonHandler(body)),
            RequestGate.ForRequestsPerMinute(6000, TimeProvider.System, burst: 10),
            TimeProvider.System);

    /// <summary>Answers every request with the same Commons payload.</summary>
    private sealed class JsonHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
    }
}
