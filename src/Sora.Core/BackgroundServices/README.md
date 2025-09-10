# Sora Background Services

Sora Background Services provides elegant background service development with auto-discovery, fluent APIs, and seamless integration with the Sora Framework ecosystem.

## Quick Start

### 1. Create a Background Service

```csharp
[SoraBackgroundService]
public class MyService : SoraBackgroundServiceBase
{
    public MyService(ILogger<MyService> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            Logger.LogInformation("Service is running...");
            // Your work here
        }
    }
}
```

### 2. Create a Periodic Service

```csharp
[SoraPeriodicService(IntervalSeconds = 3600)]
public class CleanupService : SoraPokablePeriodicServiceBase
{
    public override TimeSpan Period => TimeSpan.FromHours(1);
    
    public CleanupService(ILogger<CleanupService> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    protected override async Task ExecutePeriodicAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Running cleanup...");
        // Cleanup logic here
    }
}
```

### 3. Create a Fluent Service with Actions and Events

```csharp
[ServiceEvent("WorkCompleted")]
[ServiceEvent("WorkFailed")]
public class WorkerService : SoraFluentServiceBase
{
    public WorkerService(ILogger<WorkerService> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    [ServiceAction("process-item")]
    public async Task ProcessItemAsync(string itemId, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Processing item: {ItemId}", itemId);
        
        try
        {
            // Work processing logic
            await Task.Delay(5000, cancellationToken);
            
            await EmitEventAsync("WorkCompleted", new { ItemId = itemId });
        }
        catch (Exception ex)
        {
            await EmitEventAsync("WorkFailed", new { ItemId = itemId, Error = ex.Message });
            throw;
        }
    }

    public override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
```

### 4. Use the Fluent API

```csharp
// Trigger service actions
await SoraServices
    .Do<WorkerService>("process-item", "item-123")
    .WithPriority(10)
    .ExecuteAsync();

// Subscribe to events (chainable!)
await SoraServices
    .On<WorkerService>("WorkCompleted").Do<WorkCompletedArgs>(async args => 
        Logger.LogInformation("Work completed: {ItemId}", args.ItemId))
    .On("WorkFailed").Do<WorkFailedArgs>(async args => 
        Logger.LogError("Work failed: {ItemId} - {Error}", args.ItemId, args.Error))
    .SubscribeAsync();
```

### 5. Registration (Automatic!)

Services are automatically discovered and registered when you call:

```csharp
// Program.cs
builder.Services.AddSora(); // This discovers ALL background services automatically!
```

## Features

- ✅ **Auto-Discovery**: Services are automatically found and registered
- ✅ **Fluent API**: Beautiful, chainable syntax for service communication
- ✅ **Event-Driven**: Rich event subscription and emission patterns
- ✅ **Health Checks**: Built-in integration with Sora's health system
- ✅ **Pokeable Services**: Services can be triggered on-demand
- ✅ **Multiple Patterns**: Startup, periodic, continuous, and event-driven services
- ✅ **Configuration**: Attribute-based and appsettings.json configuration
- ✅ **Type-Safe**: Full IntelliSense and compile-time checking

## Configuration

```json
{
  "Sora": {
    "BackgroundServices": {
      "Enabled": true,
      "StartupTimeoutSeconds": 120,
      "Services": {
        "WorkerService": {
          "Enabled": true,
          "Settings": {
            "MaxConcurrentItems": 5
          }
        }
      }
    }
  }
}
```

## More Examples

See the `Examples/` folder for comprehensive examples of all service patterns and fluent API usage.