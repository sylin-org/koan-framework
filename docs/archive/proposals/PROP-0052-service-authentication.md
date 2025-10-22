# Proposal: Koan.Web.Auth.Services - Zero-Configuration Service Authentication

**Document ID:** PROP-0052
**Date:** 2025-01-16
**Status:** Draft
**Reviewers:** TBD

## Executive Summary

This proposal introduces **Koan.Web.Auth.Services**, a new module that provides zero-configuration service-to-service authentication following OAuth 2.0 Client Credentials flow. The module extends Koan's existing authentication infrastructure to support distributed microservices architectures while maintaining the framework's core philosophy of "no-config with sane defaults and minimal scaffolding."

## Problem Statement

### Current Limitations

1. **No Service-to-Service Authentication**: Koan.Web.Auth currently only supports browser-based user authentication via cookies
2. **JWT Token Consumption**: JWT tokens from TestProvider are consumed server-side and not propagated to downstream services
3. **Manual Service Configuration**: Developers must manually configure HTTP clients, authentication, and service discovery
4. **Security Gaps**: No standardized way to secure API-to-API communication in distributed Koan applications

### Business Impact

- **Security Risk**: Unprotected service-to-service communication
- **Development Friction**: Complex manual setup for microservices
- **Scalability Issues**: No standardized service authentication patterns
- **Compliance Concerns**: Lack of proper audit trails for service calls

## Solution Overview

### Design Goals

1. **Zero Configuration**: Works out-of-the-box with package reference
2. **Attribute-Driven**: Uses declarative attributes for service definitions
3. **Auto-Discovery**: Automatically discovers service dependencies
4. **Progressive Configuration**: Override defaults only when needed
5. **Production Ready**: Secure defaults with proper secret management

### High-Level Architecture

```
Browser → Koan App (Cookie Auth) → Service A (JWT) → Service B (JWT)
                ↓                      ↓               ↓
         Session Cookie          Bearer Token    Bearer Token
```

## Detailed Design

### 1. Module Structure

```
Koan.Web.Auth.Services/
├── Initialization/
│   └── KoanAutoRegistrar.cs        # Auto-registration and discovery
├── Authentication/
│   ├── IServiceAuthenticator.cs     # Token acquisition interface
│   ├── ServiceAuthenticator.cs      # Client credentials implementation
│   └── ServiceAuthenticationHandler.cs # HTTP message handler
├── Discovery/
│   ├── IServiceDiscovery.cs         # Service URL resolution
│   ├── KoanServiceDiscovery.cs      # Container-aware discovery
│   └── ServiceMetadata.cs           # Service registration data
├── Http/
│   ├── IKoanServiceClient.cs        # Authenticated HTTP client interface
│   ├── KoanServiceClient.cs         # Implementation with auth
│   └── ServiceClientFactory.cs     # Factory for typed clients
├── Attributes/
│   ├── KoanServiceAttribute.cs      # Mark controllers as services
│   └── CallsServiceAttribute.cs     # Declare service dependencies
├── Options/
│   └── ServiceAuthOptions.cs       # Configuration options
└── Extensions/
    └── ServiceCollectionExtensions.cs # Extension methods
```

### 2. Core Interfaces

#### 2.1 Service Authentication

```csharp
public interface IServiceAuthenticator
{
    Task<string> GetServiceTokenAsync(string targetService, string[] scopes = null, CancellationToken ct = default);
    Task<ServiceTokenInfo> GetServiceTokenInfoAsync(string targetService, string[] scopes = null, CancellationToken ct = default);
    Task InvalidateTokenAsync(string targetService, CancellationToken ct = default);
}

public record ServiceTokenInfo(string AccessToken, DateTimeOffset ExpiresAt, string[] GrantedScopes);
```

#### 2.2 Service Discovery

```csharp
public interface IServiceDiscovery
{
    Task<ServiceEndpoint> ResolveServiceAsync(string serviceId, CancellationToken ct = default);
    Task<ServiceEndpoint[]> DiscoverServicesAsync(CancellationToken ct = default);
    Task RegisterServiceAsync(ServiceRegistration registration, CancellationToken ct = default);
}

public record ServiceEndpoint(string ServiceId, Uri BaseUrl, string[] SupportedScopes);
public record ServiceRegistration(string ServiceId, Uri BaseUrl, string[] ProvidedScopes);
```

