# CLD_Koan_MCP_INTEGRATION_PLAN

## Executive Summary

This document outlines the comprehensive implementation plan for integrating Model Context Protocol (MCP) capabilities into the Koan Framework. The integration will enable Koan services to both provide and consume MCP tools, creating a standardized interface for AI applications to interact with Koan-powered services while maintaining the framework's core principles of simplicity, explicitness, and developer experience.

**Key Benefits:**
- **Native AI Integration**: Transform any Koan service into an AI-ready MCP provider
- **Service Discovery**: Automatic MCP service discovery within container environments  
- **Framework Consistency**: Leverages existing Koan patterns (messaging, auto-registration, health checks)
- **Opt-in Complexity**: Services choose their level of MCP participation
- **Production Ready**: Built-in security, monitoring, and reliability patterns

**Implementation Effort:** ~3-4 weeks across multiple development sessions
**Framework Impact:** Additive only - no breaking changes to existing Koan services

---

## 1. Current State Analysis

### 1.1 Koan Framework Infrastructure Assessment ✅ **EXCELLENT**

The Koan Framework provides **exceptional infrastructure** for MCP integration with approximately **85% of required capabilities already available**:

#### **Messaging Infrastructure**
- **Location**: `src/Koan.Messaging.Core/`
- **Capabilities**: 
  - `IMessageProxy` with adaptive buffering (startup-safe messaging)
  - `MessagingExtensions` with `.Send()` and `.On<T>()` patterns
  - `MessagingInterceptors` for type-based message transformation
  - `IQueuedMessage` interface for dedicated queue routing (used by Flow)
- **MCP Readiness**: 🟢 **Perfect** - Can implement MCP service announcements via existing messaging

#### **Service Registration Patterns**
- **Location**: `src/Koan.Core/Hosting/Bootstrap/AppBootstrapper.cs`
- **Capabilities**:
  - `IKoanAutoRegistrar` with module metadata and environment description
  - Assembly scanning and dependency walking
  - Best-effort registration with failure resilience
- **MCP Readiness**: 🟢 **Perfect** - MCP services can auto-register via `IKoanAutoRegistrar`

#### **Discovery Mechanisms** 
- **AI Adapters**: `IAiAdapter` with `CanServe()` capability checking
- **Data Adapters**: `IDataProviderConnectionFactory` with provider enumeration
- **Pattern**: Provider registration → capability negotiation → health monitoring
- **MCP Readiness**: 🟢 **Strong** - Can extend for MCP tool discovery

#### **Configuration & Environment**
- **Location**: `src/Koan.Core/KoanEnv.cs`
- **Capabilities**: Container detection, environment classification, configuration precedence
- **MCP Readiness**: 🟢 **Perfect** - Can detect development environments for MCP features

#### **Health Check Infrastructure**
- **Capabilities**: `IHealthContributor`, `HealthRegistry`, provider-specific checks
- **MCP Readiness**: 🟢 **Perfect** - MCP services can integrate health monitoring

### 1.2 Gap Analysis

#### **What Exists** (85% Coverage)
- ✅ Service registration and discovery infrastructure
- ✅ Messaging system with buffering and routing
- ✅ Health check and monitoring framework
- ✅ Environment detection and configuration management
- ✅ Container-aware orchestration patterns
- ✅ Auto-registration and lifecycle management

#### **What Needs Building** (15% Implementation)
- 🔨 MCP protocol interfaces and models
- 🔨 MCP service discovery via messaging
- 🔨 MCP tool registration and execution
- 🔨 MCP resource exposure patterns
- 🔨 HTTP transport for remote MCP servers

---

## 2. Architecture Overview

### 2.1 MCP Integration Architecture

```mermaid
graph TB
    subgraph "Koan MCP Architecture"
        subgraph "MCP Services"
            MS1[Analytics Service<br/>[McpService]]
            MS2[User Service<br/>[McpService]]  
            MS3[Data Service<br/>[McpService]]
        end
        
        subgraph "MCP Registry"
            REG[MCP Discovery Service<br/>[McpOrchestrator]]
            QUEUE[Dedicated MCP Queue<br/>Koan.MCP.ServiceAnnouncement]
        end
        
        subgraph "External AI Applications"
            AI1[Claude Desktop]
            AI2[Custom AI App]
            AI3[Copilot Extension]
        end
        
        MS1 -->|Announces Tools| QUEUE
        MS2 -->|Announces Tools| QUEUE
        MS3 -->|Announces Tools| QUEUE
        
        QUEUE --> REG
        REG -->|Aggregated Tools| AI1
        REG -->|Aggregated Tools| AI2
        REG -->|Aggregated Tools| AI3
        
        AI1 -.->|Direct Tool Calls| MS1
        AI2 -.->|Direct Tool Calls| MS2
        AI3 -.->|Direct Tool Calls| MS3
    end
```

### 2.2 Component Responsibilities

#### **MCP Services** (`[McpService(AutoDiscovery = true)]`)
- Expose business capabilities as MCP tools
- Announce tool availability via messaging
- Handle direct MCP tool execution requests
- Maintain independent operation (lego pieces)

#### **MCP Registry** (`[McpOrchestrator(AutoDiscovery = true)]`)
- Listen for service announcements
- Aggregate available tools and capabilities
- Provide unified MCP interface to AI applications
- Route tool calls to appropriate services

#### **Messaging Infrastructure**
- Route MCP announcements to dedicated queue: `Koan.MCP.ServiceAnnouncement`
- Enable service-to-service discovery within containers
- Buffer messages during startup phases

### 2.3 Discovery Flow

```sequence
participant S as MCP Service
participant M as Messaging Queue
participant R as MCP Registry
participant A as AI Application

S->>M: ServiceAnnouncement{tools, capabilities, endpoint}
M->>R: Route announcement to registry
R->>R: Register service and capabilities
A->>R: GET /mcp (list tools)
R->>A: Aggregated tool list
A->>S: POST /mcp/tools/{tool} (direct call)
S->>A: Tool execution result
```

---

## 3. Implementation Plan

### Phase 1: Core MCP Infrastructure (Week 1)

#### **Goals:**
- Implement core MCP interfaces following .NET MCP SDK patterns
- Create service registration and discovery mechanisms
- Enable basic MCP tool execution

#### **Deliverables:**

##### **3.1.1 Koan.MCP.Core Assembly**

