# Koan.Web.Auth.Services - Usage Samples

This document provides comprehensive usage examples for the Koan.Web.Auth.Services module, from basic scenarios to advanced enterprise patterns.

## Table of Contents

1. [Quick Start](#quick-start)
2. [Basic Service Communication](#basic-service-communication)
3. [Multi-Service Orchestration](#multi-service-orchestration)
4. [Error Handling and Resilience](#error-handling-and-resilience)
5. [Configuration Patterns](#configuration-patterns)
6. [Testing Strategies](#testing-strategies)
7. [Production Deployment](#production-deployment)
8. [Advanced Scenarios](#advanced-scenarios)

## Quick Start

### 1. Add Package Reference

```xml
<ProjectReference Include="Koan.Web.Auth.Services" />
```

### 2. Minimal Service Declaration

```csharp
[ApiController]
[KoanService("order-service")]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly IKoanServiceClient _serviceClient;

    public OrderController(IKoanServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    [HttpPost("process")]
    [CallsService("payment-service")]
    public async Task<IActionResult> ProcessOrder([FromBody] Order order)
    {
        var paymentResult = await _serviceClient.PostAsync<PaymentResult>(
            "payment-service", "/api/payments", order.PaymentDetails);

        return Ok(new { orderId = order.Id, paymentStatus = paymentResult?.Status });
    }
}
```

### 3. Configuration (Optional)

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "TestProvider": {
          "EnableClientCredentials": true
        }
      }
    }
  }
}
```

That's it! The framework handles authentication, service discovery, and HTTP client configuration automatically.

## Basic Service Communication

### Simple Service Call

```csharp
[ApiController]
[KoanService("user-service", ProvidedScopes = new[] { "users:read", "users:write" })]
public class UserController : ControllerBase
{
    private readonly IKoanServiceClient _client;

    public UserController(IKoanServiceClient client) => _client = client;

    [HttpGet("{id}/preferences")]
    [CallsService("preference-service", RequiredScopes = new[] { "preferences:read" })]
    public async Task<IActionResult> GetUserPreferences(int id)
    {
        var preferences = await _client.GetAsync<UserPreferences>(
            "preference-service", $"/api/users/{id}/preferences");

        return preferences != null ? Ok(preferences) : NotFound();
    }

    [HttpPost("{id}/preferences")]
    [CallsService("preference-service", RequiredScopes = new[] { "preferences:write" })]
    public async Task<IActionResult> UpdateUserPreferences(int id, [FromBody] UserPreferences preferences)
    {
        var result = await _client.PostAsync<UpdateResult>(
            "preference-service", $"/api/users/{id}/preferences", preferences);

        return Ok(result);
    }
}
```

### Typed Service Responses

```csharp
// Define your response models
public record UserPreferences(
    string Theme,
    string Language,
    bool NotificationsEnabled,
    Dictionary<string, object> CustomSettings
);

public record PaymentResult(
    string TransactionId,
    string Status,
    decimal Amount,
    DateTimeOffset ProcessedAt
);

public record ValidationResult(
    bool IsValid,
    string[] Errors,
    Dictionary<string, string> FieldErrors
);

// Use them in service calls
[HttpPost("validate")]
[CallsService("validation-service", RequiredScopes = new[] { "validation:execute" })]
public async Task<IActionResult> ValidateData([FromBody] DataModel data)
{
    var validationResult = await _client.PostAsync<ValidationResult>(
        "validation-service", "/api/validate", data);

    if (validationResult?.IsValid == true)
    {
        return Ok(new { message = "Data is valid" });
    }

    return BadRequest(new
    {
        message = "Validation failed",
        errors = validationResult?.Errors ?? new[] { "Unknown validation error" },
        fieldErrors = validationResult?.FieldErrors ?? new Dictionary<string, string>()
    });
}
```

## Multi-Service Orchestration

### Sequential Service Calls

```csharp
[ApiController]
[KoanService("order-processing-service")]
public class OrderProcessingController : ControllerBase
{
    private readonly IKoanServiceClient _client;
    private readonly ILogger<OrderProcessingController> _logger;

    public OrderProcessingController(IKoanServiceClient client, ILogger<OrderProcessingController> logger)
    {
        _client = client;
        _logger = logger;
    }

    [HttpPost("process-order")]
    [CallsService("inventory-service", RequiredScopes = new[] { "inventory:check", "inventory:reserve" })]
    [CallsService("payment-service", RequiredScopes = new[] { "payments:process" })]
    [CallsService("shipping-service", RequiredScopes = new[] { "shipping:create" })]
    [CallsService("notification-service", RequiredScopes = new[] { "notifications:send" })]
    public async Task<IActionResult> ProcessOrder([FromBody] OrderRequest request)
    {
        try
        {
            // Step 1: Check inventory
            _logger.LogInformation("Checking inventory for order {OrderId}", request.OrderId);
            var inventoryCheck = await _client.PostAsync<InventoryCheckResult>(
                "inventory-service", "/api/inventory/check", request.Items);

            if (inventoryCheck?.Available != true)
            {
                return BadRequest(new { error = "Items not available", details = inventoryCheck?.UnavailableItems });
            }

            // Step 2: Reserve inventory
            _logger.LogInformation("Reserving inventory for order {OrderId}", request.OrderId);
            var reservation = await _client.PostAsync<ReservationResult>(
                "inventory-service", "/api/inventory/reserve", new { request.OrderId, request.Items });

            // Step 3: Process payment
            _logger.LogInformation("Processing payment for order {OrderId}", request.OrderId);
            var payment = await _client.PostAsync<PaymentResult>(
                "payment-service", "/api/payments/process", request.PaymentDetails);

            if (payment?.Status != "completed")
            {
                // Rollback inventory reservation
                await _client.PostAsync("inventory-service", "/api/inventory/release", reservation?.ReservationId);
                return BadRequest(new { error = "Payment failed", details = payment?.ErrorMessage });
            }

            // Step 4: Create shipping
            _logger.LogInformation("Creating shipping for order {OrderId}", request.OrderId);
            var shipping = await _client.PostAsync<ShippingResult>(
                "shipping-service", "/api/shipments", new
                {
                    request.OrderId,
                    request.ShippingAddress,
                    Items = request.Items
                });

            // Step 5: Send notification
            _logger.LogInformation("Sending confirmation notification for order {OrderId}", request.OrderId);
            await _client.PostAsync("notification-service", "/api/notifications/order-confirmed", new
            {
                UserId = request.UserId,
                OrderId = request.OrderId,
                TrackingNumber = shipping?.TrackingNumber
            });

            return Ok(new OrderResult
            {
                OrderId = request.OrderId,
                Status = "confirmed",
                PaymentId = payment.TransactionId,
                TrackingNumber = shipping?.TrackingNumber,
                EstimatedDelivery = shipping?.EstimatedDelivery
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process order {OrderId}", request.OrderId);
            return StatusCode(500, new { error = "Order processing failed", message = ex.Message });
        }
    }
}
```

### Parallel Service Calls

```csharp
[HttpGet("dashboard/{userId}")]
[CallsService("profile-service", RequiredScopes = new[] { "profiles:read" })]
[CallsService("activity-service", RequiredScopes = new[] { "activities:read" })]
[CallsService("recommendation-service", RequiredScopes = new[] { "recommendations:read" })]
[CallsService("notification-service", RequiredScopes = new[] { "notifications:read" })]
public async Task<IActionResult> GetUserDashboard(int userId)
{
    var tasks = new[]
    {
        _client.GetAsync<UserProfile>("profile-service", $"/api/profiles/{userId}"),
        _client.GetAsync<RecentActivity[]>("activity-service", $"/api/users/{userId}/recent"),
        _client.GetAsync<Recommendation[]>("recommendation-service", $"/api/users/{userId}/recommendations"),
        _client.GetAsync<Notification[]>("notification-service", $"/api/users/{userId}/notifications")
    };

    await Task.WhenAll(tasks);

    return Ok(new DashboardData
    {
        Profile = tasks[0].Result,
        RecentActivity = tasks[1].Result ?? Array.Empty<RecentActivity>(),
        Recommendations = tasks[2].Result ?? Array.Empty<Recommendation>(),
        Notifications = tasks[3].Result ?? Array.Empty<Notification>(),
        LoadedAt = DateTimeOffset.UtcNow
    });
}
```

## Error Handling and Resilience

### Graceful Degradation with Optional Services

```csharp
[HttpPost("create-user")]
[CallsService("user-service", RequiredScopes = new[] { "users:create" })]
[CallsService("email-service", RequiredScopes = new[] { "emails:send" }, Optional = true)]
[CallsService("analytics-service", RequiredScopes = new[] { "analytics:track" }, Optional = true)]
public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
{
    // Required service - must succeed
    var user = await _client.PostAsync<User>("user-service", "/api/users", request);
    if (user == null)
    {
        return StatusCode(500, new { error = "Failed to create user" });
    }

    var warnings = new List<string>();

    // Optional service - email notification
    try
    {
        await _client.PostAsync("email-service", "/api/emails/welcome", new { user.Id, user.Email });
        _logger.LogInformation("Welcome email sent to user {UserId}", user.Id);
    }
    catch (ServiceDiscoveryException ex)
    {
        _logger.LogWarning("Email service unavailable: {Error}", ex.Message);
        warnings.Add("Welcome email could not be sent");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to send welcome email to user {UserId}", user.Id);
        warnings.Add("Welcome email failed");
    }

    // Optional service - analytics tracking
    try
    {
        await _client.PostAsync("analytics-service", "/api/events/user-created", new
        {
            UserId = user.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Source = "api"
        });
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Analytics tracking failed for user {UserId}", user.Id);
        warnings.Add("Analytics tracking unavailable");
    }

    return Ok(new CreateUserResult
    {
        User = user,
        Warnings = warnings.ToArray()
    });
}
```

### Retry Logic and Circuit Breaker

```csharp
[HttpGet("health-check")]
[CallsService("database-service", RequiredScopes = new[] { "health:check" })]
[CallsService("cache-service", RequiredScopes = new[] { "health:check" })]
public async Task<IActionResult> HealthCheck()
{
    var healthResults = new Dictionary<string, object>();

    // Check database service with retry
    var dbHealth = await RetryHelper.ExecuteAsync(async () =>
    {
        return await _client.GetAsync<HealthStatus>("database-service", "/health");
    }, maxAttempts: 3, delay: TimeSpan.FromSeconds(1));

    healthResults["database"] = dbHealth?.Status ?? "unavailable";

    // Check cache service with circuit breaker
    try
    {
        var cacheHealth = await _client.GetAsync<HealthStatus>("cache-service", "/health");
        healthResults["cache"] = cacheHealth?.Status ?? "unavailable";
    }
    catch (ServiceDiscoveryException)
    {
        healthResults["cache"] = "service-not-found";
    }
    catch (HttpRequestException)
    {
        healthResults["cache"] = "connection-failed";
    }

    var overallHealth = healthResults.Values.All(status => status.ToString() == "healthy") ? "healthy" : "degraded";

    return Ok(new
    {
        status = overallHealth,
        services = healthResults,
        timestamp = DateTimeOffset.UtcNow
    });
}

// Helper class for retry logic
public static class RetryHelper
{
    public static async Task<T?> ExecuteAsync<T>(
        Func<Task<T?>> operation,
        int maxAttempts = 3,
        TimeSpan? delay = null) where T : class
    {
        delay ??= TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                await Task.Delay(delay.Value * attempt); // Exponential backoff
            }
        }

        throw new InvalidOperationException($"Operation failed after {maxAttempts} attempts");
    }
}
```

## Configuration Patterns

### Development Configuration

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "TestProvider": {
          "UseJwtTokens": true,
          "EnableClientCredentials": true,
          "AllowedScopes": [
            "users:read", "users:write",
            "orders:read", "orders:write",
            "payments:process",
            "inventory:check", "inventory:reserve",
            "shipping:create",
            "notifications:send",
            "analytics:track"
          ],
          "RegisteredClients": {
            "user-service": {
              "ClientId": "user-service",
              "ClientSecret": "dev-secret-user-service",
              "AllowedScopes": ["users:read", "users:write", "analytics:track"],
              "Description": "User Management Service"
            },
            "order-service": {
              "ClientId": "order-service",
              "ClientSecret": "dev-secret-order-service",
              "AllowedScopes": [
                "orders:read", "orders:write",
                "payments:process", "inventory:check",
                "shipping:create", "notifications:send"
              ],
              "Description": "Order Processing Service"
            }
          }
        },
        "Services": {
          "TokenCacheDuration": "00:45:00",
          "EnableAutoDiscovery": true,
          "ServiceEndpoints": {
            "legacy-service": "http://legacy-system.local:8080"
          }
        }
      }
    }
  }
}
```

### Production Configuration

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Services": {
          "TokenEndpoint": "https://auth.company.com/oauth/token",
          "ClientId": "${KOAN_CLIENT_ID}",
          "ClientSecret": "${KOAN_CLIENT_SECRET}",
          "TokenCacheDuration": "00:50:00",
          "ValidateServerCertificate": true,
          "MaxRetryAttempts": 3,
          "HttpTimeout": "00:00:30",
          "ServiceEndpoints": {
            "user-service": "https://users-internal.company.com",
            "order-service": "https://orders-internal.company.com",
            "payment-service": "https://payments-internal.company.com",
            "inventory-service": "https://inventory-internal.company.com",
            "shipping-service": "https://shipping-internal.company.com",
            "notification-service": "https://notifications-internal.company.com",
            "analytics-service": "https://analytics-internal.company.com"
          }
        }
      }
    }
  }
}
```

### Environment-Specific Overrides

```bash
# Docker Compose environment variables
KOAN_CLIENT_ID=my-service
KOAN_CLIENT_SECRET=super-secret-production-key
KOAN_SERVICE_USER_SERVICE_URL=http://user-service:8080
KOAN_SERVICE_ORDER_SERVICE_URL=http://order-service:8080
KOAN_SERVICE_PAYMENT_SERVICE_URL=https://payments.external.com
```

## Testing Strategies

### Unit Testing with Mocked Services

```csharp
public class OrderProcessingControllerTests
{
    private readonly Mock<IKoanServiceClient> _mockServiceClient;
    private readonly OrderProcessingController _controller;

