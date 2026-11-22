namespace F1App.Telemetry.Providers;

/// <summary>
/// The free-tier budget of every provider, in one place.
/// </summary>
/// <remarks>
/// These numbers are quoted from each provider's own documentation and re-verified before being relied on.
/// They are referenced by the request gates (pacing) and by the resilience pipelines (retry and circuit
/// breaking), so a provider's budget cannot end up written twice and drift.
/// </remarks>
public static class ProviderLimits
{
    /// <summary>OpenF1 free tier: 30 requests per minute, and a burst of 3 is what the docs demonstrate.</summary>
    /// <remarks>
    /// Measured: a burst of five requests in a second returns 429 from OpenF1, so the pacing is not optional.
    /// </remarks>
    public const int OpenF1PerMinute = 30;

    /// <summary>Requests allowed back to back before the gate starts spacing them out.</summary>
    public const int OpenF1Burst = 3;

    /// <summary>Jolpica publishes no figure on its landing page; this is the self-imposed cap.</summary>
    public const int JolpicaPerMinute = 120;

    /// <summary>Wikimedia Commons asks clients to be conservative; this is deliberately low.</summary>
    public const int CommonsPerMinute = 30;

    /// <summary>RSS publishers vary wildly; one conservative bucket covers all of them.</summary>
    public const int NewsPerMinute = 20;

    /// <summary>Burst allowed for each provider, matching the gate the client builds.</summary>
    public const int JolpicaBurst = 4;

    /// <summary>Burst allowed for Wikimedia Commons.</summary>
    public const int CommonsBurst = 2;

    /// <summary>Burst allowed for the news feeds.</summary>
    public const int NewsBurst = 10;

    /// <summary>How many times a transient failure is retried before it is reported.</summary>
    public const int RetryAttempts = 3;

    /// <summary>First retry delay; each subsequent attempt doubles it, plus jitter.</summary>
    public static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>Failures in a row that open the circuit before a provider is asked again.</summary>
    public const int CircuitBreakerFailures = 5;

    /// <summary>How long the circuit stays open once it trips.</summary>
    public static readonly TimeSpan CircuitBreakerDuration = TimeSpan.FromSeconds(30);
}
