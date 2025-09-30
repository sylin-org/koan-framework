using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Web.Auth.Services.Attributes;
using Koan.Web.Auth.Services.Authentication;
using Koan.Web.Auth.Services.Discovery;
using Koan.Web.Auth.Services.Http;
using Koan.Web.Auth.Services.Options;
using Koan.Web.Auth.Connector.Test.Options;

namespace Koan.Web.Auth.Services.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Services";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register memory cache dependency
        services.AddMemoryCache();

        // Core services
        services.AddSingleton<IServiceAuthenticator, ServiceAuthenticator>();
        services.AddSingleton<IServiceDiscovery, KoanServiceDiscovery>();
        services.AddTransient<ServiceAuthenticationHandler>();

        // Register KoanServiceClient with a clean HTTP client (no authentication handler)
        // KoanServiceClient handles authentication explicitly, so it doesn't need the handler
        services.AddHttpClient<IKoanServiceClient, KoanServiceClient>("KoanServiceClientInternal");

        // Options with intelligent defaults
        services.AddKoanOptions<ServiceAuthOptions>(ServiceAuthOptions.SectionPath);

        // Auto-discover services in current assembly
        var serviceMetadata = DiscoverServices();
        RegisterDiscoveredServices(services, serviceMetadata);

        // Configure HTTP clients with authentication
        ConfigureHttpClients(services, serviceMetadata);

        // Auto-configure TestProvider and ServiceAuth with discovered services
        // Use deferred configuration to avoid building service provider during registration
        services.PostConfigure<TestProviderOptions>(options =>
        {
            var serviceProvider = services.BuildServiceProvider();
            var config = serviceProvider.GetService<IConfiguration>();
            var env = serviceProvider.GetService<IHostEnvironment>();

            if (config != null && env != null)
            {
                ServiceAutoConfiguration.ConfigureTestProviderForDiscoveredServices(
                    options, serviceMetadata, config, env);
            }
        });

        services.PostConfigure<ServiceAuthOptions>(options =>
        {
            var serviceProvider = services.BuildServiceProvider();
            var config = serviceProvider.GetService<IConfiguration>();
            var env = serviceProvider.GetService<IHostEnvironment>();

            if (config != null && env != null)
            {
                ServiceAutoConfiguration.ConfigureServiceAuthForDiscoveredServices(
                    options, serviceMetadata, config, env);
            }
        });
    }

    private ServiceMetadata[] DiscoverServices()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        var services = new List<ServiceMetadata>();

        foreach (var type in assembly.GetTypes())
        {
            var serviceAttr = type.GetCustomAttribute<KoanServiceAttribute>();
            if (serviceAttr == null) continue;

            var dependencies = type.GetCustomAttributes<CallsServiceAttribute>()
                .Concat(GetMethodDependencies(type))
                .DistinctBy(d => d.ServiceId)
                .ToArray();

            services.Add(new ServiceMetadata(
                ServiceId: serviceAttr.ServiceId,
                ProvidedScopes: serviceAttr.ProvidedScopes,
                Dependencies: dependencies.Select(d => new ServiceDependency(
                    ServiceId: d.ServiceId,
                    RequiredScopes: d.RequiredScopes,
                    Optional: d.Optional
                )).ToArray(),
                ControllerType: type
            ));
        }

        return services.ToArray();
    }

    private IEnumerable<CallsServiceAttribute> GetMethodDependencies(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(method => method.GetCustomAttributes<CallsServiceAttribute>());
    }

    private void RegisterDiscoveredServices(IServiceCollection services, ServiceMetadata[] serviceMetadata)
    {
        // Register service metadata for later use
        services.AddSingleton(provider => serviceMetadata);

        // Register typed service clients
        foreach (var service in serviceMetadata)
        {
            foreach (var dependency in service.Dependencies)
            {
                RegisterServiceClient(services, dependency);
            }
        }
    }

    private void RegisterServiceClient(IServiceCollection services, ServiceDependency dependency)
    {
        // Register a factory that can create clients for specific services
        // This will be used by the IKoanServiceClient implementation
    }

    private void ConfigureHttpClients(IServiceCollection services, ServiceMetadata[] serviceMetadata)
    {
        // Register a named HTTP client for internal token requests (without auth handler to avoid circular dependency)
        services.AddHttpClient("KoanAuthInternal");

        // Default HTTP clients have NO authentication - external APIs should not receive Koan tokens
        // This prevents sending TestProvider JWT tokens to AniList, Weaviate, and other external services
        //
        // For legitimate Koan service-to-service communication:
        // - Use IKoanServiceClient (handles authentication explicitly)
        // - Or use named "KoanServiceClient" client (has authentication handler)
        services.ConfigureHttpClientDefaults(builder =>
        {
            // No authentication by default - external services manage their own auth
        });

        // Optional: Register named client with authentication for components that need it
        services.AddHttpClient("KoanServiceClient")
            .AddHttpMessageHandler<ServiceAuthenticationHandler>();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var options = new ServiceAuthOptions();
        cfg.GetSection(ServiceAuthOptions.SectionPath).Bind(options);

        report.AddSetting("Mode", env.IsDevelopment() ? "Development" : "Production");
        report.AddSetting("Auto Discovery", options.EnableAutoDiscovery.ToString());
        report.AddSetting("Token Caching", options.EnableTokenCaching.ToString());

        var discoveredServices = DiscoverServices();
        if (discoveredServices.Length > 0)
        {
            report.AddSetting("Services Discovered", discoveredServices.Length.ToString());
            foreach (var service in discoveredServices)
            {
                report.AddSetting($"  └─ {service.ServiceId}",
                    $"Scopes: {string.Join(", ", service.ProvidedScopes)} | " +
                    $"Dependencies: {service.Dependencies.Length}");
            }
        }
    }
}