    public OrderProcessingControllerTests()
    {
        _mockServiceClient = new Mock<IKoanServiceClient>();
        _controller = new OrderProcessingController(_mockServiceClient.Object, Mock.Of<ILogger<OrderProcessingController>>());
    }

    [Fact]
    public async Task ProcessOrder_WhenInventoryAvailable_ShouldCompleteOrder()
    {
        // Arrange
        var orderRequest = new OrderRequest { OrderId = "123", Items = new[] { "item1", "item2" } };

        _mockServiceClient.Setup(x => x.PostAsync<InventoryCheckResult>("inventory-service", "/api/inventory/check", It.IsAny<object>()))
            .ReturnsAsync(new InventoryCheckResult { Available = true });

        _mockServiceClient.Setup(x => x.PostAsync<ReservationResult>("inventory-service", "/api/inventory/reserve", It.IsAny<object>()))
            .ReturnsAsync(new ReservationResult { ReservationId = "res123" });

        _mockServiceClient.Setup(x => x.PostAsync<PaymentResult>("payment-service", "/api/payments/process", It.IsAny<object>()))
            .ReturnsAsync(new PaymentResult { Status = "completed", TransactionId = "pay123" });

        _mockServiceClient.Setup(x => x.PostAsync<ShippingResult>("shipping-service", "/api/shipments", It.IsAny<object>()))
            .ReturnsAsync(new ShippingResult { TrackingNumber = "track123" });

        // Act
        var result = await _controller.ProcessOrder(orderRequest);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var orderResult = Assert.IsType<OrderResult>(okResult.Value);
        Assert.Equal("confirmed", orderResult.Status);
        Assert.Equal("pay123", orderResult.PaymentId);
        Assert.Equal("track123", orderResult.TrackingNumber);
    }

