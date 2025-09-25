# Bootstrap Failures - Troubleshooting Guide

**Document Type**: TROUBLESHOOTING
**Target Audience**: Developers, DevOps Engineers
**Last Updated**: 2025-01-27
**Framework Version**: v0.2.18+

---

## Problem: Application Startup Tasks Not Executing

### Common Symptoms
- Application starts but missing essential data or configuration
- Endpoints returning empty results when initial data is expected
- "Reference data not found" or similar initialization errors
- Startup tasks not executing or being discovered
- Database schemas created but no seed data populated

---

## 🏗️ Understanding Koan Bootstrap System

### Conceptual Framework

The Koan Framework provides an **automatic startup task system** where you can define tasks that run during application initialization:

```csharp
// Any class implementing IOnStartup will be discovered and executed
public class MyBootstrapTask : IScheduledTask, IOnStartup
{
    public string Id => "my-app:bootstrap";

    public async Task RunAsync(CancellationToken ct)
    {
        // Your initialization logic here
        await EnsureReferenceDataAsync(ct);
        await SetupRequiredConfigurationAsync(ct);
    }
}
```

**Key Principles:**
- **Automatic Discovery**: Framework finds classes implementing `IOnStartup`
- **Dependency Injection**: Tasks can inject any registered services
- **Entity Integration**: Tasks can use Entity<> patterns for data operations
- **Conditional Logic**: Tasks should handle "already initialized" scenarios

---

## 🚨 Quick Diagnosis Checklist

### 1. Check Task Discovery
```bash
# Look for scheduling system startup
docker logs [api-container] | grep -E "(SchedulingOrchestrator|task.*runner)"

# Expected patterns:
# ✅ "Scheduling orchestrator started with N task runners" (N > 0)
# ❌ "Scheduling orchestrator started with 0 task runners"
# ❌ No scheduling logs at all
```

### 2. Verify Task Registration
```csharp
// Ensure your bootstrap task implements required interfaces
public class YourBootstrapTask : IScheduledTask, IOnStartup  // Both required
{
    public string Id => "your-app:bootstrap";  // Unique identifier
    // Implementation...
}
```

### 3. Test Task Execution
```bash
# Look for your specific task logs
docker logs [api-container] | grep -E "([YourTaskName]|bootstrap.*[your-app])"

# Check for task-specific logging you added
docker logs [api-container] | grep "your-bootstrap-logic"
```

### 4. Verify Results
```bash
# Test endpoints that should be populated by bootstrap
curl -s http://localhost:[port]/api/[your-entities]

# Expected: Data populated by bootstrap, not empty []
```

---

## 🔍 Common Bootstrap Patterns

### Pattern 1: Reference Data Seeding

```csharp
public class ReferenceDataBootstrap : IScheduledTask, IOnStartup
{
    public string Id => "app:reference-data";

    public async Task RunAsync(CancellationToken ct)
    {
        // Check if data already exists
        var existingCategories = await Category.All(ct);
        if (existingCategories.Any())
        {
            _logger?.LogInformation("Reference data already seeded");
            return;
        }

        // Create reference data using Entity<> patterns
        var categories = new[]
        {
            new Category { Name = "Electronics", DisplayName = "Electronics" },
            new Category { Name = "Books", DisplayName = "Books" },
        };

        // Leverage auto-provisioning system
        await categories.Save(ct);

        _logger?.LogInformation("Seeded {Count} categories", categories.Length);
    }
}
```

### Pattern 2: Configuration Setup

```csharp
public class ConfigurationBootstrap : IScheduledTask, IOnStartup
{
    private readonly IConfiguration _config;

    public string Id => "app:configuration";

    public async Task RunAsync(CancellationToken ct)
    {
        // Set up application-specific configuration
        await EnsureApplicationSettingsAsync(ct);
        await InitializeExternalServicesAsync(ct);
        await ValidateRequiredConfigurationAsync(ct);
    }
}
```

### Pattern 3: External System Integration

```csharp
public class IntegrationBootstrap : IScheduledTask, IOnStartup
{
    public string Id => "app:integrations";

    public async Task RunAsync(CancellationToken ct)
    {
        // Initialize external service connections
        await RegisterWithServiceDiscoveryAsync(ct);
        await WarmUpExternalApiConnectionsAsync(ct);
        await SynchronizeRemoteDataAsync(ct);
    }
}
```

---

## 🛠️ Troubleshooting Steps

### Step 1: Verify Task Discovery

#### Check if Tasks are Found
```bash
# Monitor discovery during startup
docker logs [api-container] --follow | grep -E "(discover|register|IOnStartup)"

# Look for service registration logs
docker logs [api-container] | grep -E "(services.*register|DI.*container)"
```

#### Verify Task Implementation
```csharp
// Ensure your task is properly implemented
[assembly: System.Reflection.AssemblyMetadata("KoanAutoDiscovery", "true")]  // Optional: helps discovery

public class MyBootstrapTask : IScheduledTask, IOnStartup  // BOTH interfaces required
{
    // Required property
    public string Id => "my-unique-task-id";

    // Required method
    public async Task RunAsync(CancellationToken ct)
    {
        // Your logic here
    }
}
```

