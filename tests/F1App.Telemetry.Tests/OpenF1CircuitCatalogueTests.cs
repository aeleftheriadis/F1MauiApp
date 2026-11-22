using System.Net;
using F1App.Telemetry.Circuits;
using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Tests;

public sealed class OpenF1CircuitCatalogueTests
{
    private const string SeasonSessions = """
        [
          {"meeting_key":1300,"session_key":9001,"session_name":"Practice 1","session_type":"Practice","year":2026,"circuit_short_name":"Sakhir","location":"Sakhir","country_name":"Bahrain","date_start":"2026-03-06T11:30:00+00:00","date_end":"2026-03-06T12:30:00+00:00"},
          {"meeting_key":1300,"session_key":9005,"session_name":"Race","session_type":"Race","year":2026,"circuit_short_name":"Sakhir","location":"Sakhir","country_name":"Bahrain","date_start":"2026-03-08T15:00:00+00:00","date_end":"2026-03-08T17:00:00+00:00"},
          {"meeting_key":1301,"session_key":9101,"session_name":"Practice 1","session_type":"Practice","year":2026,"circuit_short_name":"Jeddah","location":"Jeddah","country_name":"Saudi Arabia","date_start":"2026-03-13T13:30:00+00:00","date_end":"2026-03-13T14:30:00+00:00"},
          {"meeting_key":1301,"session_key":9105,"session_name":"Race","session_type":"Race","year":2026,"circuit_short_name":"Jeddah","location":"Jeddah","country_name":"Saudi Arabia","date_start":"2026-03-15T17:00:00+00:00","date_end":"2026-03-15T19:00:00+00:00"}
        ]
        """;

    private static (OpenF1CircuitCatalogue Catalogue, StubHttpHandler Handler) Create()
    {
        // The catalogue asks for the season's meetings too (icons, flags, circuit type); empty is a
        // legitimate answer and keeps every existing test focused on the sessions it maps.
        var handler = new StubHttpHandler().Map("meetings?year=", HttpStatusCode.OK, "[]");
        var client = new OpenF1Client(
            new HttpClient(handler),
            new RequestGate(TimeSpan.FromMilliseconds(1), TimeProvider.System));

        return (new OpenF1CircuitCatalogue(client), handler);
    }

    [Fact]
    public async Task LoadSeasonAsync_ReturnsOneEntryPerMeetingInRunningOrder()
    {
        var (catalogue, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, SeasonSessions);

        var entries = UnionAssert.Success(await catalogue.LoadSeasonAsync(2026, CancellationToken.None));

        Assert.Equal(2, entries.Length);
        Assert.Equal("Sakhir", entries[0].Circuit.ShortName);
        Assert.Equal("sakhir", entries[0].Circuit.Id);
        Assert.Equal("Bahrain", entries[0].Circuit.Country);
        Assert.Equal(1300, entries[0].MeetingKey);

        // The race session, not practice, is what traces the circuit.
        Assert.Equal(9005, entries[0].RaceSessionKey);
        Assert.Equal(2, entries[0].SessionCount);
        Assert.Equal(9105, entries[1].RaceSessionKey);
    }

    [Fact]
    public async Task LoadSeasonAsync_CollapsesTestingIntoTheRaceMeeting()
    {
        // Pre-season testing and the race share a circuit; one payload holds both.
        const string WithTesting = """
            [
              {"meeting_key":1200,"session_key":8001,"session_name":"Practice 1","session_type":"Practice","year":2026,"circuit_short_name":"Sakhir","location":"Sakhir","country_name":"Bahrain","date_start":"2026-02-11T08:00:00+00:00","date_end":"2026-02-11T12:00:00+00:00"},
              {"meeting_key":1300,"session_key":9001,"session_name":"Practice 1","session_type":"Practice","year":2026,"circuit_short_name":"Sakhir","location":"Sakhir","country_name":"Bahrain","date_start":"2026-03-06T11:30:00+00:00","date_end":"2026-03-06T12:30:00+00:00"},
              {"meeting_key":1300,"session_key":9005,"session_name":"Race","session_type":"Race","year":2026,"circuit_short_name":"Sakhir","location":"Sakhir","country_name":"Bahrain","date_start":"2026-03-08T15:00:00+00:00","date_end":"2026-03-08T17:00:00+00:00"}
            ]
            """;

        var (catalogue, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, WithTesting);

        var entries = UnionAssert.Success(await catalogue.LoadSeasonAsync(2026, CancellationToken.None));

        Assert.Single(entries);
        Assert.Equal(1300, entries[0].MeetingKey);
        Assert.True(entries[0].HasRaceSession);
        Assert.Equal(9005, entries[0].RaceSessionKey);
    }

    [Fact]
    public async Task LoadSeasonAsync_ReportsAnUnpublishedSeasonRatherThanFailing()
    {
        var (catalogue, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, "[]");

        var failure = UnionAssert.Failure(await catalogue.LoadSeasonAsync(2027, CancellationToken.None));

        Assert.Equal(204, failure.StatusCode);
        Assert.Contains("2027", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadSeasonAsync_RejectsSeasonsOpenF1CannotServe()
    {
        var (catalogue, _) = Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => catalogue.LoadSeasonAsync(2019, CancellationToken.None));
    }
}
