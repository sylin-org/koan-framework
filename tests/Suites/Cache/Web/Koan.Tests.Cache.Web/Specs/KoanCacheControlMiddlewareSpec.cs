using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core;
using Koan.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Cache.Web.Specs;

public sealed class KoanCacheControlMiddlewareSpec
{
    /// <summary>Helper: run the middleware against a context with a given header set, and report what the inner pipeline observed.</summary>
    private static async Task<(CacheBehavior? observed, CacheBehavior? afterRequest)> Run(
        string? cacheControl = null,
        string? koanCacheHeader = null)
    {
        var ctx = new DefaultHttpContext();
        if (cacheControl is not null) ctx.Request.Headers["Cache-Control"] = cacheControl;
        if (koanCacheHeader is not null) ctx.Request.Headers["X-Koan-Cache"] = koanCacheHeader;

        CacheBehavior? observed = null;
        RequestDelegate next = _ =>
        {
            observed = EntityContext.Current?.CacheBehavior;
            return Task.CompletedTask;
        };

        var mw = new KoanCacheControlMiddleware(next, NullLogger<KoanCacheControlMiddleware>.Instance);
        await mw.InvokeAsync(ctx);

        return (observed, EntityContext.Current?.CacheBehavior);
    }

    [Fact]
    public async Task No_cache_directive_pushes_Refresh()
    {
        var (observed, after) = await Run(cacheControl: "no-cache");

        observed.Should().Be(CacheBehavior.Refresh);
        after.Should().BeNull("scope must be disposed after the request");
    }

    [Fact]
    public async Task No_store_directive_pushes_Bypass()
    {
        var (observed, after) = await Run(cacheControl: "no-store");

        observed.Should().Be(CacheBehavior.Bypass);
        after.Should().BeNull();
    }

    [Fact]
    public async Task Both_no_cache_and_no_store_picks_no_cache_first_per_directive_order()
    {
        // The implementation checks no-cache first; both present → Refresh.
        var (observed, _) = await Run(cacheControl: "no-cache, no-store");
        observed.Should().Be(CacheBehavior.Refresh);
    }

    [Fact]
    public async Task Other_cache_control_directives_pass_through_with_no_override()
    {
        var (observed, _) = await Run(cacheControl: "max-age=60");
        observed.Should().BeNull("max-age does not signal a cache-skip intent");
    }

    [Theory]
    [InlineData("refresh", CacheBehavior.Refresh)]
    [InlineData("REFRESH", CacheBehavior.Refresh)]
    [InlineData("  refresh  ", CacheBehavior.Refresh)]
    [InlineData("bypass", CacheBehavior.Bypass)]
    [InlineData("no-cache", CacheBehavior.Bypass)]
    [InlineData("no-store", CacheBehavior.Bypass)]
    [InlineData("readonly", CacheBehavior.ReadOnly)]
    [InlineData("read-only", CacheBehavior.ReadOnly)]
    [InlineData("default", CacheBehavior.Default)]
    public async Task X_Koan_Cache_header_maps_explicit_modes(string headerValue, CacheBehavior expected)
    {
        var (observed, _) = await Run(koanCacheHeader: headerValue);
        observed.Should().Be(expected);
    }

    [Fact]
    public async Task Unknown_X_Koan_Cache_value_is_ignored()
    {
        var (observed, _) = await Run(koanCacheHeader: "definitely-not-a-mode");
        observed.Should().BeNull();
    }

    [Fact]
    public async Task X_Koan_Cache_overrides_Cache_Control()
    {
        var (observed, _) = await Run(cacheControl: "no-cache", koanCacheHeader: "bypass");
        observed.Should().Be(CacheBehavior.Bypass, "the Koan-specific header is more explicit and must win");
    }

    [Fact]
    public async Task No_headers_means_no_scope()
    {
        var (observed, after) = await Run();
        observed.Should().BeNull();
        after.Should().BeNull();
    }

    [Fact]
    public async Task Scope_disposed_even_when_next_throws()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Cache-Control"] = "no-cache";

        RequestDelegate next = _ => throw new InvalidOperationException("boom");

        var mw = new KoanCacheControlMiddleware(next, NullLogger<KoanCacheControlMiddleware>.Instance);
        var act = () => mw.InvokeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>();

        EntityContext.Current?.CacheBehavior.Should().BeNull("using-statement disposes even under exception");
    }
}