    [Fact]
    public async Task ProcessOrder_WhenInventoryUnavailable_ShouldReturnBadRequest()
    {
        // Arrange
        var orderRequest = new OrderRequest { OrderId = "123", Items = new[] { "item1" } };

        _mockServiceClient.Setup(x => x.PostAsync<InventoryCheckResult>("inventory-service", "/api/inventory/check", It.IsAny<object>()))
            .ReturnsAsync(new InventoryCheckResult
            {
                Available = false,
                UnavailableItems = new[] { "item1" }
            });

        // Act
        var result = await _controller.ProcessOrder(orderRequest);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var error = badRequestResult.Value as dynamic;
        Assert.Equal("Items not available", error?.error?.ToString());
    }
}
```

### Integration Testing with TestServer

```csharp
public class ServiceAuthenticationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ServiceAuthenticationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Koan:Web:Auth:TestProvider:EnableClientCredentials"] = "true",
                    ["Koan:Web:Auth:TestProvider:RegisteredClients:test-service:ClientId"] = "test-service",
                    ["Koan:Web:Auth:TestProvider:RegisteredClients:test-service:ClientSecret"] = "test-secret"
                });
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task ServiceToService_AuthenticationFlow_ShouldWork()
    {
        // This would require a more complex setup with actual service endpoints
        // or mock HTTP handlers to simulate the full flow

        var response = await _client.GetAsync("/api/health-check");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var healthCheck = JsonSerializer.Deserialize<HealthCheckResponse>(content);

        Assert.NotNull(healthCheck);
        Assert.True(healthCheck.Status == "healthy" || healthCheck.Status == "degraded");
    }
}
```

## Production Deployment

### Docker Compose Configuration

```yaml
version: '3.8'