```csharp
// File: src/Koan.MCP.Core/IMcpService.cs
public interface IMcpService
{
    string ServiceId { get; }
    string ServiceName { get; }
    IReadOnlyList<McpToolDescriptor> AvailableTools { get; }
    IReadOnlyList<McpResourceDescriptor> AvailableResources { get; }
    
    Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default);
    Task<McpResourceResult> ReadResourceAsync(string uri, CancellationToken ct = default);
}

// File: src/Koan.MCP.Core/Models/McpServiceAnnouncement.cs  
public class McpServiceAnnouncement : IQueuedMessage
{
    public string QueueName { get; } = "Koan.MCP.ServiceAnnouncement";
    public object Payload { get; }
    
    public string ServiceId { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string[] Capabilities { get; init; } = Array.Empty<string>();
    public Dictionary<string, object> Metadata { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    
    public McpServiceAnnouncement(object payload) => Payload = payload;
}

// File: src/Koan.MCP.Core/Attributes/McpServiceAttribute.cs
[AttributeUsage(AttributeTargets.Class)]
public class McpServiceAttribute : Attribute
{
    public bool AutoDiscovery { get; set; } = true;
    public string[]? Capabilities { get; set; }
    public string? ServiceName { get; set; }
    public int AnnouncementIntervalMinutes { get; set; } = 5;
}

[AttributeUsage(AttributeTargets.Class)]  
public class McpOrchestratorAttribute : Attribute
{
    public bool AutoDiscovery { get; set; } = true;
    public int DiscoveryTimeoutSeconds { get; set; } = 30;
    public bool EnableToolAggregation { get; set; } = true;
}

// File: src/Koan.MCP.Core/Attributes/McpToolAttribute.cs
[AttributeUsage(AttributeTargets.Method)]
public class McpToolAttribute : Attribute
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsAsync { get; set; } = true;
}
```

##### **3.1.2 Auto-Registration Implementation**

```csharp
// File: src/Koan.MCP.Core/Initialization/KoanAutoRegistrar.cs
public sealed class McpAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.MCP";
    public string? ModuleVersion => typeof(McpAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Core MCP services
        services.TryAddSingleton<IMcpServiceRegistry, McpServiceRegistry>();
        services.TryAddSingleton<IMcpToolExecutor, McpToolExecutor>();
        services.AddHostedService<McpDiscoveryService>();
        
        // Scan for MCP services and orchestrators
        RegisterMcpServices(services);
        
        // Configure message routing
        ConfigureMcpMessaging(services);
    }
    
    private void RegisterMcpServices(IServiceCollection services)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        
        foreach (var type in assembly.GetTypes())
        {
            var mcpServiceAttr = type.GetCustomAttribute<McpServiceAttribute>();
            var mcpOrchestratorAttr = type.GetCustomAttribute<McpOrchestratorAttribute>();
            
            if (mcpServiceAttr != null)
            {
                services.AddSingleton(typeof(IMcpService), type);
                services.AddHostedService<McpServiceHost>();
            }
            
            if (mcpOrchestratorAttr != null)
            {
                services.AddSingleton<IMcpOrchestrator>(type);
                services.AddHostedService<McpOrchestratorHost>();
            }
        }
    }
}
```

##### **3.1.3 Messaging Integration**

```csharp
// File: src/Koan.MCP.Core/Services/McpDiscoveryService.cs
public class McpDiscoveryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpDiscoveryService> _logger;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Register handler for MCP service announcements
        await RegisterMessageHandlers(cancellationToken);
        
        // Wait for messaging to be ready
        await WaitForMessagingReady(cancellationToken);
        
        _logger.LogInformation("MCP discovery service started");
        
        // Keep service running to handle announcements
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
    
    private async Task RegisterMessageHandlers(CancellationToken cancellationToken)
    {
        // Use Koan's messaging pattern to handle announcements
        var services = ServiceProviderServiceExtensions.GetServices<IServiceCollection>(_serviceProvider);
        services.On<McpServiceAnnouncement>(HandleServiceAnnouncement);
    }
    
    private async Task HandleServiceAnnouncement(McpServiceAnnouncement announcement)
    {
        var registry = _serviceProvider.GetRequiredService<IMcpServiceRegistry>();
        
        await registry.RegisterServiceAsync(new McpServiceDescriptor
        {
            ServiceId = announcement.ServiceId,
            ServiceName = announcement.ServiceName,
            Endpoint = announcement.Endpoint,
            Capabilities = announcement.Capabilities,
            Metadata = announcement.Metadata,
            LastSeen = announcement.Timestamp
        });
        
        _logger.LogInformation("Registered MCP service: {ServiceId} with capabilities: {Capabilities}",
            announcement.ServiceId, string.Join(", ", announcement.Capabilities));
    }
}
```

#### **Phase 1 Success Criteria:**
- ✅ MCP services can auto-register via `[McpService]` attribute
- ✅ Service announcements route through Koan messaging system
- ✅ Basic MCP tool execution works end-to-end
- ✅ Health checks integrate with Koan health system

---

### Phase 2: Service Discovery & Orchestration (Week 2)

#### **Goals:**
- Implement MCP service registry with messaging-based discovery
- Create MCP orchestrator for service aggregation
- Enable container-based service coordination

#### **Deliverables:**

##### **3.2.1 Service Registry Implementation**

```csharp
// File: src/Koan.MCP.Core/Services/McpServiceRegistry.cs
public interface IMcpServiceRegistry
{
    Task RegisterServiceAsync(McpServiceDescriptor service, CancellationToken ct = default);
    Task UnregisterServiceAsync(string serviceId, CancellationToken ct = default);
    Task<IReadOnlyList<McpServiceDescriptor>> GetServicesAsync(CancellationToken ct = default);
    Task<McpServiceDescriptor?> FindServiceForToolAsync(string toolName, CancellationToken ct = default);
    
    event EventHandler<McpServiceRegisteredEventArgs>? ServiceRegistered;
    event EventHandler<McpServiceUnregisteredEventArgs>? ServiceUnregistered;
}

public class McpServiceRegistry : IMcpServiceRegistry, IHealthContributor
{
    private readonly ConcurrentDictionary<string, McpServiceDescriptor> _services = new();
    private readonly ILogger<McpServiceRegistry> _logger;
    
    public string Name => "mcp-service-registry";
    public bool IsCritical => false; // Non-critical for app operation
    
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var activeServices = _services.Count;
            var healthyServices = await CountHealthyServicesAsync(ct);
            
            return HealthReport.Healthy($"Registry: {healthyServices}/{activeServices} services healthy");
        }
        catch (Exception ex)
        {
            return HealthReport.Unhealthy("MCP registry check failed", ex);
        }
    }
    
    public Task RegisterServiceAsync(McpServiceDescriptor service, CancellationToken ct = default)
    {
        _services.AddOrUpdate(service.ServiceId, service, (_, existing) => 
        {
            // Update timestamp and capabilities
            existing.LastSeen = service.LastSeen;
            existing.Capabilities = service.Capabilities;
            existing.Metadata = service.Metadata;
            return existing;
        });
        
        ServiceRegistered?.Invoke(this, new McpServiceRegisteredEventArgs(service));
        
        _logger.LogDebug("Registered MCP service: {ServiceId} at {Endpoint}",
            service.ServiceId, service.Endpoint);
            
        return Task.CompletedTask;
    }
    
    public async Task<McpServiceDescriptor?> FindServiceForToolAsync(string toolName, CancellationToken ct = default)
    {
        // Find service that exposes the requested tool
        foreach (var service in _services.Values)
        {
            if (service.Capabilities.Contains(toolName) || 
                await ServiceSupportsToolAsync(service, toolName, ct))
            {
                return service;
            }
        }
        
        return null;
    }
}
```

