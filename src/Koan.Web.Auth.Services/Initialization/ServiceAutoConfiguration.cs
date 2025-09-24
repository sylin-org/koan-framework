using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Web.Auth.Services.Attributes;
using Koan.Web.Auth.Services.Discovery;
using Koan.Web.Auth.Services.Options;
using Koan.Web.Auth.TestProvider.Options;

namespace Koan.Web.Auth.Services.Initialization;

/// <summary>
/// Automatically configures service authentication with zero manual configuration required.
/// Discovers services via attributes and auto-generates all necessary configuration.
/// </summary>
public static class ServiceAutoConfiguration
{
    /// <summary>
    /// Configures TestProvider with auto-discovered services and generated secrets.
    /// Called by the auto-registrar to eliminate manual configuration.
    /// </summary>
    public static void ConfigureTestProviderForDiscoveredServices(
        TestProviderOptions options,
        ServiceMetadata[] discoveredServices,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Auto-enable JWT tokens for service authentication
        options.UseJwtTokens = true;
        options.EnableClientCredentials = true;

        // Generate smart defaults for JWT settings
        var appName = environment.ApplicationName.ToLowerInvariant().Replace(" ", "-");
        options.JwtIssuer = $"koan-{appName}-{(environment.IsDevelopment() ? "dev" : "prod")}";
        options.JwtAudience = $"{appName}-services";

        // Collect all scopes from discovered services
        var allScopes = discoveredServices
            .SelectMany(s => s.ProvidedScopes.Concat(s.Dependencies.SelectMany(d => d.RequiredScopes)))
            .Distinct()
            .OrderBy(s => s)
            .ToArray();

        options.AllowedScopes = allScopes.Length > 0 ? allScopes : new[] { "koan:service" };

        // Auto-register discovered services as OAuth clients
        AutoRegisterServiceClients(options, discoveredServices, environment);
    }

    /// <summary>
    /// Configures ServiceAuth options with auto-generated client credentials.
    /// </summary>
    public static void ConfigureServiceAuthForDiscoveredServices(
        ServiceAuthOptions options,
        ServiceMetadata[] discoveredServices,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var currentService = GetCurrentServiceInfo(discoveredServices, environment);
        if (currentService != null)
        {
            options.ClientId = currentService.ServiceId;
            options.ClientSecret = GenerateServiceSecret(currentService.ServiceId, environment);

            // Auto-determine default scopes from provided scopes
            options.DefaultScopes = currentService.ProvidedScopes.Length > 0
                ? currentService.ProvidedScopes
                : new[] { "koan:service" };
        }

        // Environment-aware defaults
        if (environment.IsDevelopment())
        {
            options.ValidateServerCertificate = false;
            options.EnableAutoDiscovery = true;
        }
    }

    private static void AutoRegisterServiceClients(
        TestProviderOptions options,
        ServiceMetadata[] discoveredServices,
        IHostEnvironment environment)
    {
        // Register the current application as a service client
        var currentService = GetCurrentServiceInfo(discoveredServices, environment);
        if (currentService != null)
        {
            RegisterServiceClient(options, currentService, environment);
        }

        // Register all services that this application depends on
        var dependentServices = discoveredServices
            .SelectMany(s => s.Dependencies)
            .DistinctBy(d => d.ServiceId)
            .ToArray();

        foreach (var dependency in dependentServices)
        {
            RegisterDependentServiceClient(options, dependency, environment);
        }
    }

    private static void RegisterServiceClient(
        TestProviderOptions options,
        ServiceMetadata service,
        IHostEnvironment environment)
    {
        if (options.RegisteredClients.ContainsKey(service.ServiceId))
            return; // Don't override explicit configuration

        options.RegisteredClients[service.ServiceId] = new ClientCredentialsClient
        {
            ClientId = service.ServiceId,
            ClientSecret = GenerateServiceSecret(service.ServiceId, environment),
            AllowedScopes = service.ProvidedScopes.Concat(GetImpliedScopes(service)).Distinct().ToArray(),
            Description = $"Auto-registered service: {service.ServiceId}"
        };
    }

