using AwesomeAssertions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

/// <summary>
/// WEB-0081: Koan notes the origins requests arrive on other than the base URL in force — counted, with when each was
/// first and last seen — in memory, bounded, the least recent evicted, each told apart by address class, and says once
/// when a new one appears. On a real Kestrel host, a declared proxy's forwarded scheme and host count and an
/// undeclared one's do not.
/// </summary>
public sealed class ObservedOriginsSpec
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Distinct_origins_are_counted_with_when_they_were_first_and_last_seen()
    {
        var time = new SteppedTime(Start);
        var origins = new KoanWebOrigins(new KoanWebBaseUrl("https://place.example"), 8, time);

        origins.Note(Request("http", "127.0.0.1:5220"));
        time.Advance(TimeSpan.FromMinutes(1));
        origins.Note(Request("https", "other.example"));
        time.Advance(TimeSpan.FromMinutes(1));
        origins.Note(Request("http", "127.0.0.1:5220"));
        origins.Note(Request("HTTP", "127.0.0.1:5220"));

        var seen = origins.Seen();
        seen.Select(origin => origin.Origin).Should().Equal("http://127.0.0.1:5220", "https://other.example");
        seen[0].Count.Should().Be(3);
        seen[0].FirstSeen.Should().Be(Start);
        seen[0].LastSeen.Should().Be(Start.AddMinutes(2));
        seen[1].Count.Should().Be(1);
        seen[1].FirstSeen.Should().Be(Start.AddMinutes(1));
    }

    [Fact]
    public async Task The_base_url_in_force_is_not_an_observed_origin()
    {
        var baseUrl = new KoanWebBaseUrl(null, store: new MemoryApplicationUrlStore("https://first.example"));
        var origins = new KoanWebOrigins(baseUrl, 8, new SteppedTime(Start));
        await baseUrl.Accept("https://first.example", TestContext.Current.CancellationToken);

        origins.Note(Request("https", "first.example"));
        origins.Note(Request("https", "FIRST.example:443"));
        origins.Seen().Should().BeEmpty("the base URL in force, however its Host is spelled, is not a candidate");

        origins.Note(Request("https", "second.example"));
        await baseUrl.Accept("https://second.example", TestContext.Current.CancellationToken);
        origins.Note(Request("https", "first.example"));

        origins.Seen().Select(origin => origin.Origin).Should().Equal(["https://first.example"],
            "once another URL is accepted the previous one is an origin like any other, and the new one is not");
    }

    [Fact]
    public void Without_a_base_url_in_force_every_origin_is_observed()
    {
        var origins = new KoanWebOrigins(new KoanWebBaseUrl(null), 8, new SteppedTime(Start));

        origins.Note(Request("http", "localhost:5220"));
        origins.Note(Request("https", "place.example"));

        origins.Seen().Select(origin => origin.Origin).Should().BeEquivalentTo(["http://localhost:5220", "https://place.example"]);
    }

    [Fact]
    public void The_record_is_bounded_and_evicts_the_least_recent_origin()
    {
        var time = new SteppedTime(Start);
        var origins = new KoanWebOrigins(new KoanWebBaseUrl("https://place.example"), 2, time);

        origins.Note(Request("https", "a.example"));
        time.Advance(TimeSpan.FromSeconds(1));
        origins.Note(Request("https", "b.example"));
        time.Advance(TimeSpan.FromSeconds(1));
        origins.Note(Request("https", "a.example"));
        time.Advance(TimeSpan.FromSeconds(1));
        origins.Note(Request("https", "c.example"));

        origins.Seen().Select(origin => origin.Origin).Should().Equal("https://c.example", "https://a.example");
    }

    [Theory]
    [InlineData("localhost:5220", OriginAddressClass.Loopback)]
    [InlineData("localhost.:5220", OriginAddressClass.Loopback)]
    [InlineData("LOCALHOST.:5220", OriginAddressClass.Loopback)]
    [InlineData("app.localhost.", OriginAddressClass.Loopback)]
    [InlineData("127.0.0.1.:5220", OriginAddressClass.Loopback)]
    [InlineData("0.0.0.0.:5220", OriginAddressClass.Unspecified)]
    [InlineData("10.0.0.5.", OriginAddressClass.Private)]
    [InlineData("tangent.example.org.", OriginAddressClass.Public)]
    [InlineData("app.localhost", OriginAddressClass.Loopback)]
    [InlineData("127.0.0.1:5220", OriginAddressClass.Loopback)]
    [InlineData("127.8.9.10", OriginAddressClass.Loopback)]
    [InlineData("[::1]:5220", OriginAddressClass.Loopback)]
    [InlineData("0.0.0.0:5220", OriginAddressClass.Unspecified)]
    [InlineData("[::]:5220", OriginAddressClass.Unspecified)]
    [InlineData("10.0.0.5", OriginAddressClass.Private)]
    [InlineData("172.20.1.1", OriginAddressClass.Private)]
    [InlineData("192.168.1.10:5220", OriginAddressClass.Private)]
    [InlineData("100.64.0.1", OriginAddressClass.Private)]
    [InlineData("169.254.10.10", OriginAddressClass.Private)]
    [InlineData("[fd00::1]", OriginAddressClass.Private)]
    [InlineData("[fe80::1]", OriginAddressClass.Private)]
    [InlineData("203.0.113.9", OriginAddressClass.Public)]
    [InlineData("[2001:db8::1]", OriginAddressClass.Public)]
    [InlineData("tangent.example.org", OriginAddressClass.Public)]
    public void Each_origin_is_told_apart_by_its_address_class(string host, OriginAddressClass expected)
    {
        var origins = new KoanWebOrigins(new KoanWebBaseUrl("https://place.example"), 8, new SteppedTime(Start));

        origins.Note(Request("http", host));

        var seen = origins.Seen().Should().ContainSingle().Subject;
        seen.AddressClass.Should().Be(expected);
        seen.IsLocal.Should().Be(expected is OriginAddressClass.Loopback or OriginAddressClass.Unspecified);
    }

    [Fact]
    public void A_new_origin_is_reported_once()
    {
        var origins = new KoanWebOrigins(new KoanWebBaseUrl("https://place.example"), 8, new SteppedTime(Start));
        var appeared = new List<ObservedOrigin>();
        origins.Appeared += (_, origin) => appeared.Add(origin);
        origins.Appeared += (_, _) => throw new InvalidOperationException("a failing observer must not fail the request");

        for (var i = 0; i < 5; i++) origins.Note(Request("https", "tangent.example.org"));
        origins.Note(Request("http", "127.0.0.1:5220"));
        origins.Note(Request("https", "place.example"));

        appeared.Select(origin => origin.Origin).Should().Equal("https://tangent.example.org", "http://127.0.0.1:5220");
        appeared[0].Count.Should().Be(1);
        appeared[0].AddressClass.Should().Be(OriginAddressClass.Public);
        appeared[1].IsLocal.Should().BeTrue();
        origins.Seen().Single(origin => origin.Origin == "https://tangent.example.org").Count.Should().Be(5);
    }

    [Fact]
    public void A_limit_below_one_is_refused_naming_the_key()
    {
        var create = () => new KoanWebOrigins(new KoanWebBaseUrl(null), 0);

        create.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Koan:Web:ObservedOriginLimit");
    }

    [Fact]
    public async Task A_declared_proxy_s_forwarded_scheme_and_host_are_the_origin_observed()
    {
        await using var host = await TrustedProxiesSpec.ProxiedHost.Start(new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "127.0.0.1" });

        await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "tangent.example.org"), ("X-Forwarded-For", "203.0.113.9"));

        var seen = host.Services.GetRequiredService<KoanWebOrigins>().Seen();
        seen.Select(origin => origin.Origin).Should().Equal("https://tangent.example.org");
        seen[0].AddressClass.Should().Be(OriginAddressClass.Public);
    }

    [Fact]
    public async Task An_undeclared_proxy_s_forwarded_headers_are_not_the_origin_observed()
    {
        await using var host = await TrustedProxiesSpec.ProxiedHost.Start(new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "10.0.0.0/8" });

        await host.Probe(("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "tangent.example.org"), ("X-Forwarded-For", "203.0.113.9"));

        var seen = host.Services.GetRequiredService<KoanWebOrigins>().Seen();
        seen.Select(origin => origin.Origin).Should().Equal(host.ListenUrl);
        seen[0].IsLocal.Should().BeTrue();
    }

    private static HttpRequest Request(string scheme, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.Path = "/";
        return context.Request;
    }

    private sealed class SteppedTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