##### **3.2.2 MCP Orchestrator Implementation**

```csharp
// File: src/Koan.MCP.Core/Services/McpOrchestrator.cs
[McpOrchestrator(AutoDiscovery = true)]
public class McpOrchestrator : BackgroundService, IMcpService
{
    private readonly IMcpServiceRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpOrchestrator> _logger;
    
    public string ServiceId => "Koan-mcp-orchestrator";
    public string ServiceName => "Koan MCP Orchestrator";
    public IReadOnlyList<McpToolDescriptor> AvailableTools => GetAggregatedTools();
    public IReadOnlyList<McpResourceDescriptor> AvailableResources => GetAggregatedResources();
    
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Announce orchestrator capabilities
        await AnnounceOrchestrator(cancellationToken);
        
        // Start periodic health checks of registered services
        _ = Task.Run(() => PeriodicHealthCheck(cancellationToken), cancellationToken);
        
        // Handle service registry events
        _registry.ServiceRegistered += OnServiceRegistered;
        _registry.ServiceUnregistered += OnServiceUnregistered;
        
        _logger.LogInformation("MCP Orchestrator started");
        
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
    
    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        // Find service that provides this tool
        var service = await _registry.FindServiceForToolAsync(toolName, ct);
        if (service == null)
        {
            return McpToolResult.Error($"Tool '{toolName}' not found in any registered service");
        }
        
        // Route the tool call to the appropriate service
        return await CallServiceTool(service, toolName, arguments, ct);
    }
    
    private async Task<McpToolResult> CallServiceTool(McpServiceDescriptor service, string toolName, JsonElement arguments, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient("mcp-orchestrator");
        
        var request = new McpToolRequest
        {
            ToolName = toolName,
            Arguments = arguments
        };
        
        try
        {
            var response = await httpClient.PostAsync($"{service.Endpoint}/mcp/tools/{toolName}", 
                JsonContent.Create(request), ct);
                
            response.EnsureSuccessStatusCode();
            
            var resultJson = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<McpToolResult>(resultJson) ?? 
                   McpToolResult.Error("Failed to deserialize tool result");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool {ToolName} on service {ServiceId}", toolName, service.ServiceId);
            return McpToolResult.Error($"Tool execution failed: {ex.Message}");
        }
    }
}
```

##### **3.2.3 Container Integration**

```yaml
# File: samples/Koan.MCP.Sample/docker-compose.yml
services:
  mcp-orchestrator:
    build:
      context: .
      dockerfile: samples/Koan.MCP.Sample/Koan.MCP.Orchestrator/Dockerfile
    container_name: Koan-mcp-orchestrator
    environment:
      - Koan_MCP_AUTO_DISCOVERY=true
      - Koan_MESSAGING_RABBITMQ_URL=amqp://guest:guest@rabbitmq:5672
      - ASPNETCORE_URLS=http://+:8080
    ports:
      - "8080:8080"
    depends_on:
      rabbitmq:
        condition: service_healthy
    networks:
      - Koan-mcp-network

  user-service:
    build:
      context: .
      dockerfile: samples/Koan.MCP.Sample/Koan.MCP.UserService/Dockerfile
    container_name: Koan-mcp-user-service
    environment:
      - Koan_MCP_AUTO_DISCOVERY=true
      - Koan_MESSAGING_RABBITMQ_URL=amqp://guest:guest@rabbitmq:5672
      - ASPNETCORE_URLS=http://+:8081
      - SERVICE_ENDPOINT=http://user-service:8081
    expose:
      - "8081"
    depends_on:
      - rabbitmq
      - mcp-orchestrator
    networks:
      - Koan-mcp-network

  analytics-service:
    build:
      context: .
      dockerfile: samples/Koan.MCP.Sample/Koan.MCP.AnalyticsService/Dockerfile
    container_name: Koan-mcp-analytics-service
    environment:
      - Koan_MCP_AUTO_DISCOVERY=true
      - Koan_MESSAGING_RABBITMQ_URL=amqp://guest:guest@rabbitmq:5672
      - ASPNETCORE_URLS=http://+:8082
      - SERVICE_ENDPOINT=http://analytics-service:8082
    expose:
      - "8082"
    depends_on:
      - rabbitmq
      - mcp-orchestrator
    networks:
      - Koan-mcp-network

networks:
  Koan-mcp-network:
    driver: bridge
```

#### **Phase 2 Success Criteria:**
- ✅ Services automatically discover each other in container environments
- ✅ MCP orchestrator aggregates tools from multiple services
- ✅ Service health monitoring and failure handling works
- ✅ Container deployment via docker-compose functions correctly

---

### Phase 3: HTTP Transport & External Integration (Week 3)

#### **Goals:**
- Implement HTTP-based MCP server for external AI applications
- Add OAuth 2.1 security for remote access
- Create ASP.NET Core integration package

#### **Deliverables:**

##### **3.3.1 HTTP MCP Server**

```csharp
// File: src/Koan.MCP.AspNetCore/Controllers/McpController.cs
[ApiController]
[Route("mcp")]
[Produces("application/json")]
public class McpController : ControllerBase
{
    private readonly IMcpService _mcpService;
    private readonly ILogger<McpController> _logger;
    
    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize([FromBody] McpInitializeRequest request)
    {
        // Handle MCP initialization handshake
        var response = new McpInitializeResponse
        {
            ProtocolVersion = "2025-06-18",
            Capabilities = new McpCapabilities
            {
                Tools = new McpToolCapability { ListChanged = true },
                Resources = new McpResourceCapability { Subscribe = true }
            },
            ServerInfo = new McpServerInfo
            {
                Name = _mcpService.ServiceName,
                Version = "1.0.0"
            },
            Instructions = $"Koan MCP Server providing {_mcpService.AvailableTools.Count} tools"
        };
        
        return Ok(response);
    }
    
    [HttpPost("tools/list")]
    public async Task<IActionResult> ListTools()
    {
        var tools = _mcpService.AvailableTools.Select(tool => new
        {
            name = tool.Name,
            description = tool.Description,
            inputSchema = tool.InputSchema
        });
        
        return Ok(new { tools });
    }
    
    [HttpPost("tools/call")]
    public async Task<IActionResult> CallTool([FromBody] McpToolCallRequest request)
    {
        try
        {
            var result = await _mcpService.ExecuteToolAsync(request.Name, request.Arguments, HttpContext.RequestAborted);
            
            if (result.IsError)
            {
                return BadRequest(new { content = new[] { new { type = "text", text = result.ErrorMessage } }, isError = true });
            }
            
            return Ok(new { content = new[] { new { type = "text", text = result.Content } }, isError = false });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed for {ToolName}", request.Name);
            return StatusCode(500, new { content = new[] { new { type = "text", text = "Internal server error" } }, isError = true });
        }
    }
    
    [HttpPost("resources/list")]
    public async Task<IActionResult> ListResources()
    {
        var resources = _mcpService.AvailableResources.Select(resource => new
        {
            uri = resource.Uri,
            name = resource.Name,
            description = resource.Description,
            mimeType = resource.MimeType
        });
        
        return Ok(new { resources });
    }
    
    [HttpPost("resources/read")]
    public async Task<IActionResult> ReadResource([FromBody] McpResourceReadRequest request)
    {
        var result = await _mcpService.ReadResourceAsync(request.Uri, HttpContext.RequestAborted);
        
        if (result.IsError)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }
        
        return Ok(new { 
            contents = new[] { 
                new { 
                    uri = request.Uri, 
                    mimeType = result.MimeType, 
                    text = result.Content 
                } 
            } 
        });
    }
}
```

