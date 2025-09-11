# Migration Guide: From Hardcoded Logging to Contextual Logging

This guide shows how to migrate from the old hardcoded string replacement approach to the new Sora contextual logging system.

## Before (Problematic Approach)

```csharp
// OLD: Hardcoded prefixes and Console.WriteLine
Console.WriteLine($"[MongoDB][AUTO-DETECT] {msg}");
Console.WriteLine($"[SoraEnv][INFO] Environment snapshot:");
Console.WriteLine($"[Messaging][Phase1] Handler registered: {handlerName}");
```

## After (Sora Framework Approach)

### 1. Register Your Module's Context

```csharp
// In your module's ServiceCollectionExtensions
[assembly: SoraLogContext("sora:discover", "Discovery", 
    MessagePrefixes = new[] { "[MongoDB][AUTO-DETECT]" },
    CategoryPatterns = new[] { "MongoOptions" })]

public static class MongoServiceCollectionExtensions
{
    public static IServiceCollection AddMongo(this IServiceCollection services)
    {
        // Register your context explicitly if needed
        services.Configure<SoraLogContextRegistry>(registry =>
        {
            registry.RegisterContext(new SoraLogContext(
                "sora:discover",
                "Discovery", 
                messagePrefixes: ["[MongoDB][AUTO-DETECT]"],
                categoryPatterns: ["MongoOptions"]
            ));
        });
        
        return services;
    }
}
```

### 2. Use Semantic Logging APIs

```csharp
// NEW: Semantic logging with injected ILogger
public class MongoOptionsConfigurator
{
    private readonly ILogger<MongoOptionsConfigurator> _logger;
    
    public MongoOptionsConfigurator(ILogger<MongoOptionsConfigurator> logger)
    {
        _logger = logger;
    }
    
    public void Configure(MongoOptions options)
    {
        // Clean, semantic logging - no prefixes needed
        _logger.LogInformation("MongoDB Auto-Configuration Started");
        _logger.LogInformation("Environment: {Environment}, InContainer: {InContainer}", 
            SoraEnv.EnvironmentName, SoraEnv.InContainer);
        _logger.LogInformation("Connection: {ConnectionString}, Database: {Database}", 
            options.ConnectionString, options.Database);
    }
}
```

### 3. For Background Services

```csharp
// NEW: Use contextual logger from registry
public class MessagingLifecycleService : SoraFluentServiceBase
{
    private readonly ILogger _contextualLogger;
    
    public MessagingLifecycleService(
        ILogger<MessagingLifecycleService> logger,
        SoraLogContextRegistry contextRegistry)
        : base(logger, configuration)
    {
        // Get a contextual logger for messaging
        _contextualLogger = contextRegistry.CreateContextualLogger(logger, "sora:messaging");
    }
    
    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        // Clean semantic logging - context is automatically applied
        _contextualLogger.LogInformation("Phase 1: {HandlerCount} handlers registered", handlerCount);
        _contextualLogger.LogInformation("Phase 2: Selected provider '{ProviderName}'", provider.Name);
    }
}
```

## Benefits of the New Approach

### ✅ Separation of Concerns
- Modules define their own contexts
- Formatter doesn't need to know about specific modules
- Easy to add new modules without changing core logging

### ✅ Semantic Developer Experience  
- Clean, readable logging code
- No manual prefix management
- Type-safe structured logging

### ✅ Follows Sora Framework Principles
- Auto-registration via attributes or DI
- Minimal scaffolding - framework handles formatting
- DRY - no duplicated prefix logic

### ✅ Maintainable and Extensible
- Add new contexts without touching core formatter
- Easy to test and mock
- Clear ownership of logging contexts

## Output Comparison

### Before (Inconsistent)
```
[MongoDB][AUTO-DETECT] Connection established
[SoraEnv][INFO] Environment: Development
[Messaging][Phase1] Handler registered: OrderHandler
Microsoft.AspNetCore.Hosting.Diagnostics[15] Application started
```

### After (Consistent)
```
│ I 09:45:12 Discovery      Connection established
│ I 09:45:12 Initialization  Environment: Development  
│ I 09:45:12 Messaging       Handler registered: OrderHandler
│ I 09:45:12 HTTP Server     Application started
```

The new approach achieves the same elegant columnar output while maintaining clean architecture principles.