services:
  auth-provider:
    image: koan-test-provider:latest
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Koan__Web__Auth__TestProvider__EnableClientCredentials=true
      - Koan__Web__Auth__TestProvider__UseJwtTokens=true
    ports:
      - "5007:8080"
    networks:
      - koan-network

  user-service:
    image: user-service:latest
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - KOAN_CLIENT_ID=user-service
      - KOAN_CLIENT_SECRET=${USER_SERVICE_SECRET}
      - KOAN_SERVICE_AUTH_SERVICE_URL=http://auth-provider:8080
    depends_on:
      - auth-provider
    networks:
      - koan-network

  order-service:
    image: order-service:latest
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - KOAN_CLIENT_ID=order-service
      - KOAN_CLIENT_SECRET=${ORDER_SERVICE_SECRET}
      - KOAN_SERVICE_USER_SERVICE_URL=http://user-service:8080
      - KOAN_SERVICE_AUTH_SERVICE_URL=http://auth-provider:8080
    depends_on:
      - auth-provider
      - user-service
    networks:
      - koan-network

networks:
  koan-network:
    driver: bridge
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
    spec:
      containers:
      - name: order-service
        image: order-service:1.0.0
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: KOAN_CLIENT_ID
          value: "order-service"
        - name: KOAN_CLIENT_SECRET
          valueFrom:
            secretKeyRef:
              name: service-secrets
              key: order-service-secret
        - name: Koan__Web__Auth__Services__ServiceEndpoints__user-service
          value: "http://user-service.default.svc.cluster.local:8080"
        - name: Koan__Web__Auth__Services__ServiceEndpoints__payment-service
          value: "http://payment-service.default.svc.cluster.local:8080"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