### Step 2: Debug Task Execution

#### Add Comprehensive Logging
```csharp
public async Task RunAsync(CancellationToken ct)
{
    _logger?.LogInformation("Starting bootstrap for {TaskId}", Id);

    try
    {
        // Your bootstrap logic with detailed logging
        _logger?.LogDebug("Checking existing data...");
        var existing = await MyEntity.All(ct);

        if (existing.Any())
        {
            _logger?.LogInformation("Bootstrap skipped - {Count} records already exist", existing.Count);
            return;
        }

        _logger?.LogInformation("No existing data found, proceeding with bootstrap");

        // Bootstrap logic here
        await CreateInitialDataAsync(ct);

        _logger?.LogInformation("Bootstrap completed successfully for {TaskId}", Id);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Bootstrap failed for {TaskId}: {Error}", Id, ex.Message);
        throw; // Let framework handle the failure
    }
}
```

#### Monitor Execution
```bash
# Watch for your specific logs
docker logs [api-container] --follow | grep -E "(Starting bootstrap|Bootstrap completed|Bootstrap failed)"

# Check timing
docker logs [api-container] | grep -E "(bootstrap|ready|Healthy)" | head -20
```

### Step 3: Verify Dependencies

#### Ensure Adapter Readiness
```csharp
public class MyBootstrapTask : IScheduledTask, IOnStartup
{
    // Inject health checks to verify readiness
    private readonly IHealthCheckService _healthChecks;

    public async Task RunAsync(CancellationToken ct)
    {
        // Verify system is ready before bootstrap
        var healthResult = await _healthChecks.CheckHealthAsync(ct);
        if (healthResult.Status != HealthStatus.Healthy)
        {
            _logger?.LogWarning("System not healthy, skipping bootstrap: {Status}", healthResult.Status);
            return;
        }

        // Proceed with bootstrap
        await DoBootstrapLogicAsync(ct);
    }
}
```

#### Test Entity Operations
```bash
# Verify basic Entity<> operations work before bootstrap
curl -X GET http://localhost:[port]/health
# Should return healthy status

# Test a simple entity operation
curl -X GET http://localhost:[port]/api/[some-entity]
# Should work (may be empty, but shouldn't error)
```

### Step 4: Handle Bootstrap Conditions

#### Implement Idempotent Logic
```csharp
public async Task RunAsync(CancellationToken ct)
{
    // ALWAYS check if bootstrap is needed
    if (await IsBootstrapRequiredAsync(ct))
    {
        await PerformBootstrapAsync(ct);
        await VerifyBootstrapCompletionAsync(ct);
    }
    else
    {
        _logger?.LogInformation("Bootstrap not required - system already initialized");
    }
}

private async Task<bool> IsBootstrapRequiredAsync(CancellationToken ct)
{
    // Define your specific conditions
    var hasReferenceData = await ReferenceEntity.Any(ct);
    var hasConfiguration = await ConfigurationEntity.Any(ct);

    return !hasReferenceData || !hasConfiguration;
}
```

---

## 🚀 Resolution Strategies

### Strategy 1: Container Restart (First Try)

```bash
# Restart to trigger fresh task discovery
docker compose restart [api-service]

# Monitor bootstrap execution
docker logs [api-container] --follow | grep bootstrap

# Verify results after completion
curl -s http://localhost:[port]/api/[your-entities]
```

**When this works**: Task discovery timing issues, cached state problems

### Strategy 2: Enable Debug Logging

```json
// In appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "YourNamespace.Tasks": "Debug",
      "Koan.Scheduling": "Debug",
      "Microsoft.Extensions.Hosting": "Debug"
    }
  }
}
```

### Strategy 3: Manual Bootstrap Verification

```csharp
// Create a controller for manual bootstrap testing
[ApiController]
[Route("api/[controller]")]
public class BootstrapController : ControllerBase
{
    private readonly MyBootstrapTask _bootstrapTask;

    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerBootstrap(CancellationToken ct)
    {
        try
        {
            await _bootstrapTask.RunAsync(ct);
            return Ok(new { status = "Bootstrap completed" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
```

### Strategy 4: Dependency-Aware Bootstrap

```csharp
public class SmartBootstrapTask : IScheduledTask, IOnStartup
{
    public async Task RunAsync(CancellationToken ct)
    {
        // Wait for dependencies to be ready
        await WaitForDependenciesAsync(ct);

        // Proceed with bootstrap
        await DoActualBootstrapAsync(ct);
    }

    private async Task WaitForDependenciesAsync(CancellationToken ct)
    {
        var maxWait = TimeSpan.FromMinutes(2);
        var interval = TimeSpan.FromSeconds(5);
        var deadline = DateTime.UtcNow.Add(maxWait);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // Test if dependencies are ready
                await TestEntity.All(ct);  // Simple test operation
                _logger?.LogInformation("Dependencies ready, proceeding with bootstrap");
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Waiting for dependencies: {Error}", ex.Message);
                await Task.Delay(interval, ct);
            }
        }

        throw new InvalidOperationException("Dependencies not ready within timeout");
    }
}
```

