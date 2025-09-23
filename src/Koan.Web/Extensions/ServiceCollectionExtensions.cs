using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Web.Endpoints;
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
        services.AddOptions<EntityEndpointOptions>();
        services.TryAddScoped<EntityRequestContextBuilder>();

        // Core web bits expected by Koan Web apps
        services.AddRouting();
        // Add controllers with NewtonsoftJson for JSON Patch
        services.AddControllers(o =>
        {
            o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            try
            {
                var outputFilterType = Type.GetType("Koan.Web.Transformers.EntityOutputTransformFilter, Koan.Web.Transformers");
                if (outputFilterType is not null)
                {
                    o.Filters.Add(new TypeFilterAttribute(outputFilterType));
                }
            }
            catch { /* optional package */ }
        })
        .AddNewtonsoftJson(j =>
        {
            j.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            j.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            j.SerializerSettings.Converters.Add(new StringEnumConverter());
            j.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        });

        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, OptionalTransformerInputFormatterConfigurator>());
        services.AddKoanOptions<WebPipelineOptions>(ConfigurationConstants.Web.Section + ":Pipeline");
        services.TryAddSingleton<IEntityEndpointDescriptorProvider, DefaultEntityEndpointDescriptorProvider>();
        services.TryAddScoped(typeof(IEntityHookPipeline<>), typeof(DefaultEntityHookPipeline<>));
        services.TryAddScoped(typeof(IEntityEndpointService<,>), typeof(EntityEndpointService<,>));

        return services;
    }

    public static IServiceCollection AddKoanWeb(this IServiceCollection services, Action<KoanWebOptions> configure)
    {
        services.AddKoanWeb();
        services.Configure(configure);
        return services;
    }

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
            p.UseRateLimiter = false;
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

    public static IServiceCollection AsProxiedApi(this IServiceCollection services)
    {
        services.Configure<KoanWebOptions>(o => o.IsProxiedApi = true);
        return services;
    }

    public static IServiceCollection SuppressSecurityHeaders(this IServiceCollection services)
        => services.AsProxiedApi();
}
