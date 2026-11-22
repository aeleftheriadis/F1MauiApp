using System.Net;
using Polly;

namespace F1App.Telemetry.Providers;

/// <summary>
/// Retry and circuit-breaker settings for a provider whose free tier throttles.
/// </summary>
/// <remarks>
/// <para>
/// Pacing and retrying are different jobs and this app keeps them apart: <see cref="RequestGate"/> owns the
/// rate (the free tier is <see cref="ProviderLimits.OpenF1PerMinute"/> per minute, and it penalises a 429), while
/// this pipeline retries what a rate limit cannot fix - a timeout, a 5xx, a dropped connection - with exponential
/// backoff and jitter, and stops hammering a provider that is failing outright.
/// </para>
/// <para>
/// Retry does not include <c>429</c>: retrying into a throttle is how a client turns a one-minute penalty into a
/// five-minute ban. The gate waits instead.
/// </para>
/// </remarks>
public static class OpenF1Resilience
{
    /// <summary>Builds the pipeline the app uses for OpenF1.</summary>
    public static ResiliencePipeline<HttpResponseMessage> Create()
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = ProviderLimits.RetryAttempts,
                Delay = ProviderLimits.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = static args => ValueTask.FromResult(IsTransient(args.Outcome)),
            })
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = ProviderLimits.CircuitBreakerFailures,
                BreakDuration = ProviderLimits.CircuitBreakerDuration,
                ShouldHandle = static args => ValueTask.FromResult(IsTransient(args.Outcome)),
            })
            .Build();
    }

    /// <summary>True for the failures worth another attempt: transport errors and 5xx. Never a 429.</summary>
    public static bool IsTransient(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is HttpRequestException or TaskCanceledException)
        {
            return true;
        }

        if (outcome.Result is { } response)
        {
            return (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout;
        }

        return false;
    }
}