#### 2.3 Authenticated HTTP Client

```csharp
public interface IKoanServiceClient
{
    Task<T> GetAsync<T>(string serviceId, string endpoint, CancellationToken ct = default);
    Task<T> PostAsync<T>(string serviceId, string endpoint, object data, CancellationToken ct = default);
    Task<HttpResponseMessage> SendAsync(string serviceId, HttpRequestMessage request, CancellationToken ct = default);
}

public interface IKoanServiceClient<TService> : IKoanServiceClient where TService : class
{
    // Typed client for specific service
}
```

### 3. Attribute System

#### 3.1 Service Declaration

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class KoanServiceAttribute : Attribute
{
    public string ServiceId { get; }
    public string[] ProvidedScopes { get; init; } = Array.Empty<string>();
    public string Description { get; init; } = string.Empty;

    public KoanServiceAttribute(string serviceId) => ServiceId = serviceId;
}

// Usage
[KoanService("s5-recs-backend", ProvidedScopes = new[] { "recommendations:read", "library:write" })]
[ApiController]
public class RecsController : ControllerBase { }
```

#### 3.2 Service Dependency Declaration

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CallsServiceAttribute : Attribute
{
    public string ServiceId { get; }
    public string[] RequiredScopes { get; init; } = Array.Empty<string>();
    public bool Optional { get; init; } = false;

    public CallsServiceAttribute(string serviceId) => ServiceId = serviceId;
}

// Usage
[CallsService("ai-service", RequiredScopes = new[] { "recommendations:read" })]
[CallsService("analytics-service", RequiredScopes = new[] { "analytics:write" }, Optional = true)]
public async Task<IActionResult> ProcessRecommendations() { }
```

### 4. Configuration Options

```csharp
public sealed class ServiceAuthOptions
{
    public const string SectionPath = "Koan:Auth:Services";

    // Token Management
    public TimeSpan TokenCacheDuration { get; init; } = TimeSpan.FromMinutes(55);
    public TimeSpan TokenRefreshBuffer { get; init; } = TimeSpan.FromMinutes(5);
    public bool EnableTokenCaching { get; init; } = true;

    // Service Discovery
    public bool EnableAutoDiscovery { get; init; } = true; // Dev only
    public ServiceDiscoveryMode DiscoveryMode { get; init; } = ServiceDiscoveryMode.Auto;
    public Dictionary<string, string> ServiceEndpoints { get; init; } = new();

    // Authentication
    public string TokenEndpoint { get; init; } = "/.testoauth/token";
    public string ClientId { get; init; } = string.Empty; // Auto-generated if empty
    public string ClientSecret { get; init; } = string.Empty; // Auto-generated in dev
    public string[] DefaultScopes { get; init; } = new[] { "koan:service" };

    // Security
    public bool ValidateServerCertificate { get; init; } = true; // False in dev
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRetryAttempts { get; init; } = 3;
}

public enum ServiceDiscoveryMode
{
    Auto,           // Container-aware resolution
    Manual,         // Use ServiceEndpoints dictionary
    Registry        // Use service registry (future)
}
```

### 5. Auto-Registration Implementation

#### 5.1 Main Auto-Registrar

```csharp
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth.Services";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IServiceAuthenticator, ServiceAuthenticator>();
        services.AddSingleton<IServiceDiscovery, KoanServiceDiscovery>();
        services.AddSingleton<IKoanServiceClient, KoanServiceClient>();
        services.AddTransient<ServiceAuthenticationHandler>();

        // Options with intelligent defaults
        services.AddKoanOptions<ServiceAuthOptions>(ServiceAuthOptions.SectionPath, ConfigureDefaults);

        // Auto-discover services in current assembly
        var serviceMetadata = DiscoverServices();
        RegisterDiscoveredServices(services, serviceMetadata);

        // Configure HTTP clients with authentication
        ConfigureHttpClients(services, serviceMetadata);

        // Register with TestProvider (if available)
        RegisterWithTestProvider(services, serviceMetadata);
    }

    private void ConfigureDefaults(ServiceAuthOptions options, IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            options.EnableAutoDiscovery = true;
            options.ValidateServerCertificate = false;
            options.ClientId = GenerateDevClientId();
            options.ClientSecret = GenerateDevClientSecret(options.ClientId);
        }
        else
        {
            options.EnableAutoDiscovery = false;
            options.ValidateServerCertificate = true;
            // Require explicit configuration in production
        }
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
                .Distinct()
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
                report.AddDetail($"  └─ {service.ServiceId}",
                    $"Scopes: {string.Join(", ", service.ProvidedScopes)} | " +
                    $"Dependencies: {service.Dependencies.Length}");
            }
        }
    }
}
```