##### **3.3.2 OAuth 2.1 Security Implementation**

```csharp
// File: src/Koan.MCP.AspNetCore/Authentication/McpOAuthHandler.cs
public class McpOAuthHandler : AuthenticationHandler<McpOAuthSchemeOptions>
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return AuthenticateResult.NoResult();
        }
        
        var authHeader = Request.Headers["Authorization"].ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Invalid authorization header");
        }
        
        var token = authHeader.Substring("Bearer ".Length).Trim();
        
        try
        {
            // Validate OAuth 2.1 token
            var principal = await ValidateTokenAsync(token);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            
            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail($"Token validation failed: {ex.Message}");
        }
    }
    
    private async Task<ClaimsPrincipal> ValidateTokenAsync(string token)
    {
        // Implement OAuth 2.1 token validation
        // This would integrate with your OAuth provider
        
        var handler = new JsonWebTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKeys = await GetSigningKeysAsync()
        };
        
        var result = await handler.ValidateTokenAsync(token, validationParameters);
        
        if (!result.IsValid)
        {
            throw new SecurityTokenException("Invalid token");
        }
        
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }
}

// File: src/Koan.MCP.AspNetCore/Controllers/OAuthController.cs
[ApiController]
[Route(".well-known")]
public class OAuthController : ControllerBase
{
    [HttpGet("oauth-protected-resource")]
    public IActionResult GetProtectedResourceMetadata()
    {
        return Ok(new
        {
            resource = $"{Request.Scheme}://{Request.Host}",
            authorization_servers = new[] { Options.Value.AuthorizationServerUrl }
        });
    }
    
    [HttpGet("oauth-authorization-server")]
    public IActionResult GetAuthorizationServerMetadata()
    {
        return Ok(new
        {
            issuer = Options.Value.Issuer,
            authorization_endpoint = $"{Options.Value.AuthorizationServerUrl}/authorize",
            token_endpoint = $"{Options.Value.AuthorizationServerUrl}/token",
            registration_endpoint = $"{Options.Value.AuthorizationServerUrl}/register",
            code_challenge_methods_supported = new[] { "S256" }
        });
    }
}
```

##### **3.3.3 ASP.NET Core Extensions**

```csharp
// File: src/Koan.MCP.AspNetCore/Extensions/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanMcp(this IServiceCollection services)
    {
        return services.AddKoanMcp(_ => { });
    }
    
    public static IServiceCollection AddKoanMcp(this IServiceCollection services, Action<McpOptions> configure)
    {
        // Add core MCP services
        services.AddSingleton<IMcpServiceRegistry, McpServiceRegistry>();
        services.AddSingleton<IMcpToolExecutor, McpToolExecutor>();
        services.AddHostedService<McpDiscoveryService>();
        
        // Configure options
        services.Configure(configure);
        
        // Add HTTP client for service-to-service calls
        services.AddHttpClient("mcp-orchestrator", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Koan-MCP-Client/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        // Add authentication if enabled
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<McpOptions>>()?.Value ?? new McpOptions();
        
        if (options.EnableAuthentication)
        {
            services.AddAuthentication("MCP-OAuth")
                .AddScheme<McpOAuthSchemeOptions, McpOAuthHandler>("MCP-OAuth", configureOptions =>
                {
                    configureOptions.Issuer = options.OAuth.Issuer;
                    configureOptions.Audience = options.OAuth.Audience;
                });
        }
        
        return services;
    }
    
    public static IApplicationBuilder UseKoanMcp(this IApplicationBuilder app)
    {
        // Add MCP-specific middleware
        app.UseMiddleware<McpRequestLoggingMiddleware>();
        
        // Add OAuth endpoints if authentication is enabled
        var options = app.ApplicationServices.GetService<IOptions<McpOptions>>()?.Value ?? new McpOptions();
        if (options.EnableAuthentication)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }
        
        return app;
    }
}

// File: src/Koan.MCP.AspNetCore/Extensions/ApplicationBuilderExtensions.cs
public static class ApplicationBuilderExtensions
{
    public static IEndpointRouteBuilder MapMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        endpoints.MapControllerRoute(
            name: "mcp",
            pattern: pattern + "/{action}",
            defaults: new { controller = "Mcp" });
            
        return endpoints;
    }
}
```

#### **Phase 3 Success Criteria:**
- ✅ External AI applications can connect via HTTP MCP protocol
- ✅ OAuth 2.1 authentication secures remote access
- ✅ ASP.NET Core integration provides clean developer experience  
- ✅ Full MCP protocol compliance (initialization, tools, resources)

---

### Phase 4: Samples & Documentation (Week 4)

#### **Goals:**
- Create comprehensive sample applications
- Write documentation and integration guides
- Performance optimization and testing
- Production deployment guidance

#### **Deliverables:**

##### **3.4.1 Sample Applications**