---
apiVersion: v1
kind: Service
metadata:
  name: order-service
spec:
  selector:
    app: order-service
  ports:
  - port: 8080
    targetPort: 8080
```

## Advanced Scenarios

### Custom Service Client Factory

```csharp
public interface ITypedServiceClient<TService> where TService : class
{
    Task<T?> CallAsync<T>(string endpoint, object? data = null, CancellationToken ct = default) where T : class;
}

public class TypedServiceClient<TService> : ITypedServiceClient<TService> where TService : class
{
    private readonly IKoanServiceClient _client;
    private readonly string _serviceId;

    public TypedServiceClient(IKoanServiceClient client, IOptions<ServiceClientOptions> options)
    {
        _client = client;
        _serviceId = options.Value.ServiceMappings[typeof(TService).Name];
    }

    public async Task<T?> CallAsync<T>(string endpoint, object? data = null, CancellationToken ct = default) where T : class
    {
        return data != null
            ? await _client.PostAsync<T>(_serviceId, endpoint, data, ct)
            : await _client.GetAsync<T>(_serviceId, endpoint, ct);
    }
}

// Usage
public class OrderController : ControllerBase
{
    private readonly ITypedServiceClient<UserService> _userService;
    private readonly ITypedServiceClient<PaymentService> _paymentService;

    public OrderController(
        ITypedServiceClient<UserService> userService,
        ITypedServiceClient<PaymentService> paymentService)
    {
        _userService = userService;
        _paymentService = paymentService;
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var user = await _userService.CallAsync<User>($"/api/users/{request.UserId}");
        if (user == null) return BadRequest("User not found");

        var payment = await _paymentService.CallAsync<PaymentResult>("/api/payments", request.PaymentDetails);
        if (payment?.Status != "completed") return BadRequest("Payment failed");

        return Ok(new { message = "Order created successfully" });
    }
}
```

### Service Mesh Integration

```csharp
public class ServiceMeshAwareServiceDiscovery : IServiceDiscovery
{
    private readonly IServiceDiscovery _fallbackDiscovery;
    private readonly ServiceMeshOptions _options;

    public ServiceMeshAwareServiceDiscovery(IServiceDiscovery fallbackDiscovery, IOptions<ServiceMeshOptions> options)
    {
        _fallbackDiscovery = fallbackDiscovery;
        _options = options.Value;
    }