#### 5.2 Service Discovery Implementation

```csharp
public sealed class KoanServiceDiscovery : IServiceDiscovery
{
    private readonly ServiceAuthOptions _options;
    private readonly ILogger<KoanServiceDiscovery> _logger;

    public async Task<ServiceEndpoint> ResolveServiceAsync(string serviceId, CancellationToken ct = default)
    {
        // 1. Check manual configuration first
        if (_options.ServiceEndpoints.TryGetValue(serviceId, out var manualUrl))
        {
            return new ServiceEndpoint(serviceId, new Uri(manualUrl), Array.Empty<string>());
        }

        // 2. Try environment variables
        var envUrl = Environment.GetEnvironmentVariable($"KOAN_SERVICE_{serviceId.ToUpper()}_URL");
        if (!string.IsNullOrEmpty(envUrl))
        {
            return new ServiceEndpoint(serviceId, new Uri(envUrl), Array.Empty<string>());
        }

        // 3. Container-aware resolution (following Koan patterns)
        var candidates = new[]
        {
            $"http://{serviceId}:8080",                           // Docker Compose service name
            $"http://host.docker.internal:{GetPortForService(serviceId)}", // Docker to host
            $"http://localhost:{GetPortForService(serviceId)}"    // Local development
        };

        foreach (var candidate in candidates)
        {
            if (await IsServiceReachable(candidate, ct))
            {
                return new ServiceEndpoint(serviceId, new Uri(candidate), Array.Empty<string>());
            }
        }

        throw new ServiceDiscoveryException($"Unable to resolve endpoint for service: {serviceId}");
    }

    private static int GetPortForService(string serviceId)
    {
        // Deterministic port assignment for development
        var hash = serviceId.GetHashCode();
        return 8000 + (Math.Abs(hash) % 1000);
    }
}
```

### 6. Enhanced TestProvider Integration

#### 6.1 Client Credentials Support

```csharp
// Enhanced TokenController in TestProvider
public sealed class TokenController : ControllerBase
{
    [HttpPost("token")]
    public IActionResult Token([FromForm] TokenRequest req)
    {
        // Handle both authorization_code and client_credentials flows
        return req.grant_type switch
        {
            "authorization_code" => HandleAuthorizationCode(req),
            "client_credentials" => HandleClientCredentials(req),
            _ => BadRequest(new { error = "unsupported_grant_type" })
        };
    }

    private IActionResult HandleClientCredentials(TokenRequest req)
    {
        var options = _options.Value;
        if (!options.EnableClientCredentials)
            return BadRequest(new { error = "unsupported_grant_type" });

        // Validate client credentials
        if (!ValidateClientCredentials(req.client_id, req.client_secret))
            return Unauthorized(new { error = "invalid_client" });

        var client = options.RegisteredClients[req.client_id];
        var requestedScopes = ParseScopes(req.scope);
        var grantedScopes = requestedScopes.Intersect(client.AllowedScopes).ToArray();

        // Create service token with appropriate claims
        var serviceProfile = new UserProfile(req.client_id, $"{req.client_id}@service", null);
        var claimEnv = new DevTokenStore.ClaimEnvelope();

        foreach (var scope in grantedScopes)
            claimEnv.Permissions.Add(scope);

        claimEnv.Claims["client_id"] = new List<string> { req.client_id };
        claimEnv.Claims["token_type"] = new List<string> { "service" };

        var token = _store.IssueToken(serviceProfile, TimeSpan.FromHours(1), claimEnv);

        return Ok(new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = 3600,
            scope = string.Join(' ', grantedScopes)
        });
    }
}
```

#### 6.2 Auto-Registration of Service Clients