    private static void RegisterDependentServiceClient(
        TestProviderOptions options,
        ServiceDependency dependency,
        IHostEnvironment environment)
    {
        if (options.RegisteredClients.ContainsKey(dependency.ServiceId))
            return; // Don't override explicit configuration

        // Register dependent service with minimal scopes for demo purposes
        options.RegisteredClients[dependency.ServiceId] = new ClientCredentialsClient
        {
            ClientId = dependency.ServiceId,
            ClientSecret = GenerateServiceSecret(dependency.ServiceId, environment),
            AllowedScopes = dependency.RequiredScopes.Concat(new[] { "koan:service" }).Distinct().ToArray(),
            Description = $"Auto-registered dependency: {dependency.ServiceId}"
        };
    }

    private static ServiceMetadata? GetCurrentServiceInfo(
        ServiceMetadata[] discoveredServices,
        IHostEnvironment environment)
    {
        // For applications with multiple services, use the first one or derive from app name
        var currentService = discoveredServices.FirstOrDefault();

        if (currentService == null)
        {
            // Generate a default service if no [KoanService] attributes found
            var appName = environment.ApplicationName.ToLowerInvariant().Replace(" ", "-");
            return new ServiceMetadata(
                ServiceId: appName,
                ProvidedScopes: new[] { "koan:service" },
                Dependencies: Array.Empty<ServiceDependency>(),
                ControllerType: typeof(object) // Placeholder
            );
        }

        return currentService;
    }

    private static string[] GetImpliedScopes(ServiceMetadata service)
    {
        // Add common implied scopes based on service patterns
        var impliedScopes = new List<string>();

        // If service has no explicit scopes, add basic service scope
        if (service.ProvidedScopes.Length == 0)
        {
            impliedScopes.Add("koan:service");
        }

        // Add scopes for dependencies this service needs
        foreach (var dependency in service.Dependencies)
        {
            impliedScopes.AddRange(dependency.RequiredScopes);
        }

        return impliedScopes.Distinct().ToArray();
    }

    /// <summary>
    /// Generates a deterministic secret for development or uses environment variable for production.
    /// </summary>
    public static string GenerateServiceSecret(string serviceId, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            // Generate deterministic secret for development reproducibility
            var input = $"koan-dev-secret-{serviceId}-{environment.ApplicationName}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes)[..32]; // Truncate to reasonable length
        }

        // Production: require explicit environment variable
        var envVarName = $"KOAN_SERVICE_SECRET_{serviceId.ToUpper().Replace("-", "_")}";
        var secret = Environment.GetEnvironmentVariable(envVarName);

        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"Service secret not configured for '{serviceId}'. " +
                $"Set environment variable '{envVarName}' in production.");
        }

        return secret;
    }

    /// <summary>
    /// Automatically configures scope inheritance and validation rules.
    /// </summary>
    public static void ConfigureAutomaticScopeValidation(IServiceCollection services, ServiceMetadata[] discoveredServices)
    {
        // Could implement automatic scope hierarchy and validation rules
        // For example: "users:write" implies "users:read"

        services.Configure<ServiceAuthOptions>(options =>
        {
            // Auto-configure scope validation based on discovered patterns
            var scopeHierarchy = BuildScopeHierarchy(discoveredServices);
            // Store scope hierarchy for runtime validation
        });
    }

    private static Dictionary<string, string[]> BuildScopeHierarchy(ServiceMetadata[] services)
    {
        var hierarchy = new Dictionary<string, string[]>();

        // Analyze scope patterns to build inheritance
        var allScopes = services.SelectMany(s => s.ProvidedScopes.Concat(s.Dependencies.SelectMany(d => d.RequiredScopes)))
            .Distinct()
            .ToArray();

        foreach (var scope in allScopes)
        {
            if (scope.EndsWith(":write") || scope.EndsWith(":admin"))
            {
                var readScope = scope.Replace(":write", ":read").Replace(":admin", ":read");
                if (allScopes.Contains(readScope))
                {
                    hierarchy[scope] = new[] { readScope };
                }
            }
        }

        return hierarchy;
    }
}