using Microsoft.AspNetCore.Builder;

namespace Sora.Web.Hosting;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSoraWeb(this IApplicationBuilder app)
    {
        return app;
    }
}