```csharp
// Auto-register discovered services with TestProvider
public sealed class ServiceClientAutoRegistrar : IKoanAutoRegistrar
{
    public void Initialize(IServiceCollection services)
    {
        // Register service clients discovered by Koan.Web.Auth.Services
        services.Configure<TestProviderOptions>(options =>
        {
            var discoveredServices = GetDiscoveredServices();

            foreach (var service in discoveredServices)
            {
                if (!options.RegisteredClients.ContainsKey(service.ServiceId))
                {
                    options.RegisteredClients[service.ServiceId] = new ClientCredentialsClient
                    {
                        ClientId = service.ServiceId,
                        ClientSecret = GenerateServiceSecret(service.ServiceId),
                        AllowedScopes = service.ProvidedScopes,
                        Description = $"Auto-registered service: {service.ServiceId}"
                    };
                }
            }
        });
    }
}
```

### 7. Developer Experience

#### 7.1 Minimal Setup

```csharp
// Program.cs - Zero configuration required
var builder = WebApplication.CreateBuilder(args);

// Just adding the package reference auto-enables service authentication
builder.Services.AddKoan();

var app = builder.Build();
app.Run();
```

#### 7.2 Service Declaration

```csharp
// Controllers/RecsController.cs
[ApiController]
[KoanService("s5-recs-backend")]  // Declares this as a service
[Route("api/[controller]")]
public class RecsController : ControllerBase
{
    private readonly IKoanServiceClient _serviceClient;

    public RecsController(IKoanServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    [HttpPost("query")]
    [CallsService("ai-service", RequiredScopes = new[] { "recommendations:read" })]
    public async Task<IActionResult> Query([FromBody] RecsQuery query)
    {
        // Authentication handled automatically
        var recommendations = await _serviceClient.PostAsync<RecommendationResult>(
            "ai-service", "/api/recommendations", query);

        return Ok(recommendations);
    }

    [HttpPost("analyze")]
    [CallsService("analytics-service", RequiredScopes = new[] { "analytics:write" })]
    [CallsService("ml-service", RequiredScopes = new[] { "ml:inference" }, Optional = true)]
    public async Task<IActionResult> Analyze([FromBody] AnalysisRequest request)
    {
        // Multiple service calls with different scopes
        var analyticsTask = _serviceClient.PostAsync("analytics-service", "/api/events", request);

        // Optional service - gracefully handled if unavailable
        var mlTask = _serviceClient.PostAsync("ml-service", "/api/predict", request);

        await Task.WhenAll(analyticsTask, mlTask);
        return Ok();
    }
}
```

#### 7.3 Configuration Override (Optional)

```json
// appsettings.json - Only needed to override defaults
{
  "Koan": {
    "Auth": {
      "Services": {
        "ClientId": "custom-client-id",
        "TokenCacheDuration": "01:00:00",
        "ServiceEndpoints": {
          "ai-service": "http://ai.example.com:8080",
          "analytics-service": "http://analytics.example.com:8080"
        }
      }
    }
  }
}
```

### 8. Security Considerations

#### 8.1 Development vs Production

- **Development**: Auto-generated secrets, relaxed TLS validation, verbose logging
- **Production**: Requires explicit configuration, strict validation, minimal logging

#### 8.2 Token Security

- Tokens cached in memory only (never persisted)
- Automatic token refresh before expiration
- Tokens scoped to minimum required permissions
- Token invalidation on authentication errors

#### 8.3 Network Security

- TLS validation enabled in production
- Configurable timeouts and retry policies
- Circuit breaker pattern for failing services

### 9. Error Handling

#### 9.1 Service Discovery Failures

```csharp
public class ServiceDiscoveryException : Exception
{
    public string ServiceId { get; }
    public ServiceDiscoveryException(string serviceId, string message) : base(message)
        => ServiceId = serviceId;
}
```

#### 9.2 Authentication Failures

```csharp
public class ServiceAuthenticationException : Exception
{
    public string ServiceId { get; }
    public string[] RequestedScopes { get; }
    public ServiceAuthenticationException(string serviceId, string[] scopes, string message)
        : base(message)
    {
        ServiceId = serviceId;
        RequestedScopes = scopes;
    }
}
```

#### 9.3 Graceful Degradation

- Optional service calls continue if service is unavailable
- Fallback to cached responses where appropriate
- Circuit breaker prevents cascade failures

### 10. Testing Support

#### 10.1 Test Doubles

