using System.Net;
using System.Text;
using Azure.Core;

namespace Assistant.UnitTests.Infrastructure;

internal sealed class FakeCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new("test-token", DateTimeOffset.MaxValue);

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}

/// <summary>Routes requests to canned JSON responses by URL fragment and records what was called.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly List<(string Fragment, HttpStatusCode Status, string Json, IReadOnlyDictionary<string, string>? Headers)> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> RequestBodies { get; } = [];

    public bool NeverCompletes { get; set; }

    public StubHandler On(string urlFragment, string json, HttpStatusCode status = HttpStatusCode.OK, IReadOnlyDictionary<string, string>? headers = null)
    {
        _routes.Add((urlFragment, status, json, headers));
        return this;
    }

    public int CallsTo(string urlFragment) => Requests.Count(r => r.RequestUri!.ToString().Contains(urlFragment, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        if (NeverCompletes)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        // Several routes for the same fragment are served in registration order; the last one repeats.
        var matches = _routes.Where(r => request.RequestUri!.ToString().Contains(r.Fragment, StringComparison.Ordinal)).ToList();
        if (matches.Count == 0)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var calls = Requests.Count(r => r.RequestUri!.ToString().Contains(matches[0].Fragment, StringComparison.Ordinal));
        var route = matches[Math.Min(calls, matches.Count) - 1];

        var response = new HttpResponseMessage(route.Status) { Content = new StringContent(route.Json, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in route.Headers ?? new Dictionary<string, string>())
        {
            response.Headers.Add(name, value);
        }

        return response;
    }

    public HttpClient CreateClient(string baseAddress) => new(this, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
}