    public async Task<ServiceEndpoint> ResolveServiceAsync(string serviceId, CancellationToken ct = default)
    {
        // In a service mesh, services are typically accessible via their service name
        if (_options.EnableServiceMesh)
        {
            var meshUrl = $"http://{serviceId}.{_options.Namespace}.svc.cluster.local:{_options.DefaultPort}";
            return new ServiceEndpoint(serviceId, new Uri(meshUrl), Array.Empty<string>());
        }

        return await _fallbackDiscovery.ResolveServiceAsync(serviceId, ct);
    }

    public Task<ServiceEndpoint[]> DiscoverServicesAsync(CancellationToken ct = default)
    {
        return _fallbackDiscovery.DiscoverServicesAsync(ct);
    }

    public Task RegisterServiceAsync(ServiceRegistration registration, CancellationToken ct = default)
    {
        return _fallbackDiscovery.RegisterServiceAsync(registration, ct);
    }
}

public class ServiceMeshOptions
{
    public bool EnableServiceMesh { get; set; }
    public string Namespace { get; set; } = "default";
    public int DefaultPort { get; set; } = 8080;
}

// Register in Startup
services.AddSingleton<ServiceMeshAwareServiceDiscovery>();
services.AddSingleton<IServiceDiscovery>(provider =>
    new ServiceMeshAwareServiceDiscovery(
        new KoanServiceDiscovery(/* parameters */),
        provider.GetRequiredService<IOptions<ServiceMeshOptions>>()));
```

### Event-Driven Service Communication

```csharp
[ApiController]
[KoanService("order-event-handler")]
public class OrderEventController : ControllerBase
{
    private readonly IKoanServiceClient _client;
    private readonly ILogger<OrderEventController> _logger;

    public OrderEventController(IKoanServiceClient client, ILogger<OrderEventController> logger)
    {
        _client = client;
        _logger = logger;
    }

    [HttpPost("events/order-created")]
    [CallsService("inventory-service", RequiredScopes = new[] { "inventory:reserve" })]
    [CallsService("email-service", RequiredScopes = new[] { "emails:send" }, Optional = true)]
    [CallsService("analytics-service", RequiredScopes = new[] { "events:track" }, Optional = true)]
    public async Task<IActionResult> HandleOrderCreated([FromBody] OrderCreatedEvent orderEvent)
    {
        _logger.LogInformation("Processing order created event for order {OrderId}", orderEvent.OrderId);

        var tasks = new List<Task>();

        // Reserve inventory
        tasks.Add(_client.PostAsync("inventory-service", "/api/inventory/reserve", new
        {
            OrderId = orderEvent.OrderId,
            Items = orderEvent.Items
        }));

        // Send confirmation email (optional)
        if (!string.IsNullOrEmpty(orderEvent.CustomerEmail))
        {
            tasks.Add(SendEmailSafely(orderEvent));
        }

        // Track analytics (optional)
        tasks.Add(TrackAnalyticsSafely(orderEvent));

        await Task.WhenAll(tasks);

        return Ok(new { message = "Order created event processed", orderId = orderEvent.OrderId });
    }

    private async Task SendEmailSafely(OrderCreatedEvent orderEvent)
    {
        try
        {
            await _client.PostAsync("email-service", "/api/emails/order-confirmation", new
            {
                To = orderEvent.CustomerEmail,
                OrderId = orderEvent.OrderId,
                Items = orderEvent.Items
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send order confirmation email for order {OrderId}", orderEvent.OrderId);
        }
    }

    private async Task TrackAnalyticsSafely(OrderCreatedEvent orderEvent)
    {
        try
        {
            await _client.PostAsync("analytics-service", "/api/events", new
            {
                EventType = "order_created",
                OrderId = orderEvent.OrderId,
                CustomerId = orderEvent.CustomerId,
                Value = orderEvent.TotalAmount,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track analytics for order {OrderId}", orderEvent.OrderId);
        }
    }
}

public record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    string CustomerEmail,
    OrderItem[] Items,
    decimal TotalAmount,
    DateTimeOffset CreatedAt
);

public record OrderItem(string ProductId, string Name, int Quantity, decimal Price);
```

These comprehensive samples demonstrate the flexibility and power of the Koan.Web.Auth.Services module, from simple service calls to complex enterprise scenarios. The key principles remain consistent: declarative service definitions, automatic authentication, and graceful error handling.