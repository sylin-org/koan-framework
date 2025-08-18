using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sora.Web;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSoraWeb(this IServiceCollection services)
    {
        services.AddOptions<SoraWebOptions>();
    // Core web bits expected by Sora Web apps
    services.AddRouting();
    // Add controllers with NewtonsoftJson for JSON Patch
    services.AddControllers(o =>
    {
        // Don’t implicitly require non-nullable reference types (NRT) during model binding.
        // This allows POST bodies to omit Id and have it generated server-side.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        // If transformers package is referenced, register output filter and input formatter
        try
        {
            var outputFilterType = Type.GetType("Sora.Web.Transformers.EntityOutputTransformFilter, Sora.Web.Transformers");
            if (outputFilterType is not null)
            {
                // Add as a global filter; attribute on controller will gate actual execution
                o.Filters.Add(new Microsoft.AspNetCore.Mvc.TypeFilterAttribute(outputFilterType));
            }
        }
        catch { /* optional package */ }
    })
    .AddNewtonsoftJson();
    // Add input formatter if available
    services.PostConfigure<Microsoft.AspNetCore.Mvc.MvcOptions>(mvcOpts =>
    {
        try
        {
            var formatterType = Type.GetType("Sora.Web.Transformers.EntityInputTransformFormatter, Sora.Web.Transformers");
            if (formatterType is not null)
            {
                var sp = services.BuildServiceProvider();
                var formatter = (Microsoft.AspNetCore.Mvc.Formatters.IInputFormatter?)ActivatorUtilities.CreateInstance(sp, formatterType);
                if (formatter is not null)
                    mvcOpts.InputFormatters.Insert(0, formatter);
            }
        }
        catch { /* optional */ }
    });
    services.AddOptions<WebPipelineOptions>();

    // Observability is wired in Sora.Core's AddSoraObservability; Sora.Web only ensures core web services are present.
        return services;
    }

    public static IServiceCollection AddSoraWeb(this IServiceCollection services, Action<SoraWebOptions> configure)
    {
        services.AddSoraWeb();
        services.Configure(configure);
        return services;
    }

    // Launch templates and toggles
    public static IServiceCollection AsWebApi(this IServiceCollection services)
    {
        services.AddSoraWeb();
        services.AddProblemDetails();
        services.Configure<SoraWebOptions>(o =>
        {
            o.EnableSecureHeaders = true;
            o.EnableStaticFiles = true;
            o.AutoMapControllers = true;
            o.HealthPath = "/api/health";
        });
        services.Configure<WebPipelineOptions>(p =>
        {
            p.UseExceptionHandler = true;
            p.UseRateLimiter = false; // opt-in via WithRateLimit()
        });
        return services;
    }

    public static IServiceCollection WithExceptionHandler(this IServiceCollection services)
    {
        services.AddSoraWeb();
    services.Configure<WebPipelineOptions>(p => p.UseExceptionHandler = true);
        return services;
    }

    public static IServiceCollection WithRateLimit(this IServiceCollection services)
    {
        services.AddSoraWeb();
    services.Configure<WebPipelineOptions>(p => p.UseRateLimiter = true);
        return services;
    }
}