---

## 🎯 Best Practices for Bootstrap Tasks

### 1. **Design for Reliability**
```csharp
public async Task RunAsync(CancellationToken ct)
{
    // ✅ Always check if bootstrap is needed
    if (await AlreadyBootstrappedAsync(ct)) return;

    // ✅ Use comprehensive logging
    _logger?.LogInformation("Starting bootstrap for {Component}", "MyComponent");

    // ✅ Handle exceptions gracefully
    try
    {
        await DoBootstrapAsync(ct);
    }
    catch (Exception ex) when (IsRetryableError(ex))
    {
        _logger?.LogWarning("Bootstrap failed, will retry: {Error}", ex.Message);
        throw;
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Bootstrap failed permanently: {Error}", ex.Message);
        // Decide: throw or continue with degraded state?
    }
}
```

### 2. **Leverage Framework Patterns**
```csharp
public async Task RunAsync(CancellationToken ct)
{
    // ✅ Use Entity<> patterns
    var entities = new[]
    {
        new MyEntity { Name = "Default" },
        new MyEntity { Name = "System" }
    };

    // ✅ Leverage auto-provisioning
    await entities.Save(ct);  // Collections created automatically

    // ✅ Use framework services
    var healthCheck = await _healthService.CheckHealthAsync(ct);

    // ✅ Integrate with configuration
    var settings = _configuration.GetSection("MyApp").Get<MySettings>();
}
```

### 3. **Test Bootstrap Logic**
```csharp
[Test]
public async Task Bootstrap_Should_Create_Reference_Data()
{
    // Arrange: Clean database
    await ClearAllDataAsync();

    // Act: Run bootstrap
    var bootstrapTask = new MyBootstrapTask(_logger, _services);
    await bootstrapTask.RunAsync(CancellationToken.None);

    // Assert: Data created
    var entities = await MyEntity.All();
    Assert.IsTrue(entities.Any());

    // Assert: Idempotent
    await bootstrapTask.RunAsync(CancellationToken.None); // Run again
    var entitiesAfterSecondRun = await MyEntity.All();
    Assert.AreEqual(entities.Count, entitiesAfterSecondRun.Count);
}
```

---

## 🔍 Example: S5.Recs Bootstrap Implementation

*Reference implementation from the S5.Recs sample:*

```csharp
// S5BootstrapTask demonstrates real-world bootstrap patterns
internal sealed class S5BootstrapTask : IScheduledTask, IOnStartup, IHasTimeout
{
    public string Id => "s5:bootstrap";
    public TimeSpan Timeout => TimeSpan.FromMinutes(5);

    public async Task RunAsync(CancellationToken ct)
    {
        // Pattern: Check existing data before proceeding
        var (media, _, vectors) = await _seeder.GetStatsAsync(ct);

        // Pattern: Skip if system already initialized
        if (media > 0 && vectors > 0)
        {
            _logger?.LogInformation("S5 bootstrap: dataset already present. Skipping seeding.");
            return;
        }

        // Pattern: Conditional bootstrap based on system state
        if (media == 0)
        {
            // Pattern: Use dedicated service for complex bootstrap
            var bootstrapper = new DataBootstrapper();
            await bootstrapper.SeedReferenceDataAsync(ct);

            // Pattern: Verify bootstrap results
            var mediaTypes = await MediaType.All(ct);
            if (mediaTypes.Any())
            {
                _logger?.LogInformation("Reference data seeded successfully. Media types: {Names}",
                    string.Join(", ", mediaTypes.Select(mt => $"'{mt.Name}'")));
            }
            else
            {
                _logger?.LogWarning("Bootstrap failed - no MediaTypes created.");
            }
        }
    }
}
```

**Key Patterns Demonstrated:**
- Conditional execution based on system state
- Verification of bootstrap results
- Comprehensive logging for troubleshooting
- Integration with Entity<> patterns
- Timeout specification for long-running operations

---

## 📞 When to Escalate

**Escalate to Framework Team if:**
- Task discovery consistently fails across environments
- Bootstrap works in development but fails in production
- Framework scheduling system not initializing properly
- Entity<> operations fail during bootstrap despite adapter health
- Multiple applications showing similar bootstrap patterns

**Include in escalation:**
- Complete bootstrap task implementation
- Application startup logs from container start to completion
- Entity<> operation test results
- Environment configuration (docker-compose, appsettings)
- Results of manual bootstrap trigger attempts

---

## 📚 Related Documentation

- [Bootstrap Lifecycle](../deep-dive/bootstrap-lifecycle.md) - Understanding the complete bootstrap system architecture
- [Adapter Connection Issues](adapter-connection-issues.md) - Resolving dependency issues that prevent bootstrap
- [Auto-Provisioning System](../deep-dive/auto-provisioning-system.md) - Entity<> provisioning integration with bootstrap
- [Framework Principles](../../architecture/principles.md) - Understanding framework initialization philosophy

---

*Bootstrap tasks enable applications to achieve "zero-configuration" startup by automatically initializing required data and configuration. Understanding these patterns enables reliable, self-healing applications that work correctly from first deployment.*