```csharp
// File: samples/Koan.MCP.Sample/UserService/UserService.cs
[McpService(AutoDiscovery = true, Capabilities = new[] { "user-management", "authentication" })]
public class UserService : BackgroundService, IMcpService
{
    public string ServiceId => "user-service";
    public string ServiceName => "User Management Service";
    
    [McpTool("get_user", "Retrieve user information by ID")]
    public async Task<UserDto> GetUserAsync(
        [Description("The user ID")] string userId)
    {
        // Simulate user lookup
        return new UserDto
        {
            Id = userId,
            Name = $"User {userId}",
            Email = $"user{userId}@example.com",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };
    }
    
    [McpTool("create_user", "Create a new user")]
    public async Task<UserDto> CreateUserAsync(
        [Description("The user's name")] string name,
        [Description("The user's email")] string email)
    {
        // Simulate user creation
        var userId = Guid.NewGuid().ToString();
        return new UserDto
        {
            Id = userId,
            Name = name,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
    
    [McpTool("list_users", "List all users with pagination")]
    public async Task<PagedResult<UserDto>> ListUsersAsync(
        [Description("Page number (1-based)")] int page = 1,
        [Description("Items per page")] int pageSize = 10)
    {
        // Simulate paged user list
        var users = Enumerable.Range(1, pageSize)
            .Select(i => new UserDto
            {
                Id = $"user-{page}-{i}",
                Name = $"User {page}-{i}",
                Email = $"user{page}{i}@example.com",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-i)
            })
            .ToList();
            
        return new PagedResult<UserDto>
        {
            Items = users,
            TotalCount = 1000,
            Page = page,
            PageSize = pageSize
        };
    }
    
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Announce service capabilities
        await new McpServiceAnnouncement(this)
        {
            ServiceId = ServiceId,
            ServiceName = ServiceName,
            Endpoint = Environment.GetEnvironmentVariable("SERVICE_ENDPOINT") ?? "http://localhost:8081",
            Capabilities = new[] { "get_user", "create_user", "list_users" },
            Metadata = new Dictionary<string, object>
            {
                ["version"] = "1.0.0",
                ["health_check"] = "/health",
                ["supported_formats"] = new[] { "json" }
            }
        }.Send();
        
        // Periodic heartbeat
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            
            // Re-announce to handle registry restarts
            await new McpServiceAnnouncement(this)
            {
                ServiceId = ServiceId,
                ServiceName = ServiceName,
                Endpoint = Environment.GetEnvironmentVariable("SERVICE_ENDPOINT") ?? "http://localhost:8081",
                Capabilities = new[] { "get_user", "create_user", "list_users" },
                Metadata = new Dictionary<string, object>
                {
                    ["last_heartbeat"] = DateTimeOffset.UtcNow,
                    ["status"] = "healthy"
                }
            }.Send();
        }
    }
}

// File: samples/Koan.MCP.Sample/AnalyticsService/AnalyticsService.cs
[McpService(AutoDiscovery = true, Capabilities = new[] { "data-analysis", "reporting" })]
public class AnalyticsService : BackgroundService, IMcpService
{
    private readonly IAi _ai;
    
    public string ServiceId => "analytics-service";
    public string ServiceName => "Analytics and Reporting Service";
    
    [McpTool("analyze_data", "Perform statistical analysis on provided data")]
    public async Task<AnalysisResult> AnalyzeDataAsync(
        [Description("The data to analyze as JSON array")] JsonElement data,
        [Description("Type of analysis to perform")] string analysisType = "summary")
    {
        // Convert JSON data to numeric array for analysis
        var numbers = data.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Number)
            .Select(x => x.GetDouble())
            .ToArray();
            
        if (!numbers.Any())
        {
            return new AnalysisResult
            {
                AnalysisType = analysisType,
                Error = "No numeric data found in input"
            };
        }
        
        return analysisType.ToLowerInvariant() switch
        {
            "summary" => new AnalysisResult
            {
                AnalysisType = "summary",
                Results = new Dictionary<string, object>
                {
                    ["count"] = numbers.Length,
                    ["mean"] = numbers.Average(),
                    ["min"] = numbers.Min(),
                    ["max"] = numbers.Max(),
                    ["std_dev"] = CalculateStandardDeviation(numbers)
                }
            },
            "ai_insights" => await GenerateAiInsights(numbers),
            _ => new AnalysisResult
            {
                AnalysisType = analysisType,
                Error = $"Unknown analysis type: {analysisType}"
            }
        };
    }
    
    [McpTool("generate_report", "Generate a formatted report from analysis results")]
    public async Task<ReportResult> GenerateReportAsync(
        [Description("Analysis results to format")] AnalysisResult analysisResult,
        [Description("Report format")] string format = "markdown")
    {
        if (analysisResult.Error != null)
        {
            return new ReportResult
            {
                Format = format,
                Error = $"Cannot generate report: {analysisResult.Error}"
            };
        }
        
        var report = format.ToLowerInvariant() switch
        {
            "markdown" => GenerateMarkdownReport(analysisResult),
            "html" => GenerateHtmlReport(analysisResult),
            "json" => JsonSerializer.Serialize(analysisResult, new JsonSerializerOptions { WriteIndented = true }),
            _ => $"Unsupported format: {format}"
        };
        
        return new ReportResult
        {
            Format = format,
            Content = report,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }
    
    private async Task<AnalysisResult> GenerateAiInsights(double[] numbers)
    {
        try
        {
            var dataDescription = $"Dataset of {numbers.Length} numbers: mean={numbers.Average():F2}, min={numbers.Min():F2}, max={numbers.Max():F2}";
            
            var aiRequest = new AiChatRequest
            {
                Messages = new[]
                {
                    new AiMessage
                    {
                        Role = AiMessageRole.User,
                        Content = $"Analyze this dataset and provide insights: {dataDescription}. What patterns, trends, or anomalies do you notice?"
                    }
                },
                Model = "gpt-4"
            };
            
            var response = await _ai.ChatAsync(aiRequest);
            var insights = response.Choices?.FirstOrDefault()?.Message?.Content ?? "No insights generated";
            
            return new AnalysisResult
            {
                AnalysisType = "ai_insights",
                Results = new Dictionary<string, object>
                {
                    ["insights"] = insights,
                    ["model_used"] = "gpt-4",
                    ["generated_at"] = DateTimeOffset.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            return new AnalysisResult
            {
                AnalysisType = "ai_insights",
                Error = $"AI analysis failed: {ex.Message}"
            };
        }
    }
}

// File: samples/Koan.MCP.Sample/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Koan services
builder.Services.AddKoan();

// Add MCP services
builder.Services.AddKoanMcp(options =>
{
    options.EnableAuthentication = builder.Environment.IsProduction();
    options.EnableDiscovery = true;
    options.DiscoveryTimeoutSeconds = 30;
});

// Add AI services for analytics
builder.Services.AddSingleton<IAi, Ai>();

var app = builder.Build();

// Configure pipeline
app.UseKoanMcp();

// Map MCP endpoints
app.MapMcp("/mcp");

// Add health checks
app.MapHealthChecks("/health");

// Add Swagger for development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await app.RunAsync();
```

##### **3.4.2 Integration Documentation**

```markdown
# File: docs/guides/mcp/getting-started.md

# Koan MCP Integration Guide

## Overview

The Koan MCP integration enables any Koan service to expose its capabilities as Model Context Protocol (MCP) tools, allowing AI applications like Claude Desktop, Copilot extensions, and custom AI agents to interact with your Koan services.

## Quick Start

### 1. Add MCP Support to Your Service

```csharp
// Add the MCP NuGet package
// dotnet add package Koan.MCP.Core

[McpService(AutoDiscovery = true)]
public class MyService : BackgroundService, IMcpService
{
    public string ServiceId => "my-service";
    public string ServiceName => "My Business Service";
    
