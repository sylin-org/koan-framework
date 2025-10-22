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
using Koan.Web.Auth.Services.Infrastructure;
using ProvenanceModes = Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;
using BootSettingSource = Koan.Core.Hosting.Bootstrap.BootSettingSource;

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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var modeValue = env.IsDevelopment() ? "Development" : "Production";
        module.AddSetting(
            WebAuthServicesProvenanceItems.Mode,
            ProvenanceModes.FromBootSource(BootSettingSource.Environment, usedDefault: false),
            modeValue,
            usedDefault: false);

        var autoDiscovery = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ServiceAuthOptions.SectionPath}:{nameof(ServiceAuthOptions.EnableAutoDiscovery)}",
            true);
        var tokenCaching = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ServiceAuthOptions.SectionPath}:{nameof(ServiceAuthOptions.EnableTokenCaching)}",
            true);

        module.AddSetting(
            WebAuthServicesProvenanceItems.AutoDiscovery,
            ProvenanceModes.FromConfigurationValue(autoDiscovery),
            autoDiscovery.Value,
            sourceKey: autoDiscovery.ResolvedKey,
            usedDefault: autoDiscovery.UsedDefault);

        module.AddSetting(
            WebAuthServicesProvenanceItems.TokenCaching,
            ProvenanceModes.FromConfigurationValue(tokenCaching),
            tokenCaching.Value,
            sourceKey: tokenCaching.ResolvedKey,
            usedDefault: tokenCaching.UsedDefault);

        var discoveredServices = DiscoverServices();
        if (discoveredServices.Length > 0)
        {
            module.AddSetting(
                WebAuthServicesProvenanceItems.ServicesDiscovered,
                ProvenancePublicationMode.Discovery,
                discoveredServices.Length,
                usedDefault: true);

            foreach (var service in discoveredServices)
            {
                module.AddSetting(
                    WebAuthServicesProvenanceItems.ServiceDetail(service.ServiceId),
                    ProvenancePublicationMode.Discovery,
                    $"Scopes: {string.Join(", ", service.ProvidedScopes)} | Dependencies: {service.Dependencies.Length}",
                    usedDefault: true);
            }
        }
        else
        {
            module.AddSetting(
                WebAuthServicesProvenanceItems.ServicesDiscovered,
                ProvenancePublicationMode.Discovery,
                0,
                usedDefault: true);
        }
    }
}

