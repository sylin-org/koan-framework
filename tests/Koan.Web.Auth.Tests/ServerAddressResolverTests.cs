using AwesomeAssertions;
using Koan.Web.Auth.Hosting;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>
/// WEB-0071 regression. The OIDC back-channel could not resolve a relative authority (the dev Test IdP's
/// <c>/.testoauth</c> base) under the most common container bind <c>ASPNETCORE_URLS=http://+:8080</c>, because
/// <see cref="System.Uri"/> cannot parse the wildcard form — leaving the authority relative and the discovery fetch
/// throwing "request URI must be absolute". <see cref="ServerAddressResolver"/> must understand the wildcard /
/// any-address bind forms and produce an absolute in-network URL.
/// </summary>
public sealed class ServerAddressResolverTests
{
    [Theory]
    [InlineData("http://+:8080", "http://localhost:8080/.testoauth")]       // the failing Docker form
    [InlineData("http://*:8080", "http://localhost:8080/.testoauth")]
    [InlineData("http://0.0.0.0:8080", "http://localhost:8080/.testoauth")]
    [InlineData("http://[::]:8080", "http://localhost:8080/.testoauth")]
    [InlineData("https://+:8443", "https://localhost:8443/.testoauth")]
    [InlineData("http://127.0.0.1:5000", "http://127.0.0.1:5000/.testoauth")] // concrete host already worked
    [InlineData("http://localhost:5000", "http://localhost:5000/.testoauth")]
    [InlineData("http://+:8080;https://+:8443", "http://localhost:8080/.testoauth")] // first http(s) entry wins
    public void Relative_authority_resolves_to_absolute_in_network_url(string urls, string expected)
        => ServerAddressResolver.ToAbsolute("/.testoauth", urls, null, null).Should().Be(expected);

    [Fact]
    public void Absolute_endpoint_passes_through_unchanged()
        => ServerAddressResolver.ToAbsolute("https://discord.com/api/oauth2/token", "http://+:8080", null, null)
            .Should().Be("https://discord.com/api/oauth2/token");

    [Theory]
    [InlineData(null, "8080", "http://localhost:8080/.testoauth")]  // chiseled images set *_PORTS, not ASPNETCORE_URLS
    [InlineData("8443", null, "https://localhost:8443/.testoauth")] // https port preferred when both present
    public void Falls_back_to_ASPNETCORE_PORTS_when_no_urls(string? httpsPorts, string? httpPorts, string expected)
        => ServerAddressResolver.ToAbsolute("/.testoauth", null, httpsPorts, httpPorts).Should().Be(expected);

    [Fact]
    public void Unresolvable_base_leaves_relative_unchanged()
        => ServerAddressResolver.ToAbsolute("/.testoauth", null, null, null).Should().Be("/.testoauth");
}
