using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using AwesomeAssertions;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// SEC-0004 origin — the stamping chokepoint <see cref="EntityRequestContextBuilder.Build"/>. For an HTTP request
/// the CONNECTION is authoritative: the builder resolves the tier from the remote IP + declared internal networks.
/// A non-HTTP path (MCP) pre-stamps at its edge and the builder preserves it; an unstamped non-HTTP path fails safe
/// to <c>remote</c>. (The ASP.NET RemoteIpAddress plumbing itself is out of scope — this drives the builder directly.)
/// </summary>
public sealed class OriginBuilderTests
{
    private static (EntityRequestContextBuilder Builder, IServiceProvider Sp) Make(params string[] internalNetworks)
    {
        var services = new ServiceCollection();
        services.AddOptions<OriginOptions>().Configure(o =>
        {
            foreach (var n in internalNetworks) o.InternalNetworks.Add(n);
        });
        var sp = services.BuildServiceProvider();
        return (new EntityRequestContextBuilder(sp), sp);
    }

    private static HttpContext HttpFrom(string ip, IServiceProvider sp)
    {
        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return ctx;
    }

    private static string? OriginOf(EntityRequestContext ctx) => ctx.User.FindFirst(Origin.ClaimType)?.Value;

    [Fact]
    public void Rest_stamps_internal_for_a_declared_network_ip()
    {
        var (builder, sp) = Make("10.0.0.0/8");
        var ctx = builder.Build(new QueryOptions(), CancellationToken.None, HttpFrom("10.1.2.3", sp), user: null);
        OriginOf(ctx).Should().Be("internal", "the source IP is in a declared internal network");
    }

    [Fact]
    public void Rest_stamps_remote_for_an_outside_ip()
    {
        var (builder, sp) = Make("10.0.0.0/8");
        var ctx = builder.Build(new QueryOptions(), CancellationToken.None, HttpFrom("203.0.113.7", sp), user: null);
        OriginOf(ctx).Should().Be("remote", "an IP outside every declared network is remote — never local");
    }

    [Fact]
    public void Rest_overwrites_a_client_forged_origin_from_the_connection()
    {
        var (builder, sp) = Make("10.0.0.0/8");
        var forged = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(Origin.ClaimType, "local") }, "test"));
        var http = HttpFrom("203.0.113.7", sp);
        http.User = forged;
        var ctx = builder.Build(new QueryOptions(), CancellationToken.None, http, user: null);
        OriginOf(ctx).Should().Be("remote", "the connection is authoritative — a forged 'local' claim is overwritten");
    }

    [Fact]
    public void Mcp_path_preserves_a_pre_stamped_origin()
    {
        var (builder, _) = Make();
        var local = OriginStamp.Apply(new ClaimsPrincipal(new ClaimsIdentity()), OriginTier.Local);
        var ctx = builder.Build(new QueryOptions(), CancellationToken.None, httpContext: null, user: local);
        OriginOf(ctx).Should().Be("local", "an MCP edge pre-stamps; the builder must not clobber it");
    }

    [Fact]
    public void An_unstamped_non_http_path_fails_safe_to_remote()
    {
        var (builder, _) = Make();
        var ctx = builder.Build(new QueryOptions(), CancellationToken.None, httpContext: null,
            user: new ClaimsPrincipal(new ClaimsIdentity()));
        OriginOf(ctx).Should().Be("remote", "a non-HTTP path that didn't pre-stamp defaults to the safe tier");
    }
}