    [McpTool("process_data", "Process business data")]
    public async Task<ProcessResult> ProcessDataAsync(
        [Description("The data to process")] string data)
    {
        // Your business logic here
        return new ProcessResult { Success = true, Message = "Data processed successfully" };
    }
    
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Service announces itself automatically
        await AnnounceService(ct);
        
        // Keep service running
        await Task.Delay(Timeout.Infinite, ct);
    }
}
```

### 2. Configure Your Application

```csharp
var builder = Host.CreateApplicationBuilder(args);

// Add Koan services (includes messaging)
builder.Services.AddKoan();

// Add MCP services
builder.Services.AddKoanMcp();

var app = builder.Build();
await app.RunAsync();
```

### 3. Deploy with Docker Compose

```yaml
version: '3.8'
services:
  my-service:
    build: .
    environment:
      - Koan_MCP_AUTO_DISCOVERY=true
      - Koan_MESSAGING_RABBITMQ_URL=amqp://guest:guest@rabbitmq:5672
    depends_on:
      - rabbitmq
```

That's it! Your service will automatically:
- ✅ Announce its MCP tools via Koan messaging
- ✅ Register with any MCP orchestrators in the same network
- ✅ Respond to direct MCP tool calls from AI applications
- ✅ Include health checks and monitoring

## Architecture Patterns

### Service-to-Service Discovery

Services discover each other automatically in container environments:

1. **Service Announcement**: Each `[McpService]` announces its capabilities via `Koan.MCP.ServiceAnnouncement` messages
2. **Registry Discovery**: `[McpOrchestrator]` services listen for announcements and maintain a registry
3. **Tool Aggregation**: Orchestrators provide unified MCP interfaces to external AI applications
4. **Direct Routing**: AI applications can call tools directly on services or through orchestrators

### Deployment Patterns

#### Pattern 1: Standalone Services (Simple)
- Each service exposes its own MCP endpoint
- AI applications discover and connect to services directly
- Good for: Single-service scenarios, development, testing

#### Pattern 2: Orchestrated Services (Recommended)
- Services register with central MCP orchestrator
- AI applications connect to orchestrator for unified interface
- Good for: Multi-service deployments, production environments

#### Pattern 3: Hybrid (Flexible)
- Services work standalone but also participate in orchestration
- AI applications can use either direct or orchestrated access
- Good for: Complex deployments with mixed requirements

## Configuration Reference

```csharp
// Configure MCP services
builder.Services.AddKoanMcp(options =>
{
    // Enable/disable automatic service discovery
    options.EnableDiscovery = true;
    
    // Timeout for discovery operations
    options.DiscoveryTimeoutSeconds = 30;
    
    // Enable OAuth 2.1 authentication for remote access
    options.EnableAuthentication = builder.Environment.IsProduction();
    
    // Announcement interval for service heartbeats
    options.AnnouncementIntervalMinutes = 5;
    
    // Custom service endpoint (auto-detected in containers)
    options.ServiceEndpoint = "http://my-service:8080";
});
```

## Tool Development Patterns

### Simple Tools

```csharp
[McpTool("calculate", "Perform basic calculations")]
public async Task<double> CalculateAsync(
    [Description("First number")] double a,
    [Description("Second number")] double b,
    [Description("Operation: add, subtract, multiply, divide")] string operation)
{
    return operation.ToLower() switch
    {
        "add" => a + b,
        "subtract" => a - b,
        "multiply" => a * b,
        "divide" => b != 0 ? a / b : throw new ArgumentException("Division by zero"),
        _ => throw new ArgumentException($"Unknown operation: {operation}")
    };
}
```

### Complex Tools with AI Integration

```csharp
[McpTool("ai_analysis", "Perform AI-powered data analysis")]
public async Task<AnalysisResult> AnalyzeWithAiAsync(
    [Description("Data to analyze")] JsonElement data,
    [Description("Analysis type")] string analysisType)
{
    // Use Koan AI services for analysis
    var aiRequest = new AiChatRequest
    {
        Messages = new[] 
        {
            new AiMessage 
            { 
                Role = AiMessageRole.User, 
                Content = $"Analyze this data: {data} using {analysisType} approach" 
            }
        }
    };
    
    var response = await _ai.ChatAsync(aiRequest);
    
    return new AnalysisResult
    {
        Analysis = response.Choices?.FirstOrDefault()?.Message?.Content,
        Confidence = 0.85,
        GeneratedAt = DateTimeOffset.UtcNow
    };
}
```

### Resource Patterns

```csharp
[McpResource("Koan://models", "Available AI Models", "application/json")]
public async Task<McpResourceResult> GetAvailableModelsAsync()
{
    var models = await _aiRegistry.GetAvailableModelsAsync();
    var json = JsonSerializer.Serialize(models, new JsonSerializerOptions { WriteIndented = true });
    
    return McpResourceResult.Success(json, "application/json");
}

[McpResource("Koan://config", "Service Configuration", "application/json")]
public async Task<McpResourceResult> GetServiceConfigAsync()
{
    var config = new
    {
        ServiceId,
        ServiceName,
        Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
        Capabilities = AvailableTools.Select(t => t.Name).ToArray(),
        Health = await GetHealthStatusAsync()
    };
    
    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    return McpResourceResult.Success(json, "application/json");
}
```

## Testing and Debugging

### Local Development

```csharp
// Program.cs for development
if (builder.Environment.IsDevelopment())
{
    // Enable detailed logging
    builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Debug);
    
    // Add Swagger for MCP endpoints
    builder.Services.AddSwagger();
    
    // Disable authentication for local testing
    builder.Services.AddKoanMcp(options =>
    {
        options.EnableAuthentication = false;
        options.EnableDiscovery = true;
    });
}
```

### Testing with Claude Desktop

1. Install Claude Desktop
2. Configure MCP server connection:

```json
// ~/.config/claude-desktop/claude_desktop_config.json
{
  "mcpServers": {
    "Koan-service": {
      "command": "curl",
      "args": ["-X", "POST", "http://localhost:8080/mcp/initialize"]
    }
  }
}
```

### Unit Testing MCP Tools

```csharp
[Test]
public async Task TestCalculateTool()
{
    var service = new MyMcpService();
    var result = await service.CalculateAsync(5, 3, "add");
    
    Assert.AreEqual(8, result);
}

[Test]
public async Task TestToolDiscovery()
{
    var registry = new McpServiceRegistry();
    var announcement = new McpServiceAnnouncement(new MyMcpService())
    {
        ServiceId = "test-service",
        Capabilities = new[] { "calculate", "analyze" }
    };
    
    await registry.RegisterServiceAsync(announcement);
    
    var service = await registry.FindServiceForToolAsync("calculate");
    Assert.IsNotNull(service);
    Assert.AreEqual("test-service", service.ServiceId);
}
```

## Production Deployment

### Security Configuration

```csharp
// Production configuration
builder.Services.AddKoanMcp(options =>
{
    // Enable OAuth 2.1 authentication
    options.EnableAuthentication = true;
    options.OAuth = new OAuthOptions
    {
        Issuer = "https://your-auth-server.com",
        Audience = "Koan-mcp-api",
        RequireHttps = true
    };
    
    // Restrict discovery to secure networks
    options.AllowDiscoveryFrom = new[] { "10.0.0.0/8", "172.16.0.0/12" };
    
    // Enable request logging
    options.EnableRequestLogging = true;
});

