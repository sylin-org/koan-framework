using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Canon.Attributes;

namespace Koan.Canon.Context;

/// <summary>
/// Hosted service wrapper that automatically sets CanonContext for adapter services
/// based on their [CanonAdapter] attribute.
/// </summary>
public class CanonAdapterContextService<T> : IHostedService where T : BackgroundService
{
    private readonly T _innerService;
    private readonly ILogger<CanonAdapterContextService<T>> _logger;
    private readonly CanonContext? _CanonContext;
    private IDisposable? _contextScope;
    
    public CanonAdapterContextService(T innerService, ILogger<CanonAdapterContextService<T>> logger)
    {
        _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        _logger = logger;
        
        // Extract CanonContext from the CanonAdapter attribute
        var adapterAttr = typeof(T).GetCustomAttribute<CanonAdapterAttribute>(inherit: true);
        if (adapterAttr != null)
        {
            _CanonContext = new CanonContext(adapterAttr.System, adapterAttr.Adapter, adapterAttr.DefaultSource);
            _logger.LogDebug("[CanonContext] Created context for adapter: System={System}, Adapter={Adapter}", 
                adapterAttr.System, adapterAttr.Adapter);
        }
        else
        {
            _logger.LogWarning("[CanonContext] No CanonAdapter attribute found on {ServiceType}", typeof(T).Name);
        }
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Set the Canon context before starting the adapter service
        if (_CanonContext != null)
        {
            _contextScope = CanonContext.Push(_CanonContext);
            _logger.LogInformation("[CanonContext] Set context for adapter: {System}:{Adapter}", 
                _CanonContext.System, _CanonContext.Adapter);
        }
        
        // Start the actual adapter service
        await _innerService.StartAsync(cancellationToken);
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Stop the adapter service first
            await _innerService.StopAsync(cancellationToken);
        }
        finally
        {
            // Clean up the context
            _contextScope?.Dispose();
            _contextScope = null;
            
            if (_CanonContext != null)
            {
                _logger.LogInformation("[CanonContext] Cleared context for adapter: {System}:{Adapter}", 
                    _CanonContext.System, _CanonContext.Adapter);
            }
        }
    }
}

/// <summary>
/// Extension methods for registering adapter services with automatic CanonContext management.
/// </summary>
public static class CanonAdapterServiceExtensions
{
    /// <summary>
    /// Registers an adapter service with automatic CanonContext management.
    /// The service will have its CanonContext set based on the [CanonAdapter] attribute.
    /// </summary>
    public static IServiceCollection AddCanonAdapter<T>(this IServiceCollection services) 
        where T : BackgroundService
    {
        // Register the actual adapter service
        services.AddSingleton<T>();
        
        // Register the context wrapper as the hosted service
        services.AddSingleton<IHostedService>(sp =>
        {
            var adapterService = sp.GetRequiredService<T>();
            var logger = sp.GetRequiredService<ILogger<CanonAdapterContextService<T>>>();
            return new CanonAdapterContextService<T>(adapterService, logger);
        });
        
        return services;
    }
}

