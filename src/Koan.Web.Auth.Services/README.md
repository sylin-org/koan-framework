# Koan.Web.Auth.Services

Zero-configuration service-to-service authentication for the Koan Framework using OAuth 2.0 Client Credentials flow.

## Features

- **Zero Configuration**: Works out-of-the-box with package reference
- **Attribute-Driven**: Declarative service definitions and dependencies
- **Auto-Discovery**: Automatic service registration and endpoint resolution
- **OAuth 2.0 Compliant**: Industry-standard client credentials flow
- **Container-Aware**: Smart service discovery for Docker/local development
- **JWT Tokens**: Self-contained authentication with scope-based authorization

## Quick Start

1. **Add Package Reference**
   ```xml
   <ProjectReference Include="Koan.Web.Auth.Services" />
   ```

2. **Declare Your Service**
   ```csharp
   [ApiController]
   [KoanService("my-service", ProvidedScopes = new[] { "data:read", "data:write" })]
   public class MyController : ControllerBase
   {
       private readonly IKoanServiceClient _client;

       public MyController(IKoanServiceClient client) => _client = client;
   }
   ```

3. **Call Other Services**
   ```csharp
   [HttpPost("process")]
   [CallsService("ai-service", RequiredScopes = new[] { "ml:inference" })]
   public async Task<IActionResult> Process([FromBody] ProcessRequest request)
   {
       // Automatic authentication with JWT Bearer token
       var result = await _client.PostAsync<ProcessResult>("ai-service", "/api/process", request);
       return Ok(result);
   }
   ```

That's it! The framework handles:
- JWT token acquisition and caching
- Service endpoint discovery
- Authorization header injection
- Token refresh and error handling

## Configuration (Optional)

```json
{
  "Koan": {
    "Auth": {
      "Services": {
        "ClientId": "my-service",
        "ClientSecret": "production-secret",
        "ServiceEndpoints": {
          "ai-service": "https://ai.example.com",
          "analytics-service": "https://analytics.example.com"
        }
      },
      "TestProvider": {
        "EnableClientCredentials": true,
        "AllowedScopes": ["ml:inference", "analytics:write", "data:read"],
        "RegisteredClients": {
          "my-service": {
            "ClientId": "my-service",
            "ClientSecret": "production-secret",
            "AllowedScopes": ["ml:inference", "analytics:write"]
          }
        }
      }
    }
  }
}
```

## Architecture

```
Browser → Web App (Cookie Auth) → Service A (JWT) → Service B (JWT)
             ↓                        ↓               ↓
      Session Cookie            Bearer Token    Bearer Token
```

- **User Authentication**: Secure HTTP-only cookies for browsers
- **Service Authentication**: JWT Bearer tokens for API calls
- **Progressive Disclosure**: Zero config → minimal config → full control

## Development vs Production

### Development
- Auto-generated client secrets
- Relaxed TLS validation
- Container-aware service discovery
- Verbose logging

### Production
- Explicit configuration required
- Strict TLS validation
- Manual service endpoints
- Minimal logging

## API Reference

### Core Interfaces

#### IServiceAuthenticator
Handles JWT token acquisition and management.

```csharp
public interface IServiceAuthenticator
{
    Task<string> GetServiceTokenAsync(string targetService, string[]? scopes = null, CancellationToken ct = default);
    Task<ServiceTokenInfo> GetServiceTokenInfoAsync(string targetService, string[]? scopes = null, CancellationToken ct = default);
    Task InvalidateTokenAsync(string targetService, CancellationToken ct = default);
}
```

#### IServiceDiscovery
Resolves service endpoints using container-aware discovery.

```csharp
public interface IServiceDiscovery
{
    Task<ServiceEndpoint> ResolveServiceAsync(string serviceId, CancellationToken ct = default);
    Task<ServiceEndpoint[]> DiscoverServicesAsync(CancellationToken ct = default);
    Task RegisterServiceAsync(ServiceRegistration registration, CancellationToken ct = default);
}
```

#### IKoanServiceClient
Authenticated HTTP client with automatic token injection.

```csharp
public interface IKoanServiceClient
{
    Task<T?> GetAsync<T>(string serviceId, string endpoint, CancellationToken ct = default) where T : class;
    Task<T?> PostAsync<T>(string serviceId, string endpoint, object? data = null, CancellationToken ct = default) where T : class;
    Task<HttpResponseMessage> SendAsync(string serviceId, HttpRequestMessage request, CancellationToken ct = default);
}
```

### Attributes

#### [KoanService]
Declares a controller as a service with provided scopes.

```csharp
[KoanService("service-id", ProvidedScopes = new[] { "scope1", "scope2" }, Description = "Service description")]
```

#### [CallsService]
Declares service dependencies with required scopes.

```csharp
[CallsService("target-service", RequiredScopes = new[] { "scope1" }, Optional = false)]
```

### Configuration

#### ServiceAuthOptions
Core configuration for service authentication.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TokenCacheDuration` | `TimeSpan` | 55 minutes | Token cache lifetime |
| `TokenRefreshBuffer` | `TimeSpan` | 5 minutes | Refresh buffer before expiration |
| `EnableTokenCaching` | `bool` | `true` | Enable token caching |
| `EnableAutoDiscovery` | `bool` | `true` (dev) | Auto-discover service endpoints |
| `ClientId` | `string` | Auto-generated | OAuth client ID |
| `ClientSecret` | `string` | Auto-generated (dev) | OAuth client secret |
| `TokenEndpoint` | `string` | `/.testoauth/token` | Token endpoint URL |
| `ValidateServerCertificate` | `bool` | `true` (prod) | TLS certificate validation |
| `ServiceEndpoints` | `Dictionary<string,string>` | Empty | Manual endpoint overrides |

#### ClientCredentialsClient
TestProvider client registration.

| Property | Type | Description |
|----------|------|-------------|
| `ClientId` | `string` | Client identifier |
| `ClientSecret` | `string` | Client secret |
| `AllowedScopes` | `string[]` | Permitted scopes |
| `Description` | `string` | Client description |

### Data Types

#### ServiceTokenInfo
```csharp
public record ServiceTokenInfo(string AccessToken, DateTimeOffset ExpiresAt, string[] GrantedScopes);
```

#### ServiceEndpoint
```csharp
public record ServiceEndpoint(string ServiceId, Uri BaseUrl, string[] SupportedScopes);
```

#### ServiceRegistration
```csharp
public record ServiceRegistration(string ServiceId, Uri BaseUrl, string[] ProvidedScopes);
```

### Exception Types

#### ServiceDiscoveryException
Thrown when service endpoint cannot be resolved.

```csharp
public class ServiceDiscoveryException : Exception
{
    public string ServiceId { get; }
}
```

#### ServiceAuthenticationException
Thrown when token acquisition fails.

```csharp
public class ServiceAuthenticationException : Exception
{
    public string ServiceId { get; }
    public string[] RequestedScopes { get; }
}
```

## Documentation

- **[Technical Documentation](TECHNICAL.md)** - Architecture and implementation details
- **[Usage Samples](SAMPLES.md)** - Comprehensive usage examples
- **[Decision Document](../../docs/decisions/DEC-0053-service-to-service-authentication.md)** - Design decisions and rationale

## Examples

See `samples/S5.Recs/Controllers/ServiceDemoController.cs` for complete examples.