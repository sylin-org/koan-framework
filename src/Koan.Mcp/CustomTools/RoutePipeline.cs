using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Koan.Mcp.CustomTools;

/// <summary>Holds the application's whole request pipeline, every middleware of it, as the host builds it: the first component of the
/// host's builder is outermost, so its <c>next</c> is everything an HTTP request passes through (ARCH-0141).</summary>
internal sealed class RoutePipeline(IServiceProvider services) : IApplicationBuilderFactory
{
    /// <summary>Null when this process serves no web requests.</summary>
    public RequestDelegate? Request { get; private set; }

    public IApplicationBuilder CreateBuilder(IFeatureCollection serverFeatures)
    {
        var builder = new ApplicationBuilderFactory(services).CreateBuilder(serverFeatures);
        builder.Use(next =>
        {
            Request = next;
            return next;
        });
        return builder;
    }
}
