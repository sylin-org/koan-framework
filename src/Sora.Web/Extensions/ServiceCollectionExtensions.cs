using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Sora.Web;



public static class ServiceCollectionExtensions
{
    // Marker to ensure AddSoraWeb runs once
    private sealed class SoraWebMarker { }

    public static IServiceCollection AddSoraWeb(this IServiceCollection services)
    {
        // Idempotence: if we've already wired Sora.Web, no-op
        if (services.Any(d => d.ServiceType == typeof(SoraWebMarker))) return services;
        services.TryAddSingleton<SoraWebMarker>();

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
        .AddNewtonsoftJson(j =>
        {
            // Emit camelCase JSON to match typical JS clients (id, name, ...)
            j.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            j.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;

            j.SerializerSettings.Converters.Add(new StringEnumConverter());
            j.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;

        });
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
            o.HealthPath = Sora.Web.Infrastructure.SoraWebConstants.Routes.ApiHealth;
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
        services.Configure<WebPipelineOptions>(p => p.UseExceptionHandler = true);
        return services;
    }

    public static IServiceCollection WithRateLimit(this IServiceCollection services)
    {
        services.Configure<WebPipelineOptions>(p => p.UseRateLimiter = true);
        return services;
    }

    // Convenience toggle: mark app as proxied to skip security headers from Sora
    public static IServiceCollection AsProxiedApi(this IServiceCollection services)
    {
        services.Configure<SoraWebOptions>(o => o.IsProxiedApi = true);
        return services;
    }

    // Correct spelling; prefer this going forward
    public static IServiceCollection SuppressSecurityHeaders(this IServiceCollection services)
        => services.AsProxiedApi();

    // Back-compat alias (typo). Remove in a future major version.
    [Obsolete("Use SuppressSecurityHeaders() instead.")]
    public static IServiceCollection SupressSecurityHeaders(this IServiceCollection services)
        => services.SuppressSecurityHeaders();
}
