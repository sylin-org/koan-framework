using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace Koan.Identity.Logins.Tests;

/// <summary>
/// A browser of one host: it keeps the cookies the host sets, and follows redirects within the host itself, so a sign-in
/// round trip runs as a person's browser runs it. A redirect elsewhere (a remote provider) stops the walk for the spec.
/// </summary>
public sealed class Browser : IDisposable
{
    private const int MostHops = 16;
    private readonly HttpClient _client;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    private readonly Uri? _alias;

    /// <param name="alias">The application's public address, which the host's own redirects may name; it is this host.</param>
    public Browser(HttpMessageHandler handler, Uri origin, Uri? alias = null)
    {
        _client = new HttpClient(handler, disposeHandler: false) { BaseAddress = origin };
        Origin = origin;
        _alias = alias;
    }

    public Uri Origin { get; }

    /// <summary>Sets a cookie as page script would, such as the Test provider's persona.</summary>
    public void Cookie(string name, string value) => _cookies[name] = value;

    public bool Holds(string name) => _cookies.ContainsKey(name);

    /// <summary>Goes to <paramref name="url"/> and follows the host's redirects to where they end, or to the first that leaves it.</summary>
    public Task<Landing> Go(string url) => Walk(HttpMethod.Get, new Uri(Origin, url), null, null);

    /// <summary>
    /// Submits a form of <paramref name="fields"/> to <paramref name="url"/> from a page of <paramref name="site"/>, as the
    /// browser says in <c>Sec-Fetch-Site</c>, then follows the redirects as <see cref="Go"/> does.
    /// </summary>
    public Task<Landing> Submit(string url, IReadOnlyDictionary<string, string> fields, string site = "same-origin")
        => Walk(HttpMethod.Post, new Uri(Origin, url), new FormUrlEncodedContent(fields), site);

    private async Task<Landing> Walk(HttpMethod method, Uri target, HttpContent? content, string? site)
    {
        var chain = new List<string>();
        for (var hop = 0; hop < MostHops; hop++)
        {
            using var request = new HttpRequestMessage(method, target) { Content = content };
            if (_cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(static c => $"{c.Key}={c.Value}")));
            if (site is not null) request.Headers.Add("Sec-Fetch-Site", site);
            (method, content, site) = (HttpMethod.Get, null, null);
            using var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
            Keep(response);
            chain.Add($"{(int)response.StatusCode} {target.PathAndQuery}");
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var location = response.Headers.Location;
            if ((int)response.StatusCode is < 300 or >= 400 || location is null)
                return new Landing(response.StatusCode, target, body, chain);
            Uri next = location.IsAbsoluteUri ? location : new Uri(target, location.OriginalString);
            if (_alias is not null && string.Equals(next.Authority, _alias.Authority, StringComparison.OrdinalIgnoreCase))
                next = new Uri(Origin, next.PathAndQuery);
            if (!string.Equals(next.Authority, Origin.Authority, StringComparison.OrdinalIgnoreCase))
                return new Landing(response.StatusCode, next, body, chain);
            target = next;
        }
        throw new InvalidOperationException("The redirects did not end: " + string.Join(" -> ", chain));
    }

    /// <summary>Who the host says is signed in: <c>/k52/whoami</c>.</summary>
    public async Task<Who> WhoAmI()
    {
        var landing = await Go(LoginProbeController.WhoAmI);
        landing.Status.Should().Be(HttpStatusCode.OK, landing.Trace);
        using var json = JsonDocument.Parse(landing.Body);
        var root = json.RootElement;
        // The host leaves out a property whose value is null.
        string? Read(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new Who(Read("id"), Read("providerSubject"), Read("did"), Read("handle"));
    }

    public async Task<HttpResponseMessage> Delete(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(Origin, url));
        request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(static c => $"{c.Key}={c.Value}")));
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Keep(response);
        return response;
    }

    public async Task<HttpResponseMessage> Post(string url, HttpContent? content = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Origin, url)) { Content = content };
        if (_cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(static c => $"{c.Key}={c.Value}")));
        if (bearer is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Keep(response);
        return response;
    }

    private void Keep(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return;
        foreach (var cookie in cookies)
        {
            var pair = cookie.Split(';', 2)[0];
            var split = pair.IndexOf('=');
            if (split <= 0) continue;
            var (name, value) = (pair[..split].Trim(), pair[(split + 1)..].Trim());
            var expired = cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
            if (expired || value.Length == 0) _cookies.Remove(name);
            else _cookies[name] = value;
        }
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Where a walk of redirects ended.</summary>
public sealed record Landing(HttpStatusCode Status, Uri At, string Body, IReadOnlyList<string> Chain)
{
    public string Trace => "chain:\n  " + string.Join("\n  ", Chain) + "\nbody: " + Body[..Math.Min(Body.Length, 600)];
}

/// <summary>Who a request is signed in as, and with which login.</summary>
public sealed record Who(string? Id, string? ProviderSubject, string? Did, string? Handle);
