using F1App.Telemetry.Media;

namespace F1App.Telemetry.Tests;

/// <summary>
/// Pins the media catalogue to the URLs that were verified by request on 2026-11-22.
/// </summary>
/// <remarks>
/// The catalogue exists because F1's ids cannot be derived (georus01, lewham01, chalec01 follow no rule),
/// so every URL it hands out must be one somebody asked the CDN for. A test that pins the exact string is
/// what stops a "tidy-up" from inventing a prettier path.
/// </remarks>
public sealed class F1MediaCatalogTests
{
    [Fact]
    public void DriverCutout_ReturnsTheVerifiedUrlForAKnownDriver()
    {
        var url = F1MediaCatalog.DriverCutout("ANT");

        Assert.Equal(
            "https://media.formula1.com/image/upload/c_lfill,w_720/q_auto/v1740000001" +
            "/common/f1/2026/mercedes/andant01/2026mercedesandant01right.webp",
            url?.ToString());
    }

    [Fact]
    public void DriverCutout_ReturnsNullForAnUnknownDriver()
    {
        Assert.Null(F1MediaCatalog.DriverCutout("ZZZ"));
        Assert.Null(F1MediaCatalog.DriverCutout(null));
        Assert.Null(F1MediaCatalog.DriverCutout(string.Empty));
    }

    [Fact]
    public void TeamCar_And_TeamLogo_ExistForEveryKnownTeam()
    {
        string[] teams =
        [
            "Mercedes", "Red Bull Racing", "Ferrari", "McLaren", "Racing Bulls", "Alpine",
            "Haas F1 Team", "Audi", "Williams", "Aston Martin", "Cadillac",
        ];

        foreach (var team in teams)
        {
            Assert.NotNull(F1MediaCatalog.TeamCar(team));
            Assert.NotNull(F1MediaCatalog.TeamLogo(team));
        }
    }

    [Fact]
    public void TeamCar_UnknownTeam_ReturnsNull()
    {
        Assert.Null(F1MediaCatalog.TeamCar("Team Rocket"));
        Assert.Null(F1MediaCatalog.TeamCar(null));
    }

    [Fact]
    public void CircuitCard_KnownSlug_ReturnsTheCard()
    {
        var url = F1MediaCatalog.CircuitCard("qatar");

        Assert.NotNull(url);
        Assert.EndsWith("/fom-website/static-assets/2026/races/card/qatar.webp", url.ToString());
    }

    [Fact]
    public void CircuitCardForMeeting_UsesTheVerifiedSlug()
    {
        Assert.EndsWith("/card/las-vegas.webp", F1MediaCatalog.CircuitCardForMeeting("Las Vegas Grand Prix")?.ToString());
        Assert.Null(F1MediaCatalog.CircuitCardForMeeting("Abu Dhabi Grand Prix"));
        Assert.Null(F1MediaCatalog.CircuitCardForMeeting(null));
    }

    [Fact]
    public void CircuitCard_UnknownSlug_ReturnsNull()
    {
        Assert.Null(F1MediaCatalog.CircuitCard("never-heard-of-it"));
    }
}
