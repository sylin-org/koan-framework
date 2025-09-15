using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Infrastructure;
using Koan.Web.Options;

namespace Koan.Web.Extensions;



public static class ServiceCollectionExtensions
{
    // Marker to ensure AddKoanWeb runs once
    private sealed class KoanWebMarker { }

    public static IServiceCollection AddKoanWeb(this IServiceCollection services)
    {
        // Idempotence: if we've already wired Koan.Web, no-op
        if (services.Any(d => d.ServiceType == typeof(KoanWebMarker))) return services;
        services.TryAddSingleton<KoanWebMarker>();

        services.AddKoanOptions<KoanWebOptions>(ConfigurationConstants.Web.Section);
        // Core web bits expected by Koan Web apps
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
                var outputFilterType = Type.GetType("Koan.Web.Transformers.EntityOutputTransformFilter, Koan.Web.Transformers");
                if (outputFilterType is not null)
                {
                    // Add as a global filter; attribute on controller will gate actual execution
                    o.Filters.Add(new TypeFilterAttribute(outputFilterType));
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
        // Add input formatter via DI-aware options configurator (no early ServiceProvider build)
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, OptionalTransformerInputFormatterConfigurator>());
        services.AddKoanOptions<WebPipelineOptions>(ConfigurationConstants.Web.Section + ":Pipeline");

        // Observability is wired in Koan.Core's AddKoanObservability; Koan.Web only ensures core web services are present.
        return services;
    }

    public static IServiceCollection AddKoanWeb(this IServiceCollection services, Action<KoanWebOptions> configure)
    {
        services.AddKoanWeb();
        services.Configure(configure);
        return services;
    }

    // Launch templates and toggles
    public static IServiceCollection AsWebApi(this IServiceCollection services)
    {
        services.AddKoanWeb();
        services.AddProblemDetails();
        services.Configure<KoanWebOptions>(o =>
        {
            o.EnableSecureHeaders = true;
            o.EnableStaticFiles = true;
            o.AutoMapControllers = true;
            o.HealthPath = KoanWebConstants.Routes.ApiHealth;
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

    // Convenience toggle: mark app as proxied to skip security headers from Koan
    public static IServiceCollection AsProxiedApi(this IServiceCollection services)
    {
        services.Configure<KoanWebOptions>(o => o.IsProxiedApi = true);
        return services;
    }

    // Correct spelling; prefer this going forward
    public static IServiceCollection SuppressSecurityHeaders(this IServiceCollection services)
        => services.AsProxiedApi();

}

// Internal DI-aware configurator to register optional transformer input formatter when package is present