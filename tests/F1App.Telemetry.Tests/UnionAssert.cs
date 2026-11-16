using F1App.Domain;
using Xunit.Sdk;

namespace F1App.Telemetry.Tests;

/// <summary>
/// Assertion helpers for <see cref="FetchResult{T}"/>.
/// </summary>
/// <remarks>
/// A union is not a boxed case value, so <c>Assert.IsType</c> cannot unwrap it; these helpers
/// pattern-match instead and report the actual case when the expectation fails.
/// </remarks>
internal static class UnionAssert
{
    public static T Success<T>(FetchResult<T> result) => result switch
    {
        null => throw new XunitException("expected a success value, got null"),
        T value => value,
        HttpFailure failure => throw new XunitException(
            $"expected a success value, got HttpFailure {failure.StatusCode}: {failure.Reason}"),
        RateLimited limited => throw new XunitException(
            $"expected a success value, got RateLimited (retry after {limited.RetryAfterSeconds}s)"),
    };

    public static HttpFailure Failure<T>(FetchResult<T> result) => result switch
    {
        HttpFailure failure => failure,
        _ => throw new XunitException($"expected HttpFailure, got {result.Value}"),
    };

    public static RateLimited Limited<T>(FetchResult<T> result) => result switch
    {
        RateLimited limited => limited,
        _ => throw new XunitException($"expected RateLimited, got {result.Value}"),
    };
}
