using F1App.Telemetry.Providers;

namespace F1App.Telemetry.Tests;

/// <summary>
/// Guards the numbers the request gates are built from against the providers' published budgets.
/// </summary>
/// <remarks>
/// These were wrong in a way no test noticed: Jolpica was paced at 120 requests a minute - about 7 200 an
/// hour - against a published 500. A limit that is only recorded in a comment drifts; arithmetic in a test
/// does not.
/// </remarks>
public sealed class ProviderLimitsTests
{
    [Fact]
    public void JolpicaSustainedPacing_StaysWithinThePublishedHourlyBudget()
    {
        var perHour = ProviderLimits.JolpicaPerMinute * 60;

        Assert.True(
            perHour <= ProviderLimits.JolpicaPerHour,
            $"pacing {ProviderLimits.JolpicaPerMinute}/min is {perHour}/hour, over the published {ProviderLimits.JolpicaPerHour}/hour");
    }

    [Fact]
    public void JolpicaBurst_StaysWithinThePublishedBurstBudget()
    {
        // jolpica-f1/docs/rate_limits.md: burst 4 requests per second.
        Assert.InRange(ProviderLimits.JolpicaBurst, 1, 4);
    }

    [Fact]
    public void OpenF1Burst_StaysWithinTheMeasuredTolerance()
    {
        // Measured: a burst of five returns 429 from OpenF1, so three is the ceiling that works.
        Assert.InRange(ProviderLimits.OpenF1Burst, 1, 3);
    }
}
