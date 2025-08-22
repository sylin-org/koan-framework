using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Sora.Web;

// Back-compat shim; prefer Sora.Web.Hosting.SoraWebStartupFilter
internal sealed class SoraWebStartupFilter(IOptions<SoraWebOptions> options, IOptions<WebPipelineOptions> pipeline) : IStartupFilter
{
    private readonly Sora.Web.Hosting.SoraWebStartupFilter _inner = new(options, pipeline);
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => _inner.Configure(next);
}

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSoraWeb(this IApplicationBuilder app)
        => Sora.Web.Hosting.ApplicationBuilderExtensions.UseSoraWeb(app);
}