// Add HTTPS redirection
app.UseHttpsRedirection();
app.UseHsts();
```

### Monitoring and Observability

```csharp
// Add health checks for MCP services
builder.Services.AddHealthChecks()
    .AddCheck<McpServiceHealthCheck>("mcp-services")
    .AddCheck<McpRegistryHealthCheck>("mcp-registry");

// Add metrics
builder.Services.AddMetrics();
app.MapMetrics("/metrics");

// Add distributed tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Koan.MCP")
        .AddJaegerExporter());
```

### Scaling Considerations

- **Service Registry**: Use Redis for distributed service registry in multi-node deployments
- **Load Balancing**: Configure load balancers to route MCP calls appropriately
- **Circuit Breakers**: Implement circuit breakers for external AI service calls
- **Rate Limiting**: Add rate limiting for public MCP endpoints

## Best Practices

1. **Tool Design**: Keep tools focused and composable
2. **Error Handling**: Return descriptive errors with proper HTTP status codes
3. **Security**: Always enable authentication for production deployments
4. **Documentation**: Use detailed descriptions for all tools and parameters
5. **Testing**: Write comprehensive tests for all MCP tools
6. **Monitoring**: Monitor tool usage patterns and performance metrics
7. **Versioning**: Version your tools and handle backwards compatibility

## Troubleshooting

### Common Issues

#### Service Not Discovered
- Check RabbitMQ connectivity
- Verify `Koan_MCP_AUTO_DISCOVERY=true` environment variable
- Check container networking configuration

#### Tool Calls Failing
- Verify tool method signatures match MCP requirements
- Check parameter serialization/deserialization
- Review error logs for detailed failure information

#### Authentication Issues
- Verify OAuth 2.1 configuration
- Check token scopes and audience claims
- Ensure HTTPS is enabled for production

### Debug Logging

```csharp
// Enable detailed MCP logging
builder.Logging.AddFilter("Koan.MCP", LogLevel.Debug);
```

This will log:
- Service announcements and registrations
- Tool discovery and execution
- Authentication attempts
- Health check results
```

#### **Phase 4 Success Criteria:**
- ✅ Complete sample applications demonstrate all MCP patterns
- ✅ Comprehensive documentation covers integration scenarios
- ✅ Performance benchmarks meet production requirements
- ✅ Production deployment guide enables enterprise adoption

---

## 4. Technical Specifications

### 4.1 Component Architecture

```
Koan.MCP/
├── Koan.MCP.Core/                    # Core interfaces and models
│   ├── Interfaces/
│   │   ├── IMcpService.cs
│   │   ├── IMcpServiceRegistry.cs
│   │   ├── IMcpToolExecutor.cs
│   │   └── IMcpOrchestrator.cs
│   ├── Models/
│   │   ├── McpServiceAnnouncement.cs
│   │   ├── McpServiceDescriptor.cs
│   │   ├── McpToolDescriptor.cs
│   │   ├── McpToolResult.cs
│   │   └── McpResourceDescriptor.cs
│   ├── Attributes/
│   │   ├── McpServiceAttribute.cs
│   │   ├── McpOrchestratorAttribute.cs
│   │   ├── McpToolAttribute.cs
│   │   └── McpResourceAttribute.cs
│   ├── Services/
│   │   ├── McpServiceRegistry.cs
│   │   ├── McpToolExecutor.cs
│   │   ├── McpDiscoveryService.cs
│   │   └── McpOrchestrator.cs
│   └── Initialization/
│       └── McpAutoRegistrar.cs
│
├── Koan.MCP.AspNetCore/              # ASP.NET Core integration
│   ├── Controllers/
│   │   ├── McpController.cs
│   │   └── OAuthController.cs
│   ├── Authentication/
│   │   ├── McpOAuthHandler.cs
│   │   └── McpOAuthSchemeOptions.cs
│   ├── Middleware/
│   │   ├── McpRequestLoggingMiddleware.cs
│   │   └── McpErrorHandlingMiddleware.cs
│   └── Extensions/
│       ├── ServiceCollectionExtensions.cs
│       └── ApplicationBuilderExtensions.cs
│
└── Samples/
    ├── Koan.MCP.Sample.UserService/
    ├── Koan.MCP.Sample.AnalyticsService/
    └── Koan.MCP.Sample.Orchestrator/
```

### 4.2 Message Flow Architecture

```mermaid
sequenceDiagram
    participant Service as MCP Service
    participant Queue as RabbitMQ
    participant Registry as MCP Registry
    participant AI as AI Application

    Note over Service,AI: Service Discovery Flow
    Service->>Queue: ServiceAnnouncement{tools, endpoint}
    Queue->>Registry: Route announcement
    Registry->>Registry: Register service capabilities
    
    Note over Service,AI: Tool Execution Flow
    AI->>Registry: GET /mcp/tools/list
    Registry->>AI: Aggregated tool list
    AI->>Registry: POST /mcp/tools/call{tool, args}
    Registry->>Service: Route tool call
    Service->>Service: Execute business logic
    Service->>Registry: Tool result
    Registry->>AI: MCP-formatted response
```

### 4.3 Configuration Schema

```yaml
# appsettings.json schema
Koan:
  MCP:
    EnableDiscovery: true
    DiscoveryTimeoutSeconds: 30
    AnnouncementIntervalMinutes: 5
    EnableAuthentication: false
    ServiceEndpoint: "http://localhost:8080"
    
    # OAuth 2.1 Configuration
    OAuth:
      Issuer: "https://your-auth-server.com"
      Audience: "Koan-mcp-api"
      RequireHttps: true
      TokenValidationUrl: "https://your-auth-server.com/validate"
    
    # Discovery restrictions
    AllowDiscoveryFrom: 
      - "10.0.0.0/8"
      - "172.16.0.0/12"
      - "192.168.0.0/16"
    
    # Performance tuning
    MaxConcurrentToolCalls: 10
    ToolExecutionTimeoutSeconds: 30
    RegistryCacheDurationMinutes: 10
```

### 4.4 Health Check Integration

