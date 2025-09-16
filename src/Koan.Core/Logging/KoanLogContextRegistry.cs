using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Koan.Core.Logging;

/// <summary>
/// Central registry for logging contexts following Koan's auto-registration pattern.
/// Modules register their logging contexts here instead of hardcoding them in formatters.
/// This maintains separation of concerns and follows the framework's semantic principles.
/// </summary>
public sealed class KoanLogContextRegistry
{
    private readonly ConcurrentDictionary<string, KoanLogContext> _contexts = new();
    
    /// <summary>
    /// Registers a logging context. Modules call this during their initialization.
    /// This follows Koan's self-registration principle.
    /// </summary>
    public void RegisterContext(KoanLogContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        
        _contexts.TryAdd(context.Name, context);
    }

    /// <summary>
    /// Finds the most appropriate context for a log entry.
    /// Returns null if no registered context matches.
    /// </summary>
    public KoanLogContext? FindContext(string message, string category)
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
    public IEnumerable<KoanLogContext> GetAllContexts() => _contexts.Values;

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
/// Follows Koan's attribute-based registration pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class KoanLogContextAttribute : Attribute
{
    public string Name { get; }
    public string DisplayName { get; }
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public string[]? MessagePrefixes { get; set; }
    public string[]? CategoryPatterns { get; set; }

    public KoanLogContextAttribute(string name, string displayName)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>
    /// Creates a KoanLogContext from this attribute's configuration.
    /// </summary>
    internal KoanLogContext CreateContext()
    {
        return new KoanLogContext(Name, DisplayName, MinimumLevel, MessagePrefixes, CategoryPatterns);
    }
}

/// <summary>
/// Extension methods for DI integration.
/// </summary>
public static class KoanLogContextExtensions
{
    /// <summary>
    /// Adds the Koan logging context system to dependency injection.
    /// This follows Koan's standard DI registration pattern.
    /// </summary>
    public static IServiceCollection AddKoanLoggingContexts(this IServiceCollection services)
    {
        services.AddSingleton<KoanLogContextRegistry>();
        
        // Auto-register contexts from assemblies
        services.AddSingleton<ILoggerProvider>(serviceProvider =>
        {
            var registry = serviceProvider.GetRequiredService<KoanLogContextRegistry>();
            RegisterBuiltInContexts(registry);
            return new KoanContextualLoggerProvider(registry, serviceProvider);
        });

        return services;
    }

    /// <summary>
    /// Registers the built-in Koan framework contexts.
    /// This replaces the hardcoded mappings in the old formatter.
    /// </summary>
    private static void RegisterBuiltInContexts(KoanLogContextRegistry registry)
    {
        // Core framework contexts
        registry.RegisterContext(new KoanLogContext(
            "Koan:init", 
            "Initialization",
            messagePrefixes: ["[KoanEnv][INFO]"],
            categoryPatterns: ["StartupProbe", "KoanEnv"]
        ));

        registry.RegisterContext(new KoanLogContext(
            "Koan:messaging", 
            "Messaging",
            messagePrefixes: ["[Messaging]", "[RabbitMQ]", "[Messaging][Registry]", "[Messaging][Phase1]"],
            categoryPatterns: ["Messaging", "RabbitMq", "MessagingLifecycleService"]
        ));

        registry.RegisterContext(new KoanLogContext(
            "Koan:discover", 
            "Discovery",
            messagePrefixes: ["[MongoDB][AUTO-DETECT]"],
            categoryPatterns: ["MongoOptions"]
        ));

        registry.RegisterContext(new KoanLogContext(
            "flow:worker", 
            "Flow Workers",
            messagePrefixes: ["[flow.association]", "[flow.projection]"],
            categoryPatterns: ["FlowOrchestrator", "ModelAssociation", "ModelProjection"]
        ));

        registry.RegisterContext(new KoanLogContext(
            "Koan:services", 
            "Background Services",
            categoryPatterns: ["BackgroundService", "KoanBackgroundService"]
        ));

        registry.RegisterContext(new KoanLogContext(
            "Koan:health", 
            "Health Checks",
            categoryPatterns: ["Health", "HealthProbe"]
        ));

        registry.RegisterContext(new KoanLogContext(
            "Koan:http", 
            "HTTP Server",
            categoryPatterns: ["Microsoft.AspNetCore", "Microsoft.Hosting"]
        ));

        registry.RegisterContext(new KoanLogContext(
            "Koan:data", 
            "Data Access",
            categoryPatterns: ["Data", "Entity", "Repository"]
        ));
    }
}

/// <summary>
/// Logger provider that creates contextual loggers based on registered contexts.
/// This integrates with .NET's logging infrastructure seamlessly.
/// </summary>
internal sealed class KoanContextualLoggerProvider : ILoggerProvider
{
    private readonly KoanLogContextRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new();

    public KoanContextualLoggerProvider(KoanLogContextRegistry registry, IServiceProvider serviceProvider)
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
    private readonly KoanLogContextRegistry _registry;

    public ContextAwareLogger(string categoryName, KoanLogContextRegistry registry)
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
        Console.WriteLine($"[{context?.Name ?? "Koan:runtime"}] {message}");
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}