using System.Net;
using System.Text;

namespace F1App.Telemetry.Tests;

/// <summary>
/// HttpMessageHandler stub: returns canned responses and records every request URI.
/// </summary>
/// <remarks>
/// Used instead of an interface in front of the clients. Mocking HttpClient's transport keeps the
/// real client (URL building, status mapping, JSON deserialisation) under test, which is the part
/// that actually breaks.
/// </remarks>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body, TimeSpan? RetryAfter)> _responses = new();
    private readonly List<(string Fragment, HttpStatusCode Status, string Body)> _routes = [];

    public List<Uri> Requests { get; } = [];

    public StubHttpHandler Enqueue(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        _responses.Enqueue((status, body, retryAfter));
        return this;
    }

    /// <summary>Answers any request whose URL contains <paramref name="urlFragment"/>. Checked before the queue.</summary>
    public StubHttpHandler Map(string urlFragment, HttpStatusCode status, string body)
    {
        _routes.Add((urlFragment, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        foreach (var route in _routes)
        {
            if (request.RequestUri!.ToString().Contains(route.Fragment, StringComparison.Ordinal))
            {
                return Task.FromResult(Respond(status: route.Status, body: route.Body, retryAfter: null, request));
            }
        }

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No stubbed response for {request.RequestUri}.");
        }

        var (queuedStatus, queuedBody, queuedRetryAfter) = _responses.Dequeue();
        return Task.FromResult(Respond(queuedStatus, queuedBody, queuedRetryAfter, request));
    }

    private static HttpResponseMessage Respond(
        HttpStatusCode status,
        string body,
        TimeSpan? retryAfter,
        HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };

        if (retryAfter is { } delay)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
        }

        return response;
    }
}
