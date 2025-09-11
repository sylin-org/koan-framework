using Microsoft.Extensions.DependencyInjection;

namespace Sora.Core.Hosting.Bootstrap;

/// <summary>
/// Extension methods to help modules manage duplicate invocation and module loading.
/// Provides a semantic API for modules to self-protect against duplicate registration.
/// </summary>
public static class InitializerExtensions
{
    /// <summary>
    /// Checks if this initializer has already been invoked. Useful for modules that
    /// want to implement their own duplicate protection logic.
    /// </summary>
    /// <param name="initializer">The ISoraInitializer instance</param>
    /// <returns>True if this initializer type has already been invoked</returns>
    public static bool HasBeenInvoked(this ISoraInitializer initializer)
    {
        return InitializerRegistry.Instance.IsInitializerInvoked(initializer.GetType());
    }
    
    /// <summary>
    /// Attempts to register this initializer as invoked and proceeds with initialization
    /// only if this is the first invocation.
    /// </summary>
    /// <param name="initializer">The ISoraInitializer instance</param>
    /// <param name="services">The service collection to initialize</param>
    /// <param name="initializeAction">The initialization action to perform</param>
    /// <returns>True if initialization was performed, false if skipped due to duplicate</returns>
    public static bool InitializeOnce(this ISoraInitializer initializer, 
        IServiceCollection services, 
        Action<IServiceCollection> initializeAction)
    {
        if (!InitializerRegistry.Instance.TryRegisterInitializer(initializer.GetType()))
        {
            // Already invoked - skip
            return false;
        }
        
        initializeAction(services);
        return true;
    }
    
    /// <summary>
    /// Registers a module as loaded and prevents duplicate loading.
    /// Returns true if this is the first time the module is being loaded.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="moduleName">Unique identifier for the module</param>
    /// <returns>True if this is the first loading, false if already loaded</returns>
    public static bool TryRegisterModule(this IServiceCollection services, string moduleName)
    {
        return InitializerRegistry.Instance.TryRegisterModule(moduleName);
    }
    
    /// <summary>
    /// Checks if a module has already been loaded.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="moduleName">Unique identifier for the module</param>
    /// <returns>True if the module has already been loaded</returns>
    public static bool IsModuleLoaded(this IServiceCollection services, string moduleName)
    {
        return InitializerRegistry.Instance.IsModuleLoaded(moduleName);
    }
    
    /// <summary>
    /// Conditionally executes module registration only if the module hasn't been loaded yet.
    /// This provides a fluent API for modules to protect themselves.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="moduleName">Unique identifier for the module</param>
    /// <param name="registrationAction">The registration action to perform</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddModuleOnce(this IServiceCollection services, 
        string moduleName, 
        Action<IServiceCollection> registrationAction)
    {
        if (services.TryRegisterModule(moduleName))
        {
            registrationAction(services);
        }
        
        return services;
    }
    
    /// <summary>
    /// Gets diagnostic information about all invoked initializers and loaded modules.
    /// Useful for debugging duplicate loading issues.
    /// </summary>
    public static InitializerDiagnostics GetInitializerDiagnostics(this IServiceCollection services)
    {
        var registry = InitializerRegistry.Instance;
        return new InitializerDiagnostics(
            registry.GetInvokedInitializers(),
            registry.GetLoadedModules()
        );
    }
}

/// <summary>
/// Diagnostic information about initializer and module loading.
/// </summary>
public sealed record InitializerDiagnostics(
    IReadOnlySet<string> InvokedInitializers,
    IReadOnlySet<string> LoadedModules
);