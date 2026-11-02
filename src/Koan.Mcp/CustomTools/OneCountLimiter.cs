using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Koan.Mcp.CustomTools;

/// <summary>An application's global limiter counts a tool call once, at the MCP edge, and the request made for it passes (ARCH-0141): that request runs inside the
/// edge's permit, so a concurrency limit would wait on itself. A limit a route names still binds it.</summary>
internal sealed class OneCountLimiter(PartitionedRateLimiter<HttpContext> global) : PartitionedRateLimiter<HttpContext>
{
    private static readonly PartitionedRateLimiter<HttpContext> Free = PartitionedRateLimiter.Create<HttpContext, int>(_ => RateLimitPartition.GetNoLimiter(0));

    private PartitionedRateLimiter<HttpContext> For(HttpContext context) => context.Features.Get<RouteTool>() is null ? global : Free;

    public override RateLimiterStatistics? GetStatistics(HttpContext resource) => For(resource).GetStatistics(resource);

    protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount) => For(resource).AttemptAcquire(resource, permitCount);

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(HttpContext resource, int permitCount, CancellationToken cancellationToken)
        => For(resource).AcquireAsync(resource, permitCount, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing) global.Dispose();
    }

    protected override ValueTask DisposeAsyncCore() => global.DisposeAsync();
}
