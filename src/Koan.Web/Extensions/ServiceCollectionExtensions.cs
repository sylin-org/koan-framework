using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
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
using System.Reflection;

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
            o.Filters.Add(new TypeFilterAttribute(typeof(Koan.Web.Transformers.EntityOutputTransformFilter)));
            o.Filters.Add(new TypeFilterAttribute(typeof(Serialization.FieldAccessResultFilter)) { Order = int.MaxValue });
            o.Filters.Add(Filters.ChallengeFilter.Instance);
            // WEB-0088: a declared entity write binds a page's form as it binds the same members sent as JSON.
            o.InputFormatters.Add(new Serialization.EntityFormInputFormatter());
            o.ModelBinderProviders.Insert(o.ModelBinderProviders.ToList().FindIndex(static provider => provider is Microsoft.AspNetCore.Mvc.ModelBinding.Binders.ArrayModelBinderProvider),
                new Filters.SentEmptyCollectionBinderProvider());
        })
        .AddNewtonsoftJson(j =>
        {
            j.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            j.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            j.SerializerSettings.Converters.Add(new StringEnumConverter());
            j.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        });

        // ARCH-0142: a principal a web context placed survives WebApplication's own authentication around what an application adds in Program.
        services.AddAuthentication();
        if (services.Last(static descriptor => descriptor.ServiceType == typeof(IAuthenticationService)) is { ImplementationType: { } authentication } registered)
        {
            services.Remove(registered);
            services.AddScoped<IAuthenticationService>(provider => new Context.PlacedAuthentication((IAuthenticationService)ActivatorUtilities.CreateInstance(provider, authentication)));
        }

        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, OptionalTransformerInputFormatterConfigurator>());
        // ARCH-0146: every routed action that declares access is judged before binding, and the host can refuse one that declares nothing.
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, Filters.GateFilter.Convention>());
        // WEB-0087: a controller that is a hook of its own requests answers none through its hook methods.
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, Controllers.EntityHooksAreNotActions>());
        // WEB-0091: wherever an action answers GET it answers HEAD.
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<MvcOptions>, Controllers.GetAnswersHead>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, Authorization.RouteAccessStartCheck>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPostConfigureOptions<MvcOptions>, Serialization.FieldAccessMvcOptions>());
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

    /// <summary>
    /// Ensures the specified assembly is registered with MVC's ApplicationPartManager for controller discovery.
    /// This is required for Koan modules with controllers to enable automatic endpoint registration.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="assembly">Assembly containing controllers to register</param>
    /// <returns>IMvcBuilder for chaining</returns>
    public static IMvcBuilder AddKoanControllersFrom(this IServiceCollection services, Assembly assembly)
    {
        var mvc = services.AddControllers();
        if (!mvc.PartManager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == assembly))
        {
            mvc.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
        }
        return mvc;
    }

    /// <summary>
    /// Ensures the assembly containing the specified type is registered with MVC's ApplicationPartManager.
    /// Convenience overload for type-safe assembly registration.
    /// </summary>
    /// <typeparam name="TController">A type from the assembly containing controllers</typeparam>
    /// <param name="services">Service collection</param>
    /// <returns>IMvcBuilder for chaining</returns>
    public static IMvcBuilder AddKoanControllersFrom<TController>(this IServiceCollection services)
        => AddKoanControllersFrom(services, typeof(TController).Assembly);
}