```csharp
// Health check integration with Koan health system
public class McpServiceHealthCheck : IHealthContributor
{
    public string Name => "mcp-services";
    public bool IsCritical => false; // Non-critical for app operation
    
    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var healthyServices = 0;
        var totalServices = 0;
        var failedServices = new List<string>();
        
        foreach (var service in await _registry.GetServicesAsync(ct))
        {
            totalServices++;
            try
            {
                var response = await _httpClient.GetAsync($"{service.Endpoint}/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    healthyServices++;
                }
                else
                {
                    failedServices.Add($"{service.ServiceId} ({response.StatusCode})");
                }
            }
            catch (Exception ex)
            {
                failedServices.Add($"{service.ServiceId} ({ex.Message})");
            }
        }
        
        if (healthyServices == totalServices)
        {
            return HealthReport.Healthy($"All {totalServices} MCP services healthy");
        }
        else if (healthyServices > 0)
        {
            return HealthReport.Degraded($"{healthyServices}/{totalServices} MCP services healthy. Failed: {string.Join(", ", failedServices)}");
        }
        else
        {
            return HealthReport.Unhealthy($"No MCP services healthy. Total: {totalServices}");
        }
    }
}
```

---

## 5. Integration Points

### 5.1 Koan Framework Integration

- **Messaging System**: Leverages `Koan.Messaging.Core` for service announcements
- **Auto-Registration**: Uses `IKoanAutoRegistrar` for seamless module loading
- **Health Checks**: Integrates with `IHealthContributor` system
- **Configuration**: Follows Koan configuration patterns and precedence rules
- **Environment Detection**: Uses `KoanEnv` for container and development environment detection

### 5.2 External System Integration

- **AI Applications**: Claude Desktop, Copilot extensions, custom AI agents
- **Authentication**: OAuth 2.1 providers, Azure AD, Auth0, custom identity providers
- **Monitoring**: Prometheus metrics, OpenTelemetry traces, Koan health endpoints
- **Container Orchestration**: Docker Compose, Kubernetes, Azure Container Apps

---

## 6. Success Metrics

### 6.1 Technical Metrics

- **Implementation Coverage**: 85% of infrastructure already available
- **Development Effort**: 3-4 weeks across multiple sessions  
- **Performance**: <100ms tool execution latency
- **Reliability**: 99.9% tool execution success rate
- **Scalability**: Support 100+ concurrent tool calls per service

### 6.2 Developer Experience Metrics

- **Lines of Code**: <10 lines to add MCP support to existing service
- **Configuration**: Zero-config for basic scenarios
- **Discovery Time**: <5 seconds for service discovery in containers
- **Documentation**: Complete integration guides and samples

### 6.3 Business Value Metrics

- **Time to AI**: Transform existing service to AI-ready in <1 hour
- **Integration Cost**: 90% reduction vs custom AI integrations
- **Ecosystem Growth**: Enable Koan services in Claude, Copilot, custom AI apps
- **Framework Differentiation**: First .NET framework with native MCP support

---

## 7. Risk Assessment & Mitigation

### 7.1 Technical Risks

#### **Risk**: Service Discovery Complexity
- **Probability**: Medium
- **Impact**: High
- **Mitigation**: Leverage proven Koan messaging patterns; provide fallback to explicit configuration

#### **Risk**: MCP Protocol Evolution
- **Probability**: High  
- **Impact**: Medium
- **Mitigation**: Implement against stable MCP specification; create abstraction layer for protocol changes

#### **Risk**: Container Networking Issues
- **Probability**: Low
- **Impact**: High
- **Mitigation**: Extensive container testing; provide troubleshooting guides; fallback to explicit endpoints

### 7.2 Business Risks

#### **Risk**: Limited MCP Adoption
- **Probability**: Low
- **Impact**: Medium
- **Mitigation**: MCP has strong industry backing (Anthropic, OpenAI, Google); provides value even without widespread adoption

#### **Risk**: Security Concerns
- **Probability**: Medium
- **Impact**: High
- **Mitigation**: Implement OAuth 2.1; provide security best practices; make authentication mandatory for production

---

## 8. Implementation Timeline

### Week 1: Core Infrastructure
- **Days 1-2**: Core MCP interfaces and models
- **Days 3-4**: Service registration and auto-discovery
- **Days 5-7**: Basic tool execution and messaging integration

### Week 2: Service Discovery & Orchestration
- **Days 8-10**: MCP service registry implementation
- **Days 11-12**: MCP orchestrator for service aggregation  
- **Days 13-14**: Container integration and testing

### Week 3: HTTP Transport & Security
- **Days 15-17**: HTTP-based MCP server for external access
- **Days 18-19**: OAuth 2.1 security implementation
- **Days 20-21**: ASP.NET Core integration package

### Week 4: Samples & Documentation
- **Days 22-24**: Comprehensive sample applications
- **Days 25-26**: Documentation and integration guides
- **Days 27-28**: Performance optimization and production deployment guidance

---

## 9. Future Considerations

### 9.1 Planned Enhancements

- **MCP Client**: Enable Koan services to consume external MCP tools
- **GraphQL Integration**: Expose MCP tools via GraphQL endpoints
- **Streaming Support**: Real-time MCP tool execution with streaming results
- **Advanced Security**: Certificate-based authentication, API key management
- **Performance Optimization**: Connection pooling, request caching, batch operations

### 9.2 Integration Opportunities

- **Koan.AI Enhancement**: Expose AI capabilities as MCP tools automatically
- **Koan.Data Integration**: Generic data access tools via MCP
- **Koan.Canon Coordination**: Event sourcing and workflow orchestration tools
- **Third-Party Providers**: MCP adapters for popular services (GitHub, Slack, etc.)

---

## 10. Conclusion

The Koan MCP integration represents a strategic enhancement that positions the framework at the forefront of AI application development. By leveraging Koan's existing infrastructure and architectural patterns, we can deliver a compelling MCP implementation with minimal complexity and maximum developer value.

The implementation plan provides a clear path to success with well-defined phases, deliverables, and success criteria. The strong alignment with Koan's core principles ensures that the MCP integration feels natural and maintains the framework's reputation for simplicity and developer experience excellence.

**Key Benefits:**
- **Strategic Positioning**: First .NET framework with native MCP support
- **Developer Value**: Transform any service to AI-ready with minimal effort
- **Framework Consistency**: Built using established Koan patterns and infrastructure
- **Production Ready**: Complete with security, monitoring, and scalability features

**Recommendation**: Proceed with implementation following the outlined plan, with particular attention to maintaining Koan's architectural integrity and developer experience standards.

---

*This document will be updated throughout implementation to reflect actual progress, lessons learned, and architectural decisions.*

<function_calls>
<invoke name="TodoWrite">
<parameter name="todos">[{"content": "Analyze current Koan codebase capabilities for MCP integration", "status": "completed", "activeForm": "Analyzing current Koan codebase capabilities for MCP integration"}, {"content": "Design MCP service discovery architecture using messaging", "status": "completed", "activeForm": "Designing MCP service discovery architecture using messaging"}, {"content": "Create detailed implementation plan with phases", "status": "completed", "activeForm": "Creating detailed implementation plan with phases"}, {"content": "Generate comprehensive CLD_ documentation", "status": "completed", "activeForm": "Generating comprehensive CLD_ documentation"}]