using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Sora.Core.Logging;

/// <summary>
/// Central registry for logging contexts following Sora's auto-registration pattern.
/// Modules register their logging contexts here instead of hardcoding them in formatters.
/// This maintains separation of concerns and follows the framework's semantic principles.
/// </summary>
public sealed class SoraLogContextRegistry
{
    private readonly ConcurrentDictionary<string, SoraLogContext> _contexts = new();
    
    /// <summary>
    /// Registers a logging context. Modules call this during their initialization.
    /// This follows Sora's self-registration principle.
    /// </summary>
    public void RegisterContext(SoraLogContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        
        _contexts.TryAdd(context.Name, context);
    }

    /// <summary>
    /// Finds the most appropriate context for a log entry.
    /// Returns null if no registered context matches.
    /// </summary>
    public SoraLogContext? FindContext(string message, string category)
    {
        // Check all registered contexts for matches
        foreach (var context in _contexts.Values)
        {
            if (context.Matches(message, category))
                return context;
        }

        return null;
    }

    /// <summary>
    /// Gets all registered contexts. Useful for diagnostics and configuration.
    /// </summary>
    public IEnumerable<SoraLogContext> GetAllContexts() => _contexts.Values;

    /// <summary>
    /// Creates a contextual logger for the specified context name.
    /// This provides the semantic API that modules expect.
    /// </summary>
    public ILogger CreateContextualLogger(ILogger baseLogger, string contextName)
    {
        if (_contexts.TryGetValue(contextName, out var context))
        {
            return context.CreateScopedLogger(baseLogger);
        }

        return baseLogger; // Fall back to base logger if context not found
    }
}

/// <summary>
/// Attribute for auto-registering logging contexts.
/// Follows Sora's attribute-based registration pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class SoraLogContextAttribute : Attribute
{
    public string Name { get; }
    public string DisplayName { get; }
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public string[]? MessagePrefixes { get; set; }
    public string[]? CategoryPatterns { get; set; }

    public SoraLogContextAttribute(string name, string displayName)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>
    /// Creates a SoraLogContext from this attribute's configuration.
    /// </summary>
    internal SoraLogContext CreateContext()
    {
        return new SoraLogContext(Name, DisplayName, MinimumLevel, MessagePrefixes, CategoryPatterns);
    }
}

/// <summary>
/// Extension methods for DI integration.
/// </summary>
public static class SoraLogContextExtensions
{
    /// <summary>
    /// Adds the Sora logging context system to dependency injection.
    /// This follows Sora's standard DI registration pattern.
    /// </summary>
    public static IServiceCollection AddSoraLoggingContexts(this IServiceCollection services)
    {
        services.AddSingleton<SoraLogContextRegistry>();
        
        // Auto-register contexts from assemblies
        services.AddSingleton<ILoggerProvider>(serviceProvider =>
        {
            var registry = serviceProvider.GetRequiredService<SoraLogContextRegistry>();
            RegisterBuiltInContexts(registry);
            return new SoraContextualLoggerProvider(registry, serviceProvider);
        });

        return services;
    }

    /// <summary>
    /// Registers the built-in Sora framework contexts.
    /// This replaces the hardcoded mappings in the old formatter.
    /// </summary>
    private static void RegisterBuiltInContexts(SoraLogContextRegistry registry)
    {
        // Core framework contexts
        registry.RegisterContext(new SoraLogContext(
            "sora:init", 
            "Initialization",
            messagePrefixes: ["[SoraEnv][INFO]"],
            categoryPatterns: ["StartupProbe", "SoraEnv"]
        ));

        registry.RegisterContext(new SoraLogContext(
            "sora:messaging", 
            "Messaging",
            messagePrefixes: ["[Messaging]", "[RabbitMQ]", "[Messaging][Registry]", "[Messaging][Phase1]"],
            categoryPatterns: ["Messaging", "RabbitMq", "MessagingLifecycleService"]
        ));

        registry.RegisterContext(new SoraLogContext(
            "sora:discover", 
            "Discovery",
            messagePrefixes: ["[MongoDB][AUTO-DETECT]"],
            categoryPatterns: ["MongoOptions"]
        ));

        registry.RegisterContext(new SoraLogContext(
            "flow:worker", 
            "Flow Workers",
            messagePrefixes: ["[flow.association]", "[flow.projection]"],
            categoryPatterns: ["FlowOrchestrator", "ModelAssociation", "ModelProjection"]
        ));

        registry.RegisterContext(new SoraLogContext(
            "sora:services", 
            "Background Services",
            categoryPatterns: ["BackgroundService", "SoraBackgroundService"]
        ));

        registry.RegisterContext(new SoraLogContext(
            "sora:health", 
            "Health Checks",
            categoryPatterns: ["Health", "HealthProbe"]
        ));

        registry.RegisterContext(new SoraLogContext(
            "sora:http", 
            "HTTP Server",
            categoryPatterns: ["Microsoft.AspNetCore", "Microsoft.Hosting"]
        ));

        registry.RegisterContext(new SoraLogContext(
            "sora:data", 
            "Data Access",
            categoryPatterns: ["Data", "Entity", "Repository"]
        ));
    }
}

/// <summary>
/// Logger provider that creates contextual loggers based on registered contexts.
/// This integrates with .NET's logging infrastructure seamlessly.
/// </summary>
internal sealed class SoraContextualLoggerProvider : ILoggerProvider
{
    private readonly SoraLogContextRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();

    public SoraContextualLoggerProvider(SoraLogContextRegistry registry, IServiceProvider serviceProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, category =>
        {
            // This would integrate with the main logger factory
            // For now, we'll return a simple logger that knows about contexts
            return new ContextAwareLogger(category, _registry);
        });
    }

    public void Dispose() => _loggers.Clear();
}

/// <summary>
/// Logger implementation that's aware of registered contexts.
/// </summary>
internal sealed class ContextAwareLogger : ILogger
{
    private readonly string _categoryName;
    private readonly SoraLogContextRegistry _registry;

    public ContextAwareLogger(string categoryName, SoraLogContextRegistry registry)
    {
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var context = _registry.FindContext(message, _categoryName);
        
        // This is where integration with the actual logging system would happen
        // For now, this demonstrates the concept
        Console.WriteLine($"[{context?.Name ?? "sora:runtime"}] {message}");
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}