```csharp
public class TestKoanServiceClient : IKoanServiceClient
{
    private readonly Dictionary<string, object> _mockResponses = new();

    public TestKoanServiceClient WithMockResponse<T>(string serviceId, string endpoint, T response)
    {
        _mockResponses[$"{serviceId}:{endpoint}"] = response;
        return this;
    }

    public async Task<T> GetAsync<T>(string serviceId, string endpoint, CancellationToken ct = default)
    {
        var key = $"{serviceId}:{endpoint}";
        return _mockResponses.ContainsKey(key) ? (T)_mockResponses[key] : default(T);
    }
}
```

#### 10.2 Integration Testing

```csharp
public class ServiceAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Should_Authenticate_Service_Calls()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/recs/query", new RecsQuery());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify downstream service was called with correct Bearer token
    }
}
```

## Implementation Plan

### Phase 1: Core Infrastructure (Sprint 1-2)

- [ ] Create `Koan.Web.Auth.Services` project structure
- [ ] Implement core interfaces (`IServiceAuthenticator`, `IServiceDiscovery`, `IKoanServiceClient`)
- [ ] Create attribute system (`KoanServiceAttribute`, `CallsServiceAttribute`)
- [ ] Implement basic auto-registrar with service discovery

### Phase 2: TestProvider Integration (Sprint 3)

- [ ] Extend TestProvider to support client credentials flow
- [ ] Implement service client auto-registration
- [ ] Add configuration options and validation
- [ ] Create development-mode defaults

### Phase 3: HTTP Client Integration (Sprint 4)

- [ ] Implement authenticated HTTP client with token injection
- [ ] Add token caching and refresh logic
- [ ] Implement retry policies and circuit breaker
- [ ] Add comprehensive error handling

### Phase 4: Developer Experience (Sprint 5)

- [ ] Create NuGet package with proper dependencies
- [ ] Add Swagger/OpenAPI integration
- [ ] Implement boot report integration
- [ ] Create comprehensive documentation and samples

### Phase 5: Testing and Hardening (Sprint 6)

- [ ] Add comprehensive unit tests
- [ ] Create integration test suite
- [ ] Performance testing and optimization
- [ ] Security audit and hardening

## Success Metrics

### Developer Experience

- **Setup Time**: From package reference to working service auth < 5 minutes
- **Configuration Lines**: 0 lines for basic scenarios, < 10 lines for advanced
- **Learning Curve**: Developers familiar with Koan can use service auth immediately

### Performance

- **Token Acquisition**: < 100ms for cached tokens, < 500ms for new tokens
- **Service Call Overhead**: < 50ms additional latency for authentication
- **Memory Usage**: < 10MB additional memory for service auth components

### Security

- **Zero Critical Vulnerabilities**: No critical security issues in security audit
- **Token Security**: Tokens never logged, persisted, or exposed
- **Scope Enforcement**: All service calls properly scoped and validated

## Risks and Mitigation

### Risk 1: Complexity

**Risk**: Adding too much complexity to maintain Koan's simplicity
**Mitigation**: Extensive testing with real developers, focus on convention over configuration

### Risk 2: Security

**Risk**: Insecure defaults or implementation flaws
**Mitigation**: Security audit, secure defaults, comprehensive threat modeling

### Risk 3: Performance

**Risk**: Authentication overhead impacts application performance
**Mitigation**: Aggressive caching, connection pooling, performance testing

### Risk 4: Compatibility

**Risk**: Breaking changes to existing Koan applications
**Mitigation**: Additive changes only, comprehensive backward compatibility testing

## Alternative Approaches Considered

### Alternative 1: Manual Configuration

**Pros**: Full control, explicit configuration
**Cons**: High setup complexity, not aligned with Koan philosophy

### Alternative 2: External Service Mesh

**Pros**: Industry standard, mature solutions
**Cons**: Complex deployment, not integrated with Koan ecosystem

### Alternative 3: JWT Propagation in Cookies

**Pros**: Simple implementation
**Cons**: Security risks, doesn't work for service-to-service calls

## Conclusion

The proposed `Koan.Web.Auth.Services` module addresses a critical gap in Koan's authentication story while maintaining the framework's core philosophy of zero-configuration with sane defaults. By leveraging auto-registration, attribute-driven configuration, and intelligent service discovery, developers can secure their distributed applications with minimal friction.

The implementation follows established Koan patterns and integrates seamlessly with existing infrastructure, making it a natural extension of the framework's capabilities.

---

**Next Steps:**

1. Review and feedback from Koan maintainers
2. Prototype implementation for validation
3. Community feedback and iteration
4. Full implementation following the proposed plan
