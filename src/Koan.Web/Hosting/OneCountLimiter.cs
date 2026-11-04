using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Hosting;

/// <summary>An application's global limiter counts the request in hand once, and a request <see cref="KoanWebPipeline"/> makes for it
/// passes (ARCH-0144): that request runs inside the outer one's permit, so a concurrency limit would wait on itself. A limit a route names
/// still binds it.</summary>
internal sealed class OneCountLimiter(PartitionedRateLimiter<HttpContext> global) : PartitionedRateLimiter<HttpContext>
{
    private static readonly PartitionedRateLimiter<HttpContext> Free = PartitionedRateLimiter.Create<HttpContext, int>(_ => RateLimitPartition.GetNoLimiter(0));

    private PartitionedRateLimiter<HttpContext> For(HttpContext context) => context.Features.Get<InProcessMark>() is null ? global : Free